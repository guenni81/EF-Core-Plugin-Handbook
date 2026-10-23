namespace Acme.EntityFrameworkCore.Tenancy;

/// <summary>
/// Marker: the entity belongs to a tenant and is isolated (RLS, query filter, automatic
/// assignment on save). The property must be named "TenantId" and be implemented implicitly.
/// </summary>
/// <remarks>
/// Implicitly, because the conventions look the property up by name on the entity type; an
/// explicit interface implementation is not a mapped property.
/// </remarks>
public interface ITenantOwned
{
    /// <summary>The owning tenant. Set by the SaveChanges interceptor when left empty.</summary>
    TenantId TenantId { get; set; }
}
