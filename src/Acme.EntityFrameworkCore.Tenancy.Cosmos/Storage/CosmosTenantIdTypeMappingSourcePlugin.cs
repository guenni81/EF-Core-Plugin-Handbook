using Microsoft.EntityFrameworkCore.Cosmos.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Storage;

/// <summary>
/// Maps <see cref="TenantId"/> to a JSON string. Cosmos has no plugin interface of its own; its
/// type mapping source asks the core plugins first.
/// </summary>
/// <remarks>
/// Builds on CosmosTypeMapping from an *.Internal namespace (EF1001): the only concrete mapping
/// class the Cosmos provider understands.
/// </remarks>
public class CosmosTenantIdTypeMappingSourcePlugin : ITypeMappingSourcePlugin
{
    private static readonly CoreTypeMapping Mapping = new CosmosTypeMapping(typeof(string))
        .WithComposedConverter(TenantIdStringConverter.Instance);

    /// <summary>Returns the mapping for <see cref="TenantId"/>, <see langword="null"/> otherwise.</summary>
    /// <param name="mappingInfo">The requested CLR type.</param>
    /// <returns>The mapping or <see langword="null"/>.</returns>
    public virtual CoreTypeMapping? FindMapping(in TypeMappingInfo mappingInfo)
        => mappingInfo.ClrType == typeof(TenantId) ? Mapping : null;
}
