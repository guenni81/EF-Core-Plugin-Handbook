using System.Data.Common;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Operations;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>Tenant-owned entity for the RLS tests.</summary>
public class Order : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Tenant directory, not tenant-owned; key generated as <see cref="TenantId"/>.</summary>
public class Tenant
{
    public TenantId Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Tenant-owned entity with a JSON column.</summary>
public class Shipment : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public ShipmentDetails Details { get; set; } = new();
}

/// <summary>Owned type stored as JSON; contains a <see cref="TenantId"/> value.</summary>
public class ShipmentDetails
{
    public string Carrier { get; set; } = "";
    public TenantId ApprovedBy { get; set; }
}

/// <summary>Context used by the integration tests.</summary>
public sealed class ShopContext : DbContext
{
    public ShopContext(DbContextOptions<ShopContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Shipment>().OwnsOne(s => s.Details, d => d.ToJson());
}

/// <summary>Empty context that only exists to own <see cref="RolesMigration"/>.</summary>
public sealed class ScriptContext : DbContext
{
    public ScriptContext(DbContextOptions<ScriptContext> options)
        : base(options)
    {
    }
}

/// <summary>Hand-written migration that uses the plugin's custom operations.</summary>
[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(ScriptContext))]
[Migration("20261021000000_Roles")]
public class RolesMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateApplicationRole("script_role");
        migrationBuilder.GrantTablePrivileges("script_role");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropApplicationRole("script_role");
}

/// <summary>Tenant provider whose tenant a test can set and change.</summary>
public sealed class MutableTenantProvider : ITenantProvider
{
    public TenantId? CurrentTenantId { get; set; }
}

/// <summary>
/// Starts PostgreSQL, creates the schema as owner (including RLS via the plugin's SQL
/// generator) and sets up a restricted app role.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Name of the restricted role the tests connect with.</summary>
    public const string AppRole = "shop_app";
    private const string AppPassword = "shop_app_pw";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    /// <summary>Connection string for <see cref="AppRole"/>; set in <see cref="InitializeAsync"/>.</summary>
    public string AppConnectionString { get; private set; } = "";

    /// <summary>Connection string of the container's superuser, who owns the tables.</summary>
    public string OwnerConnectionString => _container.GetConnectionString();

    /// <summary>Starts the container and prepares schema and roles.</summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Owner = the container's superuser. The app later connects with a restricted role:
        // not the owner, no superuser, no BYPASSRLS. Otherwise RLS would be bypassed.
        await using var owner = CreateContext(_container.GetConnectionString(), tenants: null);

        // EnsureCreated uses the design-time model and the migrations SQL generator:
        // RLS, policies and index are created exactly as with "dotnet ef database update".
        await owner.Database.EnsureCreatedAsync();

        // Execute the plugin's custom migration operations for real. The order matters:
        // GRANT ... ON ALL TABLES only affects tables that already exist.
        var commands = owner.GetService<IMigrationsSqlGenerator>().Generate(
        [
            new CreateApplicationRoleOperation { Name = AppRole, CanLogin = true },
            new GrantTablePrivilegesOperation { Role = AppRole },
        ]);
        await owner.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(
            commands, owner.GetService<IRelationalConnection>(),
            new MigrationExecutionState(), commitTransaction: true);

        // Passwords do not belong in migrations, so the test sets it separately. Raw SQL with
        // constants only; never interpolate untrusted input like this.
        await owner.Database.ExecuteSqlRawAsync($"ALTER ROLE {AppRole} PASSWORD '{AppPassword}'");

        AppConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = AppRole,
            Password = AppPassword,
        }.ConnectionString;
    }

    /// <summary>Creates a context that connects as <see cref="AppRole"/> with the given tenant.</summary>
    /// <param name="tenants">The tenant of the context.</param>
    /// <param name="tenancy">Optional plugin options.</param>
    /// <param name="configure">Optional additional configuration, e.g. more interceptors.</param>
    public ShopContext CreateAppContext(
        MutableTenantProvider tenants,
        Action<TenancyOptionsBuilder>? tenancy = null,
        Action<DbContextOptionsBuilder<ShopContext>>? configure = null)
        => CreateContext(new DbContextOptionsBuilder<ShopContext>().UseNpgsql(AppConnectionString),
            tenants, tenancy, configure);

    /// <summary>
    /// Creates an app context over a connection the caller owns. EF only opens (and the
    /// session interceptor only runs) if the connection is still closed.
    /// </summary>
    /// <param name="connection">The connection; EF opens it only if it is closed.</param>
    /// <param name="tenants">The tenant of the context.</param>
    /// <param name="tenancy">Optional plugin options.</param>
    /// <param name="configure">Optional additional configuration, e.g. more interceptors.</param>
    public ShopContext CreateAppContext(
        DbConnection connection,
        MutableTenantProvider tenants,
        Action<TenancyOptionsBuilder>? tenancy = null,
        Action<DbContextOptionsBuilder<ShopContext>>? configure = null)
        => CreateContext(new DbContextOptionsBuilder<ShopContext>().UseNpgsql(connection),
            tenants, tenancy, configure);

    private static ShopContext CreateContext(string connectionString, ITenantProvider? tenants)
        => CreateContext(new DbContextOptionsBuilder<ShopContext>().UseNpgsql(connectionString),
            tenants, tenancy: null, configure: null);

    private static ShopContext CreateContext(
        DbContextOptionsBuilder<ShopContext> builder,
        ITenantProvider? tenants,
        Action<TenancyOptionsBuilder>? tenancy,
        Action<DbContextOptionsBuilder<ShopContext>>? configure)
    {
        builder.UseAcmeTenancy(tenancy);

        // Without a provider (owner context) no interceptors: the session setting stays unset.
        if (tenants is not null)
            builder.AddInterceptors(
                new TenantConnectionInterceptor(tenants),
                new TenantSaveChangesInterceptor(tenants));

        configure?.Invoke(builder);
        return new ShopContext(builder.Options);
    }

    /// <summary>Stops and removes the container.</summary>
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
