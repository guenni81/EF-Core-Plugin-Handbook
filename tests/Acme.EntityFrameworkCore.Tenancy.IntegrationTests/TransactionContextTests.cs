using System.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// The session interceptor and transactions against PostgreSQL (chapter "Interceptors"): EF opens
/// the connection before it begins a transaction, and an ambient TransactionScope enlists the
/// connection when it is opened, so the session command never needs a transaction assigned.
/// </summary>
public class TransactionContextTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public TransactionContextTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private async Task<TenantId> CreateTenantWithOrderAsync()
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var db = _fixture.CreateAppContext(tenants);
        db.Orders.Add(new Order { Total = 1 });
        await db.SaveChangesAsync();
        return tenants.CurrentTenantId.Value;
    }

    [Fact]
    public async Task Connection_opened_by_BeginTransaction_gets_the_tenant()
    {
        var tenant = await CreateTenantWithOrderAsync();
        await CreateTenantWithOrderAsync();

        await using var db = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant });
        await using var transaction = await db.Database.BeginTransactionAsync();   // opens here

        var orders = await db.Orders.ToListAsync();
        Assert.Single(orders);
        Assert.Equal(tenant, orders[0].TenantId);
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task ApplyTenantSession_inside_an_explicit_transaction_switches_the_tenant()
    {
        TenantId a = await CreateTenantWithOrderAsync(), b = await CreateTenantWithOrderAsync();
        var tenants = new MutableTenantProvider { CurrentTenantId = a };

        await using var db = _fixture.CreateAppContext(tenants);
        await using var transaction = await db.Database.BeginTransactionAsync();
        tenants.CurrentTenantId = b;
        Assert.True(await db.Database.ApplyTenantSessionAsync());   // enlisted in the transaction

        Assert.All(await db.Orders.ToListAsync(), o => Assert.Equal(b, o.TenantId));
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Ambient_TransactionScope_reads_and_writes_with_the_tenant()
    {
        var tenant = await CreateTenantWithOrderAsync();
        await CreateTenantWithOrderAsync();

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await using var db = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant });
            Assert.All(await db.Orders.ToListAsync(), o => Assert.Equal(tenant, o.TenantId));
            db.Orders.Add(new Order { Total = 2 });
            await db.SaveChangesAsync();
            scope.Complete();
        }

        await using var check = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant });
        Assert.Equal(2, await check.Orders.CountAsync());
    }
}
