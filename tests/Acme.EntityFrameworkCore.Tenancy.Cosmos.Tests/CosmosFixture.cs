using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.CosmosDb;

namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Tests;

/// <summary>
/// The Cosmos DB emulator (Linux, vNext) in Docker. The image tag is pinned: the vNext emulator
/// is still a preview and changes behaviour between builds (chapter "Without SQL: what carries
/// over to Cosmos DB"). Gateway mode over the container's HTTP client is the only supported
/// connection mode.
/// </summary>
public sealed class CosmosFixture : IAsyncLifetime
{
    private readonly CosmosDbContainer _container =
        new CosmosDbBuilder("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-EN20261019")
            .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var ctx = CreateContext(new MutableTenantProvider());
        await ctx.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Connection string of the emulator, for contexts of other types.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>The container's HTTP client; gateway mode only works through it.</summary>
    public HttpClient HttpClient => _container.HttpClient;

    public ShopContext CreateContext(
        MutableTenantProvider tenants,
        bool verifyMaterializedTenants = false,
        Action<DbContextOptionsBuilder>? configure = null)
    {
        var services = new ServiceCollection().AddSingleton<ITenantProvider>(tenants).BuildServiceProvider();
        var builder = new DbContextOptionsBuilder<ShopContext>()
            .UseCosmos(_container.GetConnectionString(), "shop", cosmos => cosmos
                .ConnectionMode(ConnectionMode.Gateway)
                .HttpClientFactory(() => _container.HttpClient)
                .LimitToEndpoint()
                .UseAcmeTenancy(services, verifyMaterializedTenants))
            // Every test context has an application container of its own; cached, they would
            // count towards EF's limit of twenty internal service providers per process
            .EnableServiceProviderCaching(false);
        configure?.Invoke(builder);
        return new ShopContext(builder.Options);
    }
}
