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
                .UseAcmeTenancy(services, verifyMaterializedTenants));
        configure?.Invoke(builder);
        return new ShopContext(builder.Options);
    }
}
