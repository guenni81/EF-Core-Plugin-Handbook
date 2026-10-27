using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// The optional materialization check without a database: registration as a singleton
/// interceptor, the check itself, and why a new instance per context is wrong
/// (chapter "Materialization: from rows to objects").
/// </summary>
public class MaterializationTests
{
    private sealed class FixedTenant : ITenantProvider
    {
        public TenantId? CurrentTenantId { get; init; }
    }

    private static TestContext CreateContext(
        ITenantProvider tenants, bool verify = true, params IInterceptor[] interceptors)
        => Contexts.Create(
            tenancy: t => t.VerifyMaterializedTenants(verify),
            cacheServiceProvider: true,
            configure: o => o
                .AddInterceptors([new TenantSaveChangesInterceptor(tenants), .. interceptors])
                // These tests create internal providers on purpose; the global count of
                // providers must not turn into an exception for other tests
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

    private static IEnumerable<ISingletonInterceptor> SingletonInterceptors(DbContext context)
        => context.GetService<IEnumerable<ISingletonInterceptor>>();

    [Fact]
    public void Option_registers_the_interceptor_in_the_internal_provider()
    {
        using var on = CreateContext(new FixedTenant());
        using var off = CreateContext(new FixedTenant(), verify: false);

        Assert.Single(SingletonInterceptors(on).OfType<TenantMaterializationInterceptor>());
        Assert.Empty(SingletonInterceptors(off).OfType<TenantMaterializationInterceptor>());
    }

    [Fact]
    public void Contexts_with_different_tenants_share_one_instance_and_one_provider()
    {
        using var a = CreateContext(new FixedTenant { CurrentTenantId = TenantId.New() });
        using var b = CreateContext(new FixedTenant { CurrentTenantId = TenantId.New() });

        // Scoped interceptors from AddInterceptors are not part of the provider key
        Assert.Same(
            SingletonInterceptors(a).OfType<TenantMaterializationInterceptor>().Single(),
            SingletonInterceptors(b).OfType<TenantMaterializationInterceptor>().Single());
        Assert.Same(a.GetService<ITenancySingletonOptions>(), b.GetService<ITenancySingletonOptions>());
    }

    [Fact]
    public void New_singleton_instance_per_context_builds_a_provider_per_context()
    {
        var tenants = new FixedTenant { CurrentTenantId = TenantId.New() };

        // WRONG: a singleton interceptor created for every context (the "natural" way to pass
        // per-request data). Singleton interceptor instances are part of the provider key.
        using var a = CreateContext(tenants, verify: false, new TenantMaterializationInterceptor());
        using var b = CreateContext(tenants, verify: false, new TenantMaterializationInterceptor());

        Assert.NotSame(a.GetService<ITenancySingletonOptions>(), b.GetService<ITenancySingletonOptions>());
    }

    [Fact]
    public void Row_of_the_current_tenant_passes()
    {
        var tenant = TenantId.New();
        using var ctx = CreateContext(new FixedTenant { CurrentTenantId = tenant });

        TenantMaterializationInterceptor.Verify(
            ctx, ctx.Model.FindEntityType(typeof(Order))!, _ => tenant);
    }

    [Fact]
    public void Row_of_another_tenant_throws()
    {
        using var ctx = CreateContext(new FixedTenant { CurrentTenantId = TenantId.New() });

        Assert.Throws<TenancyException>(() => TenantMaterializationInterceptor.Verify(
            ctx, ctx.Model.FindEntityType(typeof(Order))!, _ => TenantId.New()));
    }

    [Fact]
    public void Row_without_a_current_tenant_throws()
    {
        // Row level security returns no rows without a tenant; a row means it did not apply
        using var ctx = CreateContext(new FixedTenant());

        Assert.Throws<TenancyException>(() => TenancyCheck(ctx, typeof(Order)));
    }

    [Fact]
    public void Entities_without_isolation_are_not_checked()
    {
        using var ctx = CreateContext(new FixedTenant());

        // Product is shared, AuditEntry opted out: no runtime annotation, no check
        TenancyCheck(ctx, typeof(Product));
        TenancyCheck(ctx, typeof(AuditEntry));
    }

    [Fact]
    public void Context_without_tenant_provider_is_a_configuration_error()
    {
        using var ctx = Contexts.Create(tenancy: t => t.VerifyMaterializedTenants());

        var ex = Assert.Throws<TenancyException>(() => TenancyCheck(ctx, typeof(Order)));
        Assert.Contains("UseAcmeTenancy(applicationServices)", ex.Message);
    }

    [Fact]
    public void Provider_is_looked_up_once_per_context_but_the_tenant_on_every_row()
    {
        var tenants = new MutableTenant { CurrentTenantId = TenantId.New() };
        using var ctx = CreateContext(tenants);
        var order = ctx.Model.FindEntityType(typeof(Order))!;

        Assert.Same(tenants, TenantMaterializationInterceptor.FindTenantProvider(ctx));
        Assert.Same(TenantMaterializationInterceptor.FindTenantProvider(ctx),
            TenantMaterializationInterceptor.FindTenantProvider(ctx));

        // Many rows in a row, and a tenant that changes in between: the cached provider is
        // asked again every time
        for (var i = 0; i < 1000; i++)
            TenantMaterializationInterceptor.Verify(ctx, order, _ => tenants.CurrentTenantId);
        var previous = tenants.CurrentTenantId;
        tenants.CurrentTenantId = TenantId.New();
        Assert.Throws<TenancyException>(() =>
            TenantMaterializationInterceptor.Verify(ctx, order, _ => previous));
    }

    [Fact]
    public void Contexts_with_different_tenants_get_their_own_provider()
    {
        TenantId a = TenantId.New(), b = TenantId.New();
        using var first = CreateContext(new FixedTenant { CurrentTenantId = a });
        using var second = CreateContext(new FixedTenant { CurrentTenantId = b });
        var order = first.Model.FindEntityType(typeof(Order))!;

        TenantMaterializationInterceptor.Verify(first, order, _ => a);
        TenantMaterializationInterceptor.Verify(second, order, _ => b);
        Assert.Throws<TenancyException>(() => TenantMaterializationInterceptor.Verify(first, order, _ => b));
        Assert.Throws<TenancyException>(() => TenantMaterializationInterceptor.Verify(second, order, _ => a));
    }

    [Fact]
    public void Pooled_context_checks_the_tenant_of_each_lease()
    {
        var options = new DbContextOptionsBuilder<PooledContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseAcmeTenancy(t => t.VerifyMaterializedTenants())
            .AddInterceptors(new TenantSaveChangesInterceptor(new AmbientTenantProvider()))
            .Options;
        var factory = new PooledDbContextFactory<PooledContext>(options);
        TenantId a = TenantId.New(), b = TenantId.New();

        PooledContext firstLease;
        using (AmbientTenantProvider.Use(a))
        using (var ctx = factory.CreateDbContext())
        {
            firstLease = ctx;
            TenantMaterializationInterceptor.Verify(ctx, ctx.Model.FindEntityType(typeof(Order))!, _ => a);
        }

        using (AmbientTenantProvider.Use(b))
        using (var ctx = factory.CreateDbContext())
        {
            Assert.Same(firstLease, ctx);                     // the same instance, reset
            var order = ctx.Model.FindEntityType(typeof(Order))!;
            TenantMaterializationInterceptor.Verify(ctx, order, _ => b);
            Assert.Throws<TenancyException>(() => TenantMaterializationInterceptor.Verify(ctx, order, _ => a));
        }
    }

    [Fact]
    public void Failed_lookup_is_not_cached()
    {
        using var ctx = Contexts.Create(tenancy: t => t.VerifyMaterializedTenants());

        Assert.Throws<TenancyException>(() => TenantMaterializationInterceptor.FindTenantProvider(ctx));
        Assert.Throws<TenancyException>(() => TenantMaterializationInterceptor.FindTenantProvider(ctx));
    }

    private sealed class MutableTenant : ITenantProvider
    {
        public TenantId? CurrentTenantId { get; set; }
    }

    /// <summary>Context type for pooling: only the options constructor.</summary>
    private sealed class PooledContext : DbContext
    {
        public PooledContext(DbContextOptions<PooledContext> options)
            : base(options)
        {
        }

        public DbSet<Order> Orders => Set<Order>();
    }

    private static void TenancyCheck(DbContext context, Type clrType)
        => TenantMaterializationInterceptor.Verify(
            context, context.Model.FindEntityType(clrType)!, _ => TenantId.New());
}
