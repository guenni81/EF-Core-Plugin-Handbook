using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Runtime;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Testcontainers.MsSql;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests;

/// <summary>Tenant-owned entity: gets a security policy.</summary>
public class Order : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Tenant-owned, but opted out of row level security via attribute.</summary>
[NoTenantIsolation]
public class AuditEntry : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
}

/// <summary>Test context; tenancy is activated through the options, not in the context.</summary>
public sealed class ShopContext : DbContext
{
    public ShopContext(DbContextOptions<ShopContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
}

/// <summary>Tenant provider whose current tenant a test can set directly.</summary>
public sealed class MutableTenantProvider : ITenantProvider
{
    public TenantId? CurrentTenantId { get; set; }
}

internal static class Contexts
{
    // Service provider caching is disabled so that every test gets a fresh internal service
    // provider and model; otherwise contexts with different options would share one.
    public static ShopContext Create(string connectionString, ITenantProvider? tenants = null)
    {
        var builder = new DbContextOptionsBuilder<ShopContext>()
            .UseSqlServer(connectionString, sql => sql.UseAcmeTenancy())
            .EnableServiceProviderCaching(false);

        if (tenants is not null)
            builder.AddInterceptors(
                new SqlServerTenantSessionInterceptor(tenants),
                new TenantSaveChangesInterceptor(tenants));

        return new ShopContext(builder.Options);
    }

    // Runs operations through the registered IMigrationsSqlGenerator, i.e. through the plugin's
    // generator, and returns the plain SQL of each command.
    public static IReadOnlyList<string> Sql(DbContext ctx, IReadOnlyList<MigrationOperation> operations)
        => ctx.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, ctx.GetService<IDesignTimeModel>().Model)
            .Select(c => c.CommandText.Trim())
            .ToList();

    // Diff against an empty model: the operations a first migration or EnsureCreated would run
    public static IReadOnlyList<string> CreateSql(DbContext ctx)
        => Sql(ctx, ctx.GetService<IMigrationsModelDiffer>().GetDifferences(
            null, ctx.GetService<IDesignTimeModel>().Model.GetRelationalModel()));
}

/// <summary>Without a database: model, query SQL, migration SQL.</summary>
public class SqlServerUnitTests
{
    // Never opened: model building and SQL generation do not need a connection
    private const string NoDatabase = "Server=localhost;Database=unused;Trusted_Connection=True";

    [Fact]
    public void TenantId_maps_to_uniqueidentifier()
    {
        using var ctx = Contexts.Create(NoDatabase);

        Assert.IsType<SqlServerTenantIdTypeMapping>(
            ctx.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(TenantId)));
    }

    [Fact]
    public void Materialization_check_is_opt_in_and_registered_as_singleton_interceptor()
    {
        using var off = Contexts.Create(NoDatabase);
        using var on = new ShopContext(new DbContextOptionsBuilder<ShopContext>()
            .UseSqlServer(NoDatabase, sql => sql.UseAcmeTenancy().VerifyMaterializedTenants())
            .EnableServiceProviderCaching(false)
            .Options);

        Assert.Empty(off.GetService<IEnumerable<ISingletonInterceptor>>()
            .OfType<TenantMaterializationInterceptor>());
        Assert.Single(on.GetService<IEnumerable<ISingletonInterceptor>>()
            .OfType<TenantMaterializationInterceptor>());
    }

    [Fact]
    public void CurrentTenant_uses_session_context()
    {
        using var ctx = Contexts.Create(NoDatabase);

        var sql = ctx.Orders.Where(o => o.TenantId == EF.Functions.CurrentTenant()).ToQueryString();

        Assert.Contains("SESSION_CONTEXT(N'TenantId')", sql);
        Assert.Contains("uniqueidentifier", sql);
    }

    [Fact]
    public void Create_table_creates_security_policy()
    {
        using var ctx = Contexts.Create(NoDatabase);

        var sql = Contexts.CreateSql(ctx);

        Assert.Contains(sql, s => s.StartsWith("CREATE SECURITY POLICY [acme].[tenant_Orders]")
                                  && s.Contains("AFTER INSERT") && s.Contains("AFTER UPDATE"));
        Assert.DoesNotContain(sql, s => s.Contains("[tenant_AuditEntries]"));
    }

    [Fact]
    public void Opt_out_attribute_is_respected()
    {
        using var ctx = Contexts.Create(NoDatabase);
        var audit = ctx.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AuditEntry))!;

        Assert.Null(audit[SqlServerRlsAnnotationNames.TenantColumn]);
    }

    [Fact]
    public void Drop_table_drops_policy_first()
    {
        using var ctx = Contexts.Create(NoDatabase);

        var sql = Contexts.Sql(ctx, [new DropTableOperation { Name = "Orders" }]);

        Assert.StartsWith("DROP SECURITY POLICY IF EXISTS [acme].[tenant_Orders]", sql[0]);
        Assert.Contains("DROP TABLE", sql[1]);
    }
}

/// <summary>Against a real SQL Server (Docker): creates the schema and a non-privileged app login.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private const string AppPassword = "Acme!App#2026";
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();

    public string AppConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var owner = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "shop",
        }.ConnectionString;

        await using (var db = Contexts.Create(owner))
        {
            // EnsureCreated uses the migrations SQL generator -> the security policies are created
            await db.Database.EnsureCreatedAsync();

            // Dedicated app login: more realistic than testing as sa/dbo
            await db.Database.ExecuteSqlRawAsync(
                $"CREATE LOGIN shop_app WITH PASSWORD = '{AppPassword}'; "
                + "CREATE USER shop_app FOR LOGIN shop_app; "
                + "GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO shop_app;");
        }

        AppConnectionString = new SqlConnectionStringBuilder(owner)
        {
            UserID = "shop_app",
            Password = AppPassword,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>Row level security end to end, connected as the app login.</summary>
public class SqlServerIntegrationTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public SqlServerIntegrationTests(SqlServerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private ShopContext ContextFor(TenantId? tenant)
        => Contexts.Create(_fixture.AppConnectionString, new MutableTenantProvider { CurrentTenantId = tenant });

    [Fact]
    public async Task Tenant_sees_only_own_rows()
    {
        var a = TenantId.New();
        var b = TenantId.New();

        await using (var db = ContextFor(a)) { db.Orders.Add(new Order { Total = 1 }); await db.SaveChangesAsync(); }
        await using (var db = ContextFor(b)) { db.Orders.Add(new Order { Total = 2 }); await db.SaveChangesAsync(); }

        await using var reader = ContextFor(a);
        var orders = await reader.Orders.ToListAsync();

        Assert.Single(orders);
        Assert.Equal(a, orders[0].TenantId);
    }

    [Fact]
    public async Task Block_predicate_rejects_rows_of_other_tenant()
    {
        await using var db = ContextFor(TenantId.New());

        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO [Orders] ([TenantId], [Total]) VALUES ({Guid.NewGuid()}, 1)"));

        Assert.Equal(33504, ex.Number);   // violation of a BLOCK PREDICATE
    }

    [Fact]
    public async Task ApplyTenantSession_works_inside_a_transaction()
    {
        var a = TenantId.New();
        var b = TenantId.New();
        await using (var db = ContextFor(a)) { db.Orders.Add(new Order { Total = 1 }); await db.SaveChangesAsync(); }
        await using (var db = ContextFor(b)) { db.Orders.Add(new Order { Total = 2 }); await db.SaveChangesAsync(); }

        var tenants = new MutableTenantProvider { CurrentTenantId = a };
        await using var context = Contexts.Create(_fixture.AppConnectionString, tenants);

        // The transaction keeps the connection open; SQL Server rejects commands that are not
        // enlisted in the pending transaction, so the helper must enlist its command
        await using var transaction = await context.Database.BeginTransactionAsync();
        tenants.CurrentTenantId = b;
        Assert.True(await context.Database.ApplyTenantSessionAsync());

        var orders = await context.Orders.ToListAsync();

        Assert.Single(orders);
        Assert.Equal(b, orders[0].TenantId);
    }
}
