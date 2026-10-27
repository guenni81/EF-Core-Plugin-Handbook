using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Acme.EntityFrameworkCore.Tenancy;

/// <summary>
/// Value comparers for <see cref="TenantId"/> and its provider type <see cref="Guid"/>, as shared
/// instances for the type mappings.
/// </summary>
/// <remarks>
/// Without them EF creates a default comparer on first use through
/// <c>ValueComparer.CreateDefault</c>, which calls <c>MakeGenericMethod</c>. That fails in a
/// NativeAOT application, and the compiled model only references the mapping's
/// <c>Default</c> instance (chapter "Compiled models, trimming and NativeAOT").
/// </remarks>
public static class TenantIdValueComparer
{
    /// <summary>Compares <see cref="TenantId"/> values; also used as key comparer.</summary>
    public static ValueComparer<TenantId> Instance { get; } = new(
        (left, right) => left.Value == right.Value,
        id => id.Value.GetHashCode(),
        id => id);

    /// <summary>Compares the provider values (<see cref="Guid"/>).</summary>
    public static ValueComparer<Guid> Provider { get; } = new(
        (left, right) => left == right,
        value => value.GetHashCode(),
        value => value);
}
