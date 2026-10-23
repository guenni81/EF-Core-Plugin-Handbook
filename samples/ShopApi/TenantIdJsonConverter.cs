using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.EntityFrameworkCore.Tenancy;

namespace ShopApi;

/// <summary>
/// Writes <see cref="TenantId"/> as a plain GUID in API responses instead of an object with
/// <c>Value</c> and <c>IsEmpty</c>. Purely a matter of the web API; EF uses its own mapping.
/// </summary>
public sealed class TenantIdJsonConverter : JsonConverter<TenantId>
{
    /// <inheritdoc />
    public override TenantId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetGuid());

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TenantId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
