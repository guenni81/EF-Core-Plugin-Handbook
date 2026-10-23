using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Acme.EntityFrameworkCore.Tenancy;

/// <summary>Converts <see cref="TenantId"/> to the provider type <see cref="Guid"/>.</summary>
public sealed class TenantIdConverter : ValueConverter<TenantId, Guid>
{
    /// <summary>
    /// Shared instance. Type mappings always use this instance so that the compiled model
    /// generator recognizes the converter as "unchanged" (chapter 21).
    /// </summary>
    public static TenantIdConverter Instance { get; } = new();

    /// <summary>
    /// Creates a converter. Prefer <see cref="Instance"/>; the constructor stays public for
    /// HasConversion&lt;TenantIdConverter&gt;().
    /// </summary>
    public TenantIdConverter()
        : base(id => id.Value, value => new TenantId(value))
    {
    }
}
