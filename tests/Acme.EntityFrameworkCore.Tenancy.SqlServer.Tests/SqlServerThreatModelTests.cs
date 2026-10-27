using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests.Relationships;

/// <summary>Tenant-owned parent.</summary>
public class Order : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

/// <summary>Tenant-owned child with a foreign key on the parent's ID alone.</summary>
public class OrderItem : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public int OrderId { get; set; }
}

/// <summary>
/// SQL Server: foreign key checks and the session context against the security policy. The
/// documentation does not say whether foreign key validation applies filter predicates; these
/// tests record the behavior of the version in the container.
/// </summary>
public class SqlServerThreatModelTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public SqlServerThreatModelTests(SqlServerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private static void Model(ModelBuilder m) => m.Entity<Order>();

    private async Task<string> CreateAsync()
    {
        var (owner, app) = await SqlServerMigrationTests.CreateDatabaseAsync(_fixture);
        await SqlServerMigrationTests.RunAsync(owner, Versions.Commands((MappingContext?)null, MappingContext.Create(Model)));
        return app;
    }

    private static async Task<int> AddOrderAsync(string app, TenantId tenant)
    {
        await using var db = MappingContext.Create(Model, app, tenants: new MutableTenantProvider { CurrentTenantId = tenant });
        var order = new Order();
        db.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    [Fact]
    public async Task Foreign_key_check_sees_rows_the_filter_predicate_hides()
    {
        var app = await CreateAsync();
        var foreignOrder = await AddOrderAsync(app, TenantId.New());

        // Tenant A references B's order: the insert succeeds although A cannot see the order
        var a = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var db = MappingContext.Create(Model, app, tenants: a);
        db.Add(new OrderItem { OrderId = foreignOrder });
        await db.SaveChangesAsync();

        Assert.Empty(await db.Set<Order>().ToListAsync());
        Assert.Single(await db.Set<OrderItem>().ToListAsync());
    }

    [Fact]
    public async Task App_login_can_set_another_tenant_unless_the_key_is_read_only()
    {
        var app = await CreateAsync();
        var victim = TenantId.New();
        await AddOrderAsync(app, victim);

        await using var connection = new SqlConnection(app);
        await connection.OpenAsync();
        await SqlServerMigrationTests.Execute(connection,
            "EXEC sp_set_session_context @key = N'TenantId', @value = @t", ("@t", victim.Value));
        Assert.Equal(1, await SqlServerMigrationTests.Execute(connection, "SELECT count(*) FROM [Order]"));

        // @read_only = 1 (not used by the plugin) freezes the key for the session
        await SqlServerMigrationTests.Execute(connection,
            "EXEC sp_set_session_context @key = N'TenantId', @value = @t, @read_only = 1", ("@t", Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(() => SqlServerMigrationTests.Execute(connection,
            "EXEC sp_set_session_context @key = N'TenantId', @value = @t", ("@t", victim.Value)));
    }
}
