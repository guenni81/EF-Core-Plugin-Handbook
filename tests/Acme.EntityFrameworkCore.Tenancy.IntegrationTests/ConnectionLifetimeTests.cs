using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// Where the session interceptor does not run: it fires when EF opens a connection. A
/// connection that is already open, or stays open while the tenant changes, keeps whatever
/// session value it has (chapter "Interceptors").
/// </summary>
public class ConnectionLifetimeTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public ConnectionLifetimeTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private async Task<TenantId> CreateTenantWithOrdersAsync(int count)
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var db = _fixture.CreateAppContext(tenants);

        for (var i = 0; i < count; i++)
            db.Orders.Add(new Order { Total = i + 1 });

        await db.SaveChangesAsync();
        return tenants.CurrentTenantId!.Value;
    }

    [Fact]
    public async Task Already_open_connection_keeps_the_previous_tenant()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        var b = await CreateTenantWithOrdersAsync(1);

        // A connection that is already open, with a session value left over from earlier work
        await using var connection = new NpgsqlConnection(_fixture.AppConnectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand(
                         "SELECT set_config('app.tenant_id', @tenant, false)", connection))
        {
            command.Parameters.AddWithValue("tenant", b.ToString());
            await command.ExecuteNonQueryAsync();
        }

        // The context is meant for tenant A, but EF does not open the connection, so the
        // interceptor never runs: the query sees the rows of tenant B.
        await using var db = _fixture.CreateAppContext(
            connection, new MutableTenantProvider { CurrentTenantId = a });
        var orders = await db.Orders.ToListAsync();

        Assert.Single(orders);
        Assert.Equal(b, orders[0].TenantId);
    }

    [Fact]
    public async Task Closed_external_connection_is_opened_by_ef_and_gets_the_tenant()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        await CreateTenantWithOrdersAsync(1);

        // Safe pattern: hand over the connection CLOSED and let EF open it
        await using var connection = new NpgsqlConnection(_fixture.AppConnectionString);
        await using var db = _fixture.CreateAppContext(
            connection, new MutableTenantProvider { CurrentTenantId = a });
        var orders = await db.Orders.ToListAsync();

        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.Equal(a, o.TenantId));
    }

    [Fact]
    public async Task Tenant_change_on_an_open_connection_applies_only_after_reopening()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        var b = await CreateTenantWithOrdersAsync(1);
        var tenants = new MutableTenantProvider { CurrentTenantId = a };
        await using var db = _fixture.CreateAppContext(tenants);

        await db.Database.OpenConnectionAsync();            // stays open until closed
        tenants.CurrentTenantId = b;
        var whileOpen = await db.Orders.AsNoTracking().ToListAsync();

        await db.Database.CloseConnectionAsync();
        var afterReopen = await db.Orders.AsNoTracking().ToListAsync();

        Assert.All(whileOpen, o => Assert.Equal(a, o.TenantId));      // still tenant A
        Assert.All(afterReopen, o => Assert.Equal(b, o.TenantId));    // now tenant B
        Assert.Equal(2, whileOpen.Count);
        Assert.Single(afterReopen);
    }

    [Fact]
    public async Task ApplyTenantSession_fixes_an_already_open_connection()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        var b = await CreateTenantWithOrdersAsync(1);

        await using var connection = new NpgsqlConnection(_fixture.AppConnectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand(
                         "SELECT set_config('app.tenant_id', @tenant, false)", connection))
        {
            command.Parameters.AddWithValue("tenant", b.ToString());
            await command.ExecuteNonQueryAsync();
        }

        await using var db = _fixture.CreateAppContext(
            connection, new MutableTenantProvider { CurrentTenantId = a });

        // The explicit call does what the interceptor could not: the session now has tenant A
        Assert.True(await db.Database.ApplyTenantSessionAsync());
        var orders = await db.Orders.ToListAsync();

        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.Equal(a, o.TenantId));
    }

    [Fact]
    public async Task ApplyTenantSession_applies_a_tenant_change_on_an_open_connection()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        var b = await CreateTenantWithOrdersAsync(1);
        var tenants = new MutableTenantProvider { CurrentTenantId = a };
        await using var db = _fixture.CreateAppContext(tenants);

        await db.Database.OpenConnectionAsync();
        tenants.CurrentTenantId = b;
        await db.Database.ApplyTenantSessionAsync();
        var orders = await db.Orders.AsNoTracking().ToListAsync();

        Assert.Single(orders);
        Assert.Equal(b, orders[0].TenantId);
    }
}
