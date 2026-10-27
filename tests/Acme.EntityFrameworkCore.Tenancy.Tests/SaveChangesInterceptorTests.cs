using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.EntityFrameworkCore;

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
    public void Update_of_an_untracked_entity_with_its_own_tenant_is_allowed()
    {
        var tenant = TenantId.New();
        using var ctx = Contexts.Create();
        var entry = ctx.Orders.Update(new Order { Id = 1, TenantId = tenant, Total = 5 });

        // EF marks every property as modified, the tenant property included
        Assert.True(entry.Property(o => o.TenantId).IsModified);

        new TenantSaveChangesInterceptor(new FixedTenant(tenant)).ApplyTenant(ctx);

        // Not a change of the tenant: the UPDATE does not write the column
        Assert.False(entry.Property(o => o.TenantId).IsModified);
        Assert.Equal(EntityState.Modified, entry.State);
    }

    [Fact]
    public void Update_of_an_untracked_entity_of_another_tenant_is_rejected()
    {
        using var ctx = Contexts.Create();
        ctx.Orders.Update(new Order { Id = 1, TenantId = TenantId.New() });

        var interceptor = new TenantSaveChangesInterceptor(new FixedTenant(TenantId.New()));

        var ex = Assert.Throws<TenancyException>(() => interceptor.ApplyTenant(ctx));
        Assert.Contains("belongs to tenant", ex.Message);
    }

    [Fact]
    public void Changing_the_tenant_to_the_current_one_is_rejected_too()
    {
        TenantId current = TenantId.New(), other = TenantId.New();
        using var ctx = Contexts.Create();
        var order = new Order { Id = 1, TenantId = other };
        ctx.Orders.Attach(order);
        order.TenantId = current;
        ctx.ChangeTracker.DetectChanges();

        var ex = Assert.Throws<TenancyException>(
            () => new TenantSaveChangesInterceptor(new FixedTenant(current)).ApplyTenant(ctx));
        Assert.Contains("must not be changed", ex.Message);
    }

    [Fact]
    public void Update_with_a_shadow_tenant_property_leaves_the_check_to_the_database()
    {
        var tenant = TenantId.New();
        using var ctx = Contexts.Create(model: m => m.Entity<Product>()
            .Property<TenantId>("TenantId").IsTenantProperty());
        var entry = ctx.Products.Update(new Product { Id = 1 });

        // The entity cannot carry a shadow value: it is unknown (empty), not foreign
        Assert.Equal(TenantId.Empty, entry.Property<TenantId>("TenantId").CurrentValue);

        new TenantSaveChangesInterceptor(new FixedTenant(tenant)).ApplyTenant(ctx);

        Assert.False(entry.Property<TenantId>("TenantId").IsModified);
    }

    [Fact]
    public void Deleting_an_entity_of_another_tenant_is_rejected()
    {
        using var ctx = Contexts.Create();
        ctx.Orders.Remove(new Order { Id = 1, TenantId = TenantId.New() });

        Assert.Throws<TenancyException>(
            () => new TenantSaveChangesInterceptor(new FixedTenant(TenantId.New())).ApplyTenant(ctx));
    }

    [Fact]
    public void Update_without_an_active_tenant_is_rejected()
    {
        using var ctx = Contexts.Create();
        ctx.Orders.Update(new Order { Id = 1, TenantId = TenantId.New() });

        Assert.Throws<TenancyException>(
            () => new TenantSaveChangesInterceptor(new FixedTenant(null)).ApplyTenant(ctx));
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
