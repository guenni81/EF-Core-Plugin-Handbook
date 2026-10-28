using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Acme.EntityFrameworkCore.Tenancy.Testing;

/// <summary>
/// Offers <see cref="TenantIdConverter"/> for <see cref="TenantId"/>, so that any provider maps
/// it as a <see cref="Guid"/>: EF asks the selector when its type mapping source knows a CLR
/// type only through a conversion (like enums).
/// </summary>
public class TenancyValueConverterSelector : ValueConverterSelector
{
    private static readonly ValueConverterInfo TenantIdToGuid = new(
        typeof(TenantId), typeof(Guid), _ => TenantIdConverter.Instance);

    /// <summary>Creates the selector.</summary>
    /// <param name="dependencies">EF's parameter object for the selector.</param>
    public TenancyValueConverterSelector(ValueConverterSelectorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>Returns the TenantId converter first, then EF's built-in converters.</summary>
    /// <param name="modelClrType">The CLR type of the property.</param>
    /// <param name="providerClrType">The requested provider type, if any.</param>
    /// <returns>The candidate converters.</returns>
    public override IEnumerable<ValueConverterInfo> Select(Type modelClrType, Type? providerClrType = null)
    {
        if (modelClrType == typeof(TenantId)
            && (providerClrType is null || providerClrType == typeof(Guid)))
            yield return TenantIdToGuid;

        foreach (var converter in base.Select(modelClrType, providerClrType))
            yield return converter;
    }
}
