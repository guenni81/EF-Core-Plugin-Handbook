using Microsoft.EntityFrameworkCore;

namespace ShopSample;

/// <summary>Context of the console sample, with some explicit RLS configuration.</summary>
public class ShopContext : DbContext
{
    public ShopContext(DbContextOptions<ShopContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The convention uses the final column name in the generated policy
        modelBuilder.Entity<Order>().Property(o => o.TenantId).HasColumnName("tenant_id");

        // Additional policy: the support role may read customers of all tenants.
        // Permissive policies are combined with OR. The role must exist before the
        // migration creates this policy (see Program.cs, step 2).
        modelBuilder.Entity<Customer>().HasPolicy("support_read", p => p
            .For(PolicyCommand.Select)
            .To("shop_support")
            .Using("true"));
    }
}
