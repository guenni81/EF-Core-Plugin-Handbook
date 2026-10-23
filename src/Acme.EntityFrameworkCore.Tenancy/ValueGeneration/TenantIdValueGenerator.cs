using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Acme.EntityFrameworkCore.Tenancy.ValueGeneration;

/// <summary>
/// Generates new <see cref="TenantId"/> values on the client (UUIDv7, time-ordered and therefore
/// index-friendly). It is not configured directly but created by
/// <see cref="TenantIdValueGeneratorFactory"/>, because EF Core 10's compiled model generator
/// only accepts <c>HasValueGeneratorFactory(Type)</c>.
/// </summary>
public class TenantIdValueGenerator : ValueGenerator<TenantId>
{
    /// <summary>Final values, no temporary placeholders until saving.</summary>
    public override bool GeneratesTemporaryValues => false;

    /// <summary>Returns a new UUIDv7-based ID.</summary>
    /// <param name="entry">The entry the value is generated for (not used).</param>
    /// <returns>A new <see cref="TenantId"/>.</returns>
    public override TenantId Next(EntityEntry entry) => TenantId.New();
}
