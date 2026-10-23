using Acme.EntityFrameworkCore.Tenancy;

namespace ShopSample;

/// <summary>Tenant-owned customer; readable for all tenants by the support role.</summary>
public class Customer : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Name { get; set; } = "";
    public List<Order> Orders { get; set; } = [];
}

/// <summary>Tenant-owned order; its tenant column is renamed in the model.</summary>
public class Order : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Shared across tenants (catalog) -> no marker, no isolation.</summary>
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Tenant-aware, but deliberately excluded (central audit). Produces a warning.</summary>
[NoTenantIsolation]
public class AuditEntry : ITenantOwned
{
    public long Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Message { get; set; } = "";
}
