using Acme.EntityFrameworkCore.Tenancy.Interceptors;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// The SaveChanges interceptor: stamping the tenant on new entities and rejecting foreign or
/// changed tenants. <c>ApplyTenant</c> is called directly, so no database is needed.
/// </summary>
public class SaveChangesInterceptorTests
{
    private sealed class FixedTenant : ITenantProvider
    {
        public FixedTenant(TenantId? id) => CurrentTenantId = id;

        public TenantId? CurrentTenantId { get; }
    }

    [Fact]
    public void Sets_tenant_on_added_entities()
    {
        var tenant = TenantId.New();
        using var ctx = Contexts.Create();
        var order = new Order();
        ctx.Orders.Add(order);

        new TenantSaveChangesInterceptor(new FixedTenant(tenant)).ApplyTenant(ctx);

        Assert.Equal(tenant, order.TenantId);
    }

    [Fact]
    public void Rejects_entity_of_other_tenant()
    {
        using var ctx = Contexts.Create();
        ctx.Orders.Add(new Order { TenantId = TenantId.New() });

        var interceptor = new TenantSaveChangesInterceptor(new FixedTenant(TenantId.New()));

        Assert.Throws<TenancyException>(() => interceptor.ApplyTenant(ctx));
    }

    [Fact]
    public void Rejects_changing_the_tenant()
    {
        var tenant = TenantId.New();
        using var ctx = Contexts.Create();
        var order = new Order { Id = 1, TenantId = tenant };
        ctx.Orders.Attach(order);
        order.TenantId = TenantId.New();
        // Snapshot change tracking: the modification only becomes visible after DetectChanges
        // (SaveChanges would do this before the interceptor runs).
        ctx.ChangeTracker.DetectChanges();

        var interceptor = new TenantSaveChangesInterceptor(new FixedTenant(tenant));

        Assert.Throws<TenancyException>(() => interceptor.ApplyTenant(ctx));
    }

    [Fact]
    public void Requires_active_tenant_for_tenant_owned_changes()
    {
        using var ctx = Contexts.Create();
        ctx.Orders.Add(new Order());

        var interceptor = new TenantSaveChangesInterceptor(new FixedTenant(null));

        Assert.Throws<TenancyException>(() => interceptor.ApplyTenant(ctx));
    }

    [Fact]
    public void Ignores_entities_without_marker()
    {
        using var ctx = Contexts.Create();
        ctx.Products.Add(new Product());

        // No tenant required, no exception
        new TenantSaveChangesInterceptor(new FixedTenant(null)).ApplyTenant(ctx);
    }
}
