using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acme.EntityFrameworkCore.Tenancy;

/// <summary>
/// System.Text.Json converter for <see cref="TenantId"/>: a plain GUID string in the "D" format
/// instead of an object with <c>Value</c> and <c>IsEmpty</c>. Applied through the attribute on
/// <see cref="TenantId"/>, so web APIs need no registration. EF Core's JSON columns do not use
/// it; they have <see cref="TenantIdJsonReaderWriter"/>.
/// </summary>
public sealed class TenantIdJsonConverter : JsonConverter<TenantId>
{
    /// <summary>Reads a GUID string in any format <see cref="Guid.TryParse(string?, out Guid)"/> accepts.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="typeToConvert">Always <see cref="TenantId"/>.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The tenant ID.</returns>
    /// <exception cref="JsonException">The value is not a string containing a GUID.</exception>
    public override TenantId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String ? Parse(reader.GetString())
            : throw new JsonException($"Expected a GUID string for {nameof(TenantId)}, got {reader.TokenType}.");

    /// <summary>Writes the GUID in the "D" format.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The tenant ID.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, TenantId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.Value);
    }

    /// <summary>Reads a tenant ID used as a dictionary key.</summary>
    /// <param name="reader">The reader, positioned on the property name.</param>
    /// <param name="typeToConvert">Always <see cref="TenantId"/>.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The tenant ID.</returns>
    /// <exception cref="JsonException">The property name is not a GUID.</exception>
    public override TenantId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Parse(reader.GetString());

    /// <summary>Writes a tenant ID used as a dictionary key.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The tenant ID.</param>
    /// <param name="options">The serializer options.</param>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, TenantId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(value.ToString());
    }

    private static TenantId Parse(string? value)
        => Guid.TryParse(value, out var guid) ? new TenantId(guid)
            : throw new JsonException($"'{value}' is not a valid {nameof(TenantId)}.");
}
