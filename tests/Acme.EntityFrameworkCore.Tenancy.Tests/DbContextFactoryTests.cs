using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>Context without extra constructor parameters, so DI can create it.</summary>
public sealed class FactoryContext : DbContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="options">The options built by the DI registration.</param>
    public FactoryContext(DbContextOptions<FactoryContext> options)
        : base(options)
    {
    }

    /// <summary>Tenant-owned orders.</summary>
    public DbSet<Order> Orders => Set<Order>();
}

/// <summary>Scoped tenant provider with state, as set by middleware per request.</summary>
public sealed class ScopedTenantProvider : ITenantProvider
{
    /// <inheritdoc />
    public TenantId? CurrentTenantId { get; set; }
}

/// <summary>
/// AddDbContextFactory registers the factory and the options as singletons by default. The
/// options callback then receives the root provider, and a scoped tenant provider resolved
/// there is not the one of the current scope (chapter "Using the plugin in ASP.NET Core").
/// </summary>
public class DbContextFactoryTests
{
    private const string ConnectionString = "Host=localhost;Database=unused";

    private static ServiceProvider Build(ServiceLifetime factoryLifetime, bool validateScopes)
    {
        var services = new ServiceCollection();
        services.AddAcmeTenancy<ScopedTenantProvider>();
        services.AddDbContextFactory<FactoryContext>(
            (sp, options) => options.UseNpgsql(ConnectionString).UseAcmeTenancy(sp),
            factoryLifetime);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = validateScopes,
        });
    }

    private static ScopedTenantProvider TenantsOf(IServiceScope scope)
        => (ScopedTenantProvider)scope.ServiceProvider.GetRequiredService<ITenantProvider>();

    /// <summary>Adds an order and runs the SaveChanges logic of the plugin (no database).</summary>
    private static Order AddOrder(FactoryContext db)
    {
        var order = new Order();
        db.Orders.Add(order);

        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!
            .Interceptors!.OfType<TenantSaveChangesInterceptor>().Single()
            .ApplyTenant(db);
        return order;
    }

    [Fact]
    public void Singleton_factory_cannot_resolve_scoped_tenant_provider()
    {
        // Development default in ASP.NET Core: scope validation is on
        using var root = Build(ServiceLifetime.Singleton, validateScopes: true);
        using var scope = root.CreateScope();

        // Fails as soon as the singleton options are built: "Cannot resolve scoped service
        // 'ITenantProvider' from root provider."
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<FactoryContext>>().CreateDbContext());
    }

    [Fact]
    public void Without_scope_validation_the_tenant_of_the_scope_is_not_seen()
    {
        // Production default: no validation. The options callback silently gets a tenant
        // provider from the root container, not the one the request has filled.
        using var root = Build(ServiceLifetime.Singleton, validateScopes: false);
        using var scope = root.CreateScope();
        TenantsOf(scope).CurrentTenantId = TenantId.New();

        using var db = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<FactoryContext>>().CreateDbContext();

        Assert.Throws<TenancyException>(() => AddOrder(db));     // "no active tenant"
    }

    [Fact]
    public void Scoped_factory_uses_the_tenant_of_its_scope()
    {
        using var root = Build(ServiceLifetime.Scoped, validateScopes: true);

        foreach (var tenant in new[] { TenantId.New(), TenantId.New() })
        {
            using var scope = root.CreateScope();
            TenantsOf(scope).CurrentTenantId = tenant;

            using var db = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<FactoryContext>>().CreateDbContext();

            Assert.Equal(tenant, AddOrder(db).TenantId);
        }
    }
}
