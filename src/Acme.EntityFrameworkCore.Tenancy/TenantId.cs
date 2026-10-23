namespace Acme.EntityFrameworkCore.Tenancy;

/// <summary>
/// Strongly typed tenant ID. Prevents mix-ups with other Guid values.
/// The PostgreSQL package maps it to uuid via a type mapping plugin, without configuration.
/// </summary>
/// <param name="Value">The underlying Guid.</param>
public readonly record struct TenantId(Guid Value)
{
    /// <summary>Empty ID (00000000-0000-0000-0000-000000000000).</summary>
    public static TenantId Empty => default;

    /// <summary>True if the ID is empty. Translated to SQL in LINQ.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>New, time-ordered ID (UUIDv7, index-friendly).</summary>
    public static TenantId New() => new(Guid.CreateVersion7());

    /// <summary>Parses a Guid string.</summary>
    /// <param name="value">A Guid in any format accepted by <see cref="Guid.Parse(string)"/>.</param>
    /// <returns>The parsed ID.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not a valid Guid.</exception>
    public static TenantId Parse(string value) => new(Guid.Parse(value));

    /// <summary>
    /// The Guid in its default "D" format. The PostgreSQL session interceptor writes exactly
    /// this string into the session setting, which the policies cast back to uuid.
    /// </summary>
    public override string ToString() => Value.ToString();
}
