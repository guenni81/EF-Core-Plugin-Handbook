using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Storage;

/// <summary>
/// Converts <see cref="TenantId"/> to a string. Cosmos DB accepts only strings, numbers and
/// booleans as partition key values, so the core's Guid converter is not enough here.
/// </summary>
public sealed class TenantIdStringConverter : ValueConverter<TenantId, string>
{
    /// <summary>Shared instance, used by the type mapping.</summary>
    public static TenantIdStringConverter Instance { get; } = new();

    /// <summary>Creates a converter; prefer <see cref="Instance"/>.</summary>
    public TenantIdStringConverter()
        : base(id => id.Value.ToString("D"), value => new TenantId(Guid.Parse(value)))
    {
    }
}
