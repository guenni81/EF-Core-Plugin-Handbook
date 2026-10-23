using Acme.EntityFrameworkCore.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ShopApi;

/// <summary>Tenant-owned order: the marker interface enables RLS for its table.</summary>
public class Order : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public decimal Total { get; set; }
}

/// <summary>
/// Plain context without any tenant code: the plugin's convention adds RLS to the model and
/// its interceptors handle the tenant at run time.
/// </summary>
public class ShopApiContext : DbContext
{
    public ShopApiContext(DbContextOptions<ShopApiContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
}
