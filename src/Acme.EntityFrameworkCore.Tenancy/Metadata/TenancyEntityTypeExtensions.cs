using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Acme.EntityFrameworkCore.Tenancy.Metadata;

/// <summary>Read access to tenant metadata, shared by all provider packages.</summary>
public static class TenancyEntityTypeExtensions
{
    /// <summary>
    /// Determines the tenant property for conventions: a property marked via IsTenantProperty(),
    /// otherwise "TenantId" for entities implementing <see cref="ITenantOwned"/>.
    /// </summary>
    /// <param name="entityType">The entity type to inspect.</param>
    /// <returns>The candidate property, or null if the entity is not tenant-owned.</returns>
    /// <exception cref="InvalidOperationException">More than one property is marked.</exception>
    public static IReadOnlyProperty? FindTenantPropertyCandidate(this IReadOnlyEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        var marked = entityType.GetProperties()
            .Where(p => p[TenancyAnnotationNames.IsTenantProperty] is true)
            .ToList();

        if (marked.Count > 1)
            throw new InvalidOperationException(
                $"'{entityType.DisplayName()}' has multiple tenant properties: "
                + string.Join(", ", marked.Select(p => p.Name)) + ".");

        // An explicit marking takes precedence over the ITenantOwned naming convention
        if (marked.Count == 1)
            return marked[0];

        return typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType)
            ? entityType.FindProperty(nameof(ITenantOwned.TenantId))
            : null;
    }

    /// <summary>
    /// Name of the tenant property as determined by the convention
    /// (null = entity is not tenant-isolated). Also works on the runtime model.
    /// </summary>
    /// <param name="entityType">The entity type; derived types use their root type.</param>
    /// <returns>The property name, or null.</returns>
    public static string? GetTenantPropertyName(this IReadOnlyEntityType entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        // Stored only on the root: in a hierarchy all types share one table and one policy
        return entityType.GetRootType()[TenancyAnnotationNames.TenantProperty] as string;
    }
}
