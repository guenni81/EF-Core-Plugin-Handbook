using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// ProtectBulkOperations(): ExecuteUpdate on the tenant property fails while the query is
/// compiled, before any connection is opened (chapter "Bulk operations and the limits of
/// interceptors"). Allowed updates are executed in the integration tests.
/// </summary>
public class BulkOperationTests
{
    private static TestContext Create(bool protect = true, Action<ModelBuilder>? model = null)
        => Contexts.Create(tenancy: t => t.ProtectBulkOperations(protect), model: model);

    [Fact]
    public void Option_registers_the_interceptor_in_the_internal_provider()
    {
        using var on = Create();
        using var off = Create(protect: false);

        Assert.Single(on.GetService<IEnumerable<IInterceptor>>().OfType<TenantQueryExpressionInterceptor>());
        Assert.Empty(off.GetService<IEnumerable<IInterceptor>>().OfType<TenantQueryExpressionInterceptor>());
    }

    [Fact]
    public void Setting_the_tenant_property_is_rejected_at_compilation()
    {
        using var ctx = Create();
        var other = TenantId.New();

        var ex = Assert.Throws<TenancyException>(() =>
            ctx.Orders.ExecuteUpdate(s => s.SetProperty(o => o.TenantId, other)));
        Assert.Equal("The tenant property of an entity must not be modified in ExecuteUpdate.", ex.Message);
    }

    [Fact]
    public async Task Async_variant_and_other_setters_in_the_same_call_are_rejected_too()
    {
        using var ctx = Create();

        await Assert.ThrowsAsync<TenancyException>(() => ctx.Orders.ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Total, o => o.Total + 1)
            .SetProperty(o => o.TenantId, o => TenantId.Empty)));
    }

    [Fact]
    public void Shadow_tenant_property_set_through_EF_Property_is_rejected()
    {
        using var ctx = Create(model: m => m.Entity<Product>().Property<TenantId>("TenantId").IsTenantProperty());

        Assert.Throws<TenancyException>(() => ctx.Products.ExecuteUpdate(s =>
            s.SetProperty(p => EF.Property<TenantId>(p, "TenantId"), TenantId.New())));
    }

    [Fact]
    public void Other_columns_and_ordinary_queries_pass_the_check()
    {
        using var ctx = Create();

        // No database: the update gets past compilation and then fails to connect
        var ex = Record.Exception(() => ctx.Orders.ExecuteUpdate(s => s.SetProperty(o => o.Total, 0m)));
        Assert.IsNotType<TenancyException>(ex);
        Assert.Contains("WHERE", ctx.Orders.Where(o => o.Total > 0).Select(o => o.Id).ToQueryString());
    }

    private const string Rejected = "The tenant property of an entity must not be modified in ExecuteUpdate.";

    // Gets past the check: without a database the update then fails to connect
    private static void AssertPasses(Action update) => Assert.IsNotType<TenancyException>(Record.Exception(update));

    [Fact]
    public void Projected_entity_setter_is_rejected()
    {
        using var ctx = Create();

        var ex = Assert.Throws<TenancyException>(() => ctx.Orders.Select(o => new { Entity = o })
            .ExecuteUpdate(s => s.SetProperty(x => x.Entity.TenantId, TenantId.New())));
        Assert.Equal(Rejected, ex.Message);
    }

    [Fact]
    public async Task Projected_setters_are_rejected_asynchronously_and_among_other_setters()
    {
        using var ctx = Create();

        await Assert.ThrowsAsync<TenancyException>(() => ctx.Orders
            .Where(o => o.Total > 0)
            .Select(o => new { Entity = o })
            .OrderBy(x => x.Entity.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Entity.Total, 1m)
                .SetProperty(x => x.Entity.TenantId, TenantId.Empty)));
    }

    [Fact]
    public void Nested_and_chained_projections_are_resolved()
    {
        using var ctx = Create();

        Assert.Throws<TenancyException>(() => ctx.Orders.Select(o => new { Outer = new { Entity = o } })
            .ExecuteUpdate(s => s.SetProperty(x => x.Outer.Entity.TenantId, TenantId.New())));
        Assert.Throws<TenancyException>(() => ctx.Orders.Select(o => new { Entity = o })
            .Select(x => new { Again = x.Entity })
            .ExecuteUpdate(s => s.SetProperty(y => y.Again.TenantId, TenantId.New())));
    }

    [Fact]
    public void Projected_tenant_scalar_is_resolved_to_the_entity_property()
    {
        using var ctx = Create();

        // x.TenantId is a member of the anonymous type; it was projected from o.TenantId
        Assert.Throws<TenancyException>(() => ctx.Orders.Select(o => new { Entity = o, o.TenantId })
            .ExecuteUpdate(s => s.SetProperty(x => x.TenantId, TenantId.New())));
    }

    [Fact]
    public void Join_and_SelectMany_are_followed_into_both_sources()
    {
        using var ctx = Create();
        var other = TenantId.New();

        // Entity and scalar of the result selector, and a scalar projected before the join
        Assert.Throws<TenancyException>(() => ctx.Orders
            .Join(ctx.Customers, o => o.Id, c => c.Id, (o, c) => new { Order = o, c.Name })
            .ExecuteUpdate(s => s.SetProperty(x => x.Order.TenantId, other)));
        Assert.Throws<TenancyException>(() => ctx.Orders
            .Join(ctx.Customers, o => o.Id, c => c.Id, (o, c) => new { Order = o, c.TenantId })
            .ExecuteUpdate(s => s.SetProperty(x => x.TenantId, other)));
        Assert.Throws<TenancyException>(() => ctx.Orders.Select(o => new { Order = o, Tenant = o.TenantId })
            .Join(ctx.Customers, x => x.Order.Id, c => c.Id, (x, c) => new { x.Order, x.Tenant })
            .ExecuteUpdate(s => s.SetProperty(y => y.Tenant, other)));
        Assert.Throws<TenancyException>(() => ctx.Orders
            .SelectMany(o => ctx.Customers.Where(c => c.Id == o.Id), (o, c) => new { Order = o, c.TenantId })
            .ExecuteUpdate(s => s.SetProperty(x => x.TenantId, other)));
        AssertPasses(() => ctx.Orders
            .Join(ctx.Customers, o => o.Id, c => c.Id, (o, c) => new { Order = o, Customer = c })
            .ExecuteUpdate(s => s.SetProperty(x => x.Order.Total, 1m)));
    }

    [Fact]
    public void Projection_into_a_class_with_member_initializers_is_resolved()
    {
        using var ctx = Create();

        Assert.Throws<TenancyException>(() => ctx.Orders.Select(o => new Holder { Entity = o })
            .ExecuteUpdate(s => s.SetProperty(x => x.Entity.TenantId, TenantId.New())));
        Assert.Throws<TenancyException>(() => ctx.Orders.Select(o => new Holder { Entity = o, Tenant = o.TenantId })
            .ExecuteUpdate(s => s.SetProperty(x => x.Tenant, TenantId.New())));
    }

    [Fact]
    public void Forms_that_EF_does_not_translate_are_left_to_EF()
    {
        using var ctx = Create();

        // Limit: a constructor parameter names no member, so the check cannot follow it. EF
        // rejects these setters itself, with and without the option.
        var ex = Record.Exception(() => ctx.Orders.Select(o => new Positional(o, o.TenantId))
            .ExecuteUpdate(s => s.SetProperty(x => x.Tenant, TenantId.New())));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("could not be translated", ex.Message);
    }

    /// <summary>Projection target with member initializers.</summary>
    public sealed class Holder
    {
        public Order Entity { get; init; } = null!;
        public TenantId Tenant { get; init; }
    }

    /// <summary>Projection target with constructor parameters only.</summary>
    public sealed record Positional(Order Entity, TenantId Tenant);

    [Fact]
    public void Projected_shadow_tenant_property_through_EF_Property_is_rejected()
    {
        using var ctx = Create(model: m => m.Entity<Product>().Property<TenantId>("TenantId").IsTenantProperty());

        Assert.Throws<TenancyException>(() => ctx.Products.Select(p => new { Entity = p })
            .ExecuteUpdate(s => s.SetProperty(x => EF.Property<TenantId>(x.Entity, "TenantId"), TenantId.New())));
    }

    [Fact]
    public void Tenant_property_with_another_name_is_rejected_directly_and_projected()
    {
        using var ctx = Create(model: m => m.Entity<Contract>().Property(c => c.Mandant).IsTenantProperty());

        Assert.Throws<TenancyException>(() => ctx.Set<Contract>()
            .ExecuteUpdate(s => s.SetProperty(c => c.Mandant, TenantId.New())));
        Assert.Throws<TenancyException>(() => ctx.Set<Contract>().Select(c => new { Entity = c })
            .ExecuteUpdate(s => s.SetProperty(x => x.Entity.Mandant, TenantId.New())));
        AssertPasses(() => ctx.Set<Contract>().Select(c => new { Entity = c })
            .ExecuteUpdate(s => s.SetProperty(x => x.Entity.Id, 1)));
    }

    [Fact]
    public void Other_and_similarly_named_properties_pass_the_check()
    {
        // Products get a TenantId that is not a tenant property: no ITenantOwned, no marking
        using var ctx = Create(model: m => m.Entity<Product>().Property<TenantId>("TenantId"));

        AssertPasses(() => ctx.Orders.Select(o => new { Entity = o })
            .ExecuteUpdate(s => s.SetProperty(x => x.Entity.Total, 0m)));
        AssertPasses(() => ctx.Products
            .ExecuteUpdate(s => s.SetProperty(p => EF.Property<TenantId>(p, "TenantId"), TenantId.New())));
        AssertPasses(() => ctx.Products.Select(p => new { Entity = p })
            .ExecuteUpdate(s => s.SetProperty(x => EF.Property<TenantId>(x.Entity, "TenantId"), TenantId.New())));
        // The key of the tenant directory is a TenantId as well, but not a tenant property
        AssertPasses(() => ctx.Tenants.ExecuteUpdate(s => s.SetProperty(t => t.Name, "x")));
    }

    [Fact]
    public void Without_the_option_projected_setters_are_left_to_the_database()
    {
        using var ctx = Create(protect: false);

        AssertPasses(() => ctx.Orders.Select(o => new { Entity = o })
            .ExecuteUpdate(s => s.SetProperty(x => x.Entity.TenantId, TenantId.New())));
    }

    [Fact]
    public void Without_the_option_the_tenant_property_is_left_to_the_database()
    {
        using var ctx = Create(protect: false);

        var ex = Record.Exception(() => ctx.Orders.ExecuteUpdate(s => s.SetProperty(o => o.TenantId, TenantId.New())));
        Assert.IsNotType<TenancyException>(ex);
    }

    // Registers an interceptor in the internal provider under the given service type
    private sealed class RegisterAs<TService> : IDbContextOptionsExtension
        where TService : class
    {
        private readonly IInterceptor _interceptor;

        public RegisterAs(IInterceptor interceptor) => _interceptor = interceptor;

        public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

        public void ApplyServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
            => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
                .AddSingleton(services, typeof(TService), _interceptor);

        public void Validate(IDbContextOptions options)
        {
        }

        private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
        {
            public ExtensionInfo(IDbContextOptionsExtension extension)
                : base(extension)
            {
            }

            public override bool IsDatabaseProvider => false;

            public override string LogFragment => "";

            public override int GetServiceProviderHashCode() => typeof(TService).GetHashCode();

            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
                => other.Extension.GetType() == Extension.GetType();

            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
            }
        }
    }

    [Theory]
    [InlineData(typeof(IInterceptor), true)]
    [InlineData(typeof(ISingletonInterceptor), false)]
    public void Query_expression_interceptors_are_only_called_when_registered_as_IInterceptor(
        Type serviceType, bool called)
    {
        // EF behavior this plugin depends on: although IQueryExpressionInterceptor derives from
        // ISingletonInterceptor, a registration under that type is never called
        var extension = serviceType == typeof(IInterceptor)
            ? (IDbContextOptionsExtension)new RegisterAs<IInterceptor>(new TenantQueryExpressionInterceptor())
            : new RegisterAs<ISingletonInterceptor>(new TenantQueryExpressionInterceptor());
        using var ctx = Contexts.Create(configure: o =>
            ((IDbContextOptionsBuilderInfrastructure)o).AddOrUpdateExtension(extension));

        var ex = Record.Exception(() => ctx.Orders.ExecuteUpdate(s => s.SetProperty(o => o.TenantId, TenantId.New())));
        Assert.Equal(called, ex is TenancyException);
    }
}
