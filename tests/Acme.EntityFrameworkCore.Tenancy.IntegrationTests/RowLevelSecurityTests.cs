using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// Row level security against a real PostgreSQL: tenant isolation is enforced by the
/// database, not by LINQ filters.
/// </summary>
public class RowLevelSecurityTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public RowLevelSecurityTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private async Task<TenantId> CreateTenantWithOrdersAsync(int count)
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var db = _fixture.CreateAppContext(tenants);

        for (var i = 0; i < count; i++)
            db.Orders.Add(new Order { Total = 10 * (i + 1) });   // the interceptor sets TenantId

        await db.SaveChangesAsync();
        return tenants.CurrentTenantId!.Value;
    }

    [Fact]
    public async Task Tenant_sees_only_own_rows()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        await CreateTenantWithOrdersAsync(3);

        await using var db = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = a });
        var orders = await db.Orders.ToListAsync();     // NO filter in LINQ: the DB does it

        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.Equal(a, o.TenantId));
    }

    [Fact]
    public async Task Without_tenant_no_rows_are_visible()
    {
        await CreateTenantWithOrdersAsync(1);

        // No tenant -> the session setting is unset, current_setting(..., true) yields NULL and
        // the policy matches no row. Fail closed instead of showing everything.
        await using var db = _fixture.CreateAppContext(new MutableTenantProvider());

        Assert.Empty(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Database_rejects_rows_of_other_tenant()
    {
        // Bypass the interceptor: direct SQL. The policy's WITH CHECK must apply.
        var tenant = TenantId.New();
        await using var db = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant });

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync(
                $"INSERT INTO \"Orders\" (\"TenantId\", \"Total\") VALUES ({Guid.NewGuid()}, 1)"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task CurrentTenant_function_returns_session_tenant()
    {
        var tenant = await CreateTenantWithOrdersAsync(1);
        await using var db = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant });

        // Translated to current_setting(...) and read back into TenantId via the type mapping
        var fromDatabase = await db.Orders.Select(o => EF.Functions.CurrentTenant()).FirstAsync();
        var matching = await db.Orders.CountAsync(o => o.TenantId == EF.Functions.CurrentTenant());

        Assert.Equal(tenant, fromDatabase);
        Assert.Equal(1, matching);
    }
}
