using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// Updates of untracked entities (DbContext.Update) against PostgreSQL with RLS: the interceptor
/// tells a marked property from a changed value, and the database decides which row is hit.
/// </summary>
public class DetachedUpdateTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public DetachedUpdateTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private async Task<(TenantId Tenant, int Id)> CreateOrderAsync(decimal total)
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var db = _fixture.CreateAppContext(tenants);
        var order = new Order { Total = total };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (tenants.CurrentTenantId.Value, order.Id);
    }

    private async Task<decimal> TotalAsync(TenantId tenant, int id)
    {
        await using var db = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant });
        return await db.Orders.Where(o => o.Id == id).Select(o => o.Total).SingleAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_of_an_untracked_entity_of_the_own_tenant_is_saved(bool async)
    {
        var (tenant, id) = await CreateOrderAsync(10);

        await using (var db = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant }))
        {
            // As in an API: the entity comes from a request, not from this context
            db.Orders.Update(new Order { Id = id, TenantId = tenant, Total = 99 });
            if (async)
                await db.SaveChangesAsync();
            else
                db.SaveChanges();
        }

        Assert.Equal(99, await TotalAsync(tenant, id));
    }

    [Fact]
    public async Task Forged_entity_with_the_id_of_a_foreign_row_changes_nothing()
    {
        var (victim, id) = await CreateOrderAsync(10);
        var attacker = TenantId.New();

        await using (var db = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = attacker }))
        {
            // The entity claims the attacker's tenant: the interceptor cannot know better without
            // reading the row. The policy hides the foreign row, the UPDATE affects 0 rows.
            db.Orders.Update(new Order { Id = id, TenantId = attacker, Total = 0 });
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        }

        Assert.Equal(10, await TotalAsync(victim, id));
    }

    [Fact]
    public async Task Moving_a_tracked_row_to_another_tenant_is_rejected_before_the_database()
    {
        var (tenant, id) = await CreateOrderAsync(10);

        await using var db = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant });
        var order = await db.Orders.SingleAsync(o => o.Id == id);
        order.TenantId = TenantId.New();

        var ex = Assert.Throws<TenancyException>(() => db.SaveChanges());
        Assert.Contains("must not be changed", ex.Message);
    }
}
