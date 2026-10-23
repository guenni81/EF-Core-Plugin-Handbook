using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Storage;

/// <summary>
/// TenantId &lt;-&gt; uniqueidentifier. The value converter and the JSON reader/writer come from
/// the core package; only store type and literal syntax are provider-specific.
/// </summary>
public class SqlServerTenantIdTypeMapping : RelationalTypeMapping
{
    /// <summary>The shared default instance (<c>uniqueidentifier</c>).</summary>
    public static SqlServerTenantIdTypeMapping Default { get; } = new();

    /// <summary>Creates the default mapping to <c>uniqueidentifier</c>.</summary>
    public SqlServerTenantIdTypeMapping()
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(TenantId),
                converter: TenantIdConverter.Instance,
                jsonValueReaderWriter: TenantIdJsonReaderWriter.Instance),
            storeType: "uniqueidentifier",
            dbType: System.Data.DbType.Guid))
    {
    }

    /// <summary>Copy constructor used by <see cref="Clone(RelationalTypeMappingParameters)" />.</summary>
    /// <param name="parameters">The parameters for the new mapping.</param>
    protected SqlServerTenantIdTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <summary>
    /// EF clones mappings to apply facets (e.g. a different store type); the clone must keep the
    /// concrete type, otherwise the TenantId-specific literal generation would be lost.
    /// </summary>
    /// <param name="parameters">The parameters for the clone.</param>
    /// <returns>A new <see cref="SqlServerTenantIdTypeMapping" />.</returns>
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new SqlServerTenantIdTypeMapping(parameters);

    /// <summary>
    /// Generates a <c>'xxxxxxxx-...'</c> literal; SQL Server converts the string implicitly to
    /// <c>uniqueidentifier</c>. Raw <see cref="Guid" /> values occur when EF passes the
    /// provider (converted) value instead of the model value.
    /// </summary>
    /// <param name="value">A <see cref="TenantId" /> or <see cref="Guid" />.</param>
    /// <returns>The SQL literal.</returns>
    protected override string GenerateNonNullSqlLiteral(object value)
        => value switch
        {
            TenantId id => $"'{id.Value}'",
            Guid g => $"'{g}'",
            _ => throw new ArgumentException($"Unexpected type {value.GetType()}.", nameof(value)),
        };
}

/// <summary>
/// Plugs the TenantId mapping into the SQL Server type mapping source. Plugins are asked before
/// the provider's own mappings, so returning <see langword="null" /> leaves all other types alone.
/// </summary>
public class SqlServerTenancyTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    /// <summary>
    /// Returns the TenantId mapping when no store type was requested or the requested store
    /// type is <c>uniqueidentifier</c>; an explicit other column type is not overridden.
    /// </summary>
    /// <param name="mappingInfo">What EF is looking for.</param>
    /// <returns>The mapping, or <see langword="null" /> if this plugin does not handle it.</returns>
    public virtual RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
        => mappingInfo.ClrType == typeof(TenantId)
           && (mappingInfo.StoreTypeNameBase is null
               || string.Equals(mappingInfo.StoreTypeNameBase, "uniqueidentifier", StringComparison.OrdinalIgnoreCase))
            ? SqlServerTenantIdTypeMapping.Default
            : null;
}
