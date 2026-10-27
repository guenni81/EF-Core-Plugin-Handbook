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
