using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;

/// <summary>
/// Policy description. Stored as a JSON string in an annotation: EF can only write simple
/// values to snapshots/migrations, and the differ compares strings reliably via Equals
/// (a string[] in the record would be compared by reference).
/// </summary>
/// <param name="Name">Policy name, unique per table.</param>
/// <param name="Command">Command the policy applies to.</param>
/// <param name="Permissive">
/// <see langword="true"/> = PERMISSIVE, <see langword="false"/> = RESTRICTIVE.
/// </param>
/// <param name="Roles">Roles (TO ...); <see langword="null"/> = PUBLIC.</param>
/// <param name="Using">USING expression, or <see langword="null"/>.</param>
/// <param name="WithCheck">WITH CHECK expression, or <see langword="null"/>.</param>
public sealed record RlsPolicy(
    string Name,
    PolicyCommand Command,
    bool Permissive,
    string[]? Roles,
    string? Using,
    string? WithCheck)
{
    /// <summary>Serializes the policy to the JSON stored in the annotation.</summary>
    /// <returns>The JSON string.</returns>
    public string Serialize()
        => JsonSerializer.Serialize(this, RlsJsonContext.Default.RlsPolicy);

    /// <summary>Reads a policy from the JSON stored in an annotation.</summary>
    /// <param name="json">The annotation value.</param>
    /// <returns>The policy.</returns>
    /// <exception cref="InvalidOperationException">The JSON contains <c>null</c>.</exception>
    public static RlsPolicy Deserialize(string json)
        => JsonSerializer.Deserialize(json, RlsJsonContext.Default.RlsPolicy)
           ?? throw new InvalidOperationException("Invalid RLS policy annotation.");
}

/// <summary>Source-generated serializer: trimming and AOT compatible.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(RlsPolicy))]
internal partial class RlsJsonContext : JsonSerializerContext;
