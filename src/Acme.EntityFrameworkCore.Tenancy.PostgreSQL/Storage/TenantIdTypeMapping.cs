using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Storage;

/// <summary>
/// Type mapping TenantId &lt;-&gt; uuid. Contains the converter, JSON reader/writer and
/// SQL literal generation.
/// </summary>
public class TenantIdTypeMapping : RelationalTypeMapping
{
    /// <summary>
    /// Public static default instance: required for compiled models.
    /// The generator emits code like <c>TenantIdTypeMapping.Default.Clone(...)</c>.
    /// </summary>
    public static TenantIdTypeMapping Default { get; } = new();

    /// <summary>Creates the default mapping (store type uuid, DbType.Guid).</summary>
    public TenantIdTypeMapping()
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(TenantId),
                converter: TenantIdConverter.Instance,
                // Shared comparers: EF would otherwise create them through reflection, which
                // NativeAOT does not support
                comparer: TenantIdValueComparer.Instance,
                keyComparer: TenantIdValueComparer.Instance,
                providerValueComparer: TenantIdValueComparer.Provider,
                // Needed for primitive collections and JSON columns that contain TenantId
                jsonValueReaderWriter: TenantIdJsonReaderWriter.Instance),
            storeType: "uuid",
            dbType: System.Data.DbType.Guid))
    {
    }

    /// <summary>Constructor for <see cref="Clone(RelationalTypeMappingParameters)"/>.</summary>
    /// <param name="parameters">The parameters of the cloned mapping.</param>
    protected TenantIdTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <summary>EF clones mappings, e.g. for facets or changed comparers.</summary>
    /// <param name="parameters">The new parameters.</param>
    /// <returns>A new mapping of this type.</returns>
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new TenantIdTypeMapping(parameters);

    /// <summary>
    /// Literal for constants in SQL. The base type applies the converter beforehand,
    /// but depending on the caller the model type can arrive as well.
    /// </summary>
    /// <param name="value">A <see cref="TenantId"/> or <see cref="Guid"/>.</param>
    /// <returns>The literal, e.g. <c>'...'::uuid</c>.</returns>
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var guid = value switch
        {
            TenantId id => id.Value,
            Guid g => g,
            _ => throw new ArgumentException(
                $"Unexpected value of type {value.GetType()} for TenantId.", nameof(value)),
        };

        // A Guid's ToString() contains only hex digits and dashes: no escaping needed
        return $"'{guid}'::uuid";
    }
}
