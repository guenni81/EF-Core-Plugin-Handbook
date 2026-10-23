using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Acme.EntityFrameworkCore.Tenancy.Interceptors;

/// <summary>
/// Provider-independent: sets the tenant property of new entities and prevents data of another
/// tenant from being written or reassigned. Complements the isolation in the database
/// (defense in depth) and produces more understandable errors.
/// </summary>
/// <remarks>
/// The interceptor reads which entities are affected and what their tenant property is called
/// from the runtime annotation <see cref="TenancyAnnotationNames.TenantProperty"/>, which the
/// isolation convention sets. Entities that opted out do not have it.
/// Bulk operations (ExecuteUpdate/ExecuteDelete) and raw SQL do NOT pass through here.
/// </remarks>
public class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantProvider _tenants;

    /// <summary>Creates the interceptor.</summary>
    /// <param name="tenants">The tenant provider from the application container.</param>
    public TenantSaveChangesInterceptor(ITenantProvider tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        _tenants = tenants;
    }

    /// <summary>Applies and checks the tenant before a synchronous SaveChanges.</summary>
    /// <exception cref="TenancyException">The changes violate tenant separation.</exception>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
            ApplyTenant(context);

        return result;
    }

    /// <summary>
    /// Applies and checks the tenant before SaveChangesAsync. EF calls only one of the two
    /// variants, so both must be overridden.
    /// </summary>
    /// <exception cref="TenancyException">The changes violate tenant separation.</exception>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
            ApplyTenant(context);

        return ValueTask.FromResult(result);
    }

    // Internal so that unit tests can call it via InternalsVisibleTo without a database
    internal void ApplyTenant(DbContext context)
    {
        // Collected first: the tenant is only required when tenant-owned entities are
        // actually saved, so contexts without such changes work without a tenant.
        List<(EntityEntry Entry, PropertyEntry Property)>? changes = null;

        // Entries() runs DetectChanges (unless disabled), so snapshot-tracked edits are seen
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (entry.Metadata.GetTenantPropertyName() is not { } propertyName)
                continue;

            (changes ??= []).Add((entry, entry.Property(propertyName)));
        }

        if (changes is null)
            return;

        var current = _tenants.CurrentTenantId
            ?? throw new TenancyException(
                "Tenant-owned entities can only be saved while a tenant is active.");

        foreach (var (entry, property) in changes)
        {
            var currentValue = (TenantId)property.CurrentValue!;
            var originalValue = (TenantId)property.OriginalValue!;

            switch (entry.State)
            {
                case EntityState.Added when currentValue.IsEmpty:
                    property.CurrentValue = current;
                    break;

                case EntityState.Added when currentValue != current:
                    throw Mismatch(entry, currentValue, current);

                // Reassigning a row to another tenant is never allowed, not even to the
                // current one: it would move data across the isolation boundary.
                case EntityState.Modified when property.IsModified:
                    throw new TenancyException(
                        $"The tenant property of '{entry.Metadata.DisplayName()}' "
                        + "must not be changed.");

                // The original value counts: it reflects the row as it is in the database
                case EntityState.Modified or EntityState.Deleted when originalValue != current:
                    throw Mismatch(entry, originalValue, current);
            }
        }
    }

    private static TenancyException Mismatch(EntityEntry entry, TenantId entityTenant, TenantId current)
        => new($"'{entry.Metadata.DisplayName()}' belongs to tenant {entityTenant}, "
               + $"but the active tenant is {current}.");
}
