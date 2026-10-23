namespace Acme.EntityFrameworkCore.Tenancy;

/// <summary>
/// Supplies the tenant of the current operation (request, job, ...).
/// Implemented by the application and registered in the application container.
/// </summary>
public interface ITenantProvider
{
    /// <summary>Current tenant, or null if there is no tenant context.</summary>
    TenantId? CurrentTenantId { get; }
}
