using System.Data.Common;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Runtime;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests;

/// <summary>Counts the commands that reach the database.</summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    public int Count { get; private set; }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Count++;
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Count++;
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// ProtectBulkOperations() on SQL Server: the same core interceptor as on PostgreSQL, with
/// ExecuteUpdate after a projection, connected as the app login.
/// </summary>
public class SqlServerBulkUpdateTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public SqlServerBulkUpdateTests(SqlServerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private ShopContext Create(TenantId tenant, CommandCounter counter, bool protect = true)
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = tenant };
        return new ShopContext(new DbContextOptionsBuilder<ShopContext>()
            .UseSqlServer(_fixture.AppConnectionString, sql => sql.UseAcmeTenancy().ProtectBulkOperations(protect))
            .EnableServiceProviderCaching(false)
            .AddInterceptors(
                new SqlServerTenantSessionInterceptor(tenants),
                new TenantSaveChangesInterceptor(tenants),
                counter)
            .Options);
    }

    private async Task<(TenantId Tenant, int Id)> CreateOrderAsync(decimal total)
    {
        var tenant = TenantId.New();
        await using var db = Create(tenant, new CommandCounter());
        var order = new Order { Total = total };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (tenant, order.Id);
    }

    // The FILTER predicate applies to every user, the administrator included: read as the tenant
    private async Task<decimal?> TotalAsync(TenantId tenant, int id)
    {
        await using var db = Create(tenant, new CommandCounter());
        return await db.Orders.Where(o => o.Id == id).Select(o => (decimal?)o.Total).SingleOrDefaultAsync();
    }

    [Fact]
    public async Task Projected_setter_of_another_column_updates_only_the_own_tenant()
    {
        var (tenant, id) = await CreateOrderAsync(10);
        var (other, otherId) = await CreateOrderAsync(10);
        await using var db = Create(tenant, new CommandCounter());

        var updated = await db.Orders.Select(o => new { Entity = o })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Entity.Total, 20m));

        Assert.Equal(1, updated);                                     // FILTER predicate
        Assert.Equal(20m, await TotalAsync(tenant, id));
        Assert.Equal(10m, await TotalAsync(other, otherId));
    }

    [Fact]
    public async Task Projected_setters_of_the_tenant_column_fail_before_any_command()
    {
        var (tenant, id) = await CreateOrderAsync(10);
        var counter = new CommandCounter();
        await using var db = Create(tenant, counter);

        await Assert.ThrowsAsync<TenancyException>(() => db.Orders.Select(o => new { Entity = o })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Entity.TenantId, TenantId.New())));
        Assert.Throws<TenancyException>(() => db.Orders.Select(o => new { Entity = o, o.TenantId })
            .ExecuteUpdate(s => s
                .SetProperty(x => x.Entity.Total, 30m)
                .SetProperty(x => x.TenantId, TenantId.New())));

        Assert.Equal(0, counter.Count);
        Assert.Equal(10m, await TotalAsync(tenant, id));                // still the tenant's row
    }

    [Fact]
    public async Task Without_the_option_the_block_predicate_rejects_the_projected_tenant_setter()
    {
        var (tenant, id) = await CreateOrderAsync(10);
        await using var db = Create(tenant, new CommandCounter(), protect: false);

        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Orders.Select(o => new { Entity = o })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Entity.TenantId, TenantId.New())));

        Assert.Equal(33504, ex.Number);                               // BLOCK PREDICATE AFTER UPDATE
        Assert.Equal(10m, await TotalAsync(tenant, id));                // still the tenant's row
    }
}
