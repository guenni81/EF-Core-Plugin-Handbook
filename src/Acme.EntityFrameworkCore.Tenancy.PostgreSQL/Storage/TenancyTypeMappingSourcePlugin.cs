using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Storage;

/// <summary>
/// Makes TenantId a scalar type without users having to configure HasConversion.
/// Queried by RelationalTypeMappingSource BEFORE the provider's mappings.
/// </summary>
public class TenancyTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    /// <summary>Returns the uuid mapping for <see cref="TenantId"/>, otherwise null.</summary>
    /// <param name="mappingInfo">What EF is looking for (CLR type, store type, facets).</param>
    /// <returns>The mapping, or <see langword="null"/> to let other sources decide.</returns>
    public virtual RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;
        if (clrType != typeof(TenantId))
            return null;

        // Explicitly different column type (HasColumnType("text")): not responsible,
        // the user can then configure a converter themselves.
        var storeTypeBase = mappingInfo.StoreTypeNameBase;
        if (storeTypeBase is not null && !string.Equals(storeTypeBase, "uuid", StringComparison.OrdinalIgnoreCase))
            return null;

        return TenantIdTypeMapping.Default;
    }
}
