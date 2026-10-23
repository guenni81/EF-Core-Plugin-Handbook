using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;

/// <summary>
/// Read access to RLS annotations. Works on entity types, tables and migration
/// operations, since they all implement <see cref="IReadOnlyAnnotatable"/>.
/// </summary>
public static class RlsAnnotatableExtensions
{
    /// <summary>Is RLS enabled (ENABLE ROW LEVEL SECURITY)?</summary>
    /// <param name="annotatable">Entity type, table or migration operation.</param>
    /// <returns><see langword="true"/> if the Enabled annotation is <see langword="true"/>.</returns>
    public static bool IsRlsEnabled(this IReadOnlyAnnotatable annotatable)
        => annotatable[RlsAnnotationNames.Enabled] as bool? ?? false;

    /// <summary>Is RLS forced (FORCE ROW LEVEL SECURITY)?</summary>
    /// <param name="annotatable">Entity type, table or migration operation.</param>
    /// <returns><see langword="true"/> if the Forced annotation is <see langword="true"/>.</returns>
    public static bool IsRlsForced(this IReadOnlyAnnotatable annotatable)
        => annotatable[RlsAnnotationNames.Forced] as bool? ?? false;

    /// <summary>Opt-out marker (HasNoRowLevelSecurity / [NoTenantIsolation]).</summary>
    /// <param name="annotatable">Entity type, table or migration operation.</param>
    /// <returns><see langword="true"/> if Enabled is explicitly <see langword="false"/>.</returns>
    public static bool IsRlsOptedOut(this IReadOnlyAnnotatable annotatable)
        => annotatable[RlsAnnotationNames.Enabled] is false;

    /// <summary>Policy name -&gt; serialized policy (JSON).</summary>
    /// <param name="annotatable">Entity type, table or migration operation.</param>
    /// <returns>All policies, keyed by name without the annotation prefix.</returns>
    public static IReadOnlyDictionary<string, string> GetRlsPolicies(
        this IReadOnlyAnnotatable annotatable)
        => annotatable.GetAnnotations()
            .Where(a => a.Name.StartsWith(RlsAnnotationNames.PolicyPrefix, StringComparison.Ordinal))
            .ToDictionary(
                a => a.Name[RlsAnnotationNames.PolicyPrefix.Length..],
                a => (string)a.Value!);

    /// <summary>Does the element carry RLS configuration (apart from the opt-out marker)?</summary>
    /// <param name="annotatable">Entity type, table or migration operation.</param>
    /// <returns><see langword="true"/> if any RLS annotation other than the opt-out is present.</returns>
    public static bool HasRlsConfiguration(this IReadOnlyAnnotatable annotatable)
        => annotatable.GetAnnotations().Any(a =>
            a.Name.StartsWith(RlsAnnotationNames.Prefix, StringComparison.Ordinal)
            && !(a.Name == RlsAnnotationNames.Enabled && a.Value is false));
}
