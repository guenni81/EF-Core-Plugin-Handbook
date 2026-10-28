using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Tests;

public class Order : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Shared master data without a tenant, in a container of its own.</summary>
[NoTenantIsolation]
public class Country
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public class MutableTenantProvider : ITenantProvider
{
    public TenantId? CurrentTenantId { get; set; }
}

public class ShopContext : DbContext
{
    public ShopContext(DbContextOptions<ShopContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Country> Countries => Set<Country>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultContainer("Shop");
        // One partition key per container: data without a tenant needs a container of its own
        modelBuilder.Entity<Country>().HasKey(c => c.Code);
        modelBuilder.Entity<Country>().ToContainer("Countries").HasPartitionKey(c => c.Code);

        modelBuilder.HasTenantQueryFilters(this);
    }
}

internal static class Contexts
{
    // The emulator's well-known key; model tests never open a connection
    internal const string OfflineConnectionString =
        "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    internal static ShopContext Offline(MutableTenantProvider? tenants = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<ITenantProvider>(tenants ?? new MutableTenantProvider())
            .BuildServiceProvider();
        return new ShopContext(new DbContextOptionsBuilder<ShopContext>()
            .UseCosmos(OfflineConnectionString, "shop", cosmos => cosmos.UseAcmeTenancy(services))
            .Options);
    }
}
