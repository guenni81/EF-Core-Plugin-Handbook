using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// Tenant provider for code without an HTTP request (background jobs, message handlers).
/// Same implementation as samples/ShopSample/AmbientTenantProvider.cs.
/// </summary>
public sealed class AmbientTenantProvider : ITenantProvider
{
    private static readonly AsyncLocal<TenantId?> Current = new();

    /// <inheritdoc />
    public TenantId? CurrentTenantId => Current.Value;

    /// <summary>Sets the tenant until the returned scope is disposed.</summary>
    /// <param name="tenant">Tenant to activate.</param>
    public static IDisposable Use(TenantId tenant)
    {
        var previous = Current.Value;
        Current.Value = tenant;
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly TenantId? _previous;

        public Restore(TenantId? previous) => _previous = previous;

        public void Dispose() => Current.Value = _previous;
    }
}

/// <summary>
/// Background work: the tenant comes from an AsyncLocal scope instead of a request. Each job
/// sets the tenant first and creates its DI scope (and thus its context) inside it
/// (chapter "Using the plugin in ASP.NET Core").
/// </summary>
public class BackgroundTenantTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // Singleton is fine: the provider reads the AsyncLocal on every access
        services.AddAcmeTenancy<AmbientTenantProvider>(ServiceLifetime.Singleton);
        services.AddDbContext<FactoryContext>((sp, options) => options
            .UseNpgsql("Host=localhost;Database=unused")
            .UseAcmeTenancy(sp));

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static Order AddOrder(FactoryContext db)
    {
        var order = new Order();
        db.Orders.Add(order);

        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!
            .Interceptors!.OfType<TenantSaveChangesInterceptor>().Single()
            .ApplyTenant(db);
        return order;
    }

    /// <summary>The pattern of a job: tenant first, then scope and context.</summary>
    private static async Task<TenantId> RunJobAsync(IServiceProvider root, TenantId tenant)
    {
        using (AmbientTenantProvider.Use(tenant))
        {
            await using var scope = root.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FactoryContext>();

            await Task.Yield();        // continue on another thread: AsyncLocal flows along
            return AddOrder(db).TenantId;
        }
    }

    [Fact]
    public async Task Parallel_jobs_keep_their_own_tenant()
    {
        await using var root = Build();
        var tenants = Enumerable.Range(0, 20).Select(_ => TenantId.New()).ToArray();

        var results = await Task.WhenAll(tenants.Select(t => RunJobAsync(root, t)));

        Assert.Equal(tenants, results);
    }

    [Fact]
    public async Task Tenant_does_not_leak_after_the_job()
    {
        await using var root = Build();
        await RunJobAsync(root, TenantId.New());

        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryContext>();

        Assert.Throws<TenancyException>(() => AddOrder(db));
    }
}
