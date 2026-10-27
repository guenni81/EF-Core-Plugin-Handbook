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

    private static void TenancyCheck(DbContext context, Type clrType)
        => TenantMaterializationInterceptor.Verify(
            context, context.Model.FindEntityType(clrType)!, _ => TenantId.New());
}
