namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Infrastructure;

/// <summary>
/// Settings of the Cosmos package; as a record (value equality) directly usable in the service
/// provider hash.
/// </summary>
public sealed record CosmosTenancySettings
{
    /// <summary>The defaults.</summary>
    public static CosmosTenancySettings Default { get; } = new();

    /// <summary>
    /// Check every materialized tenant-owned entity against the current tenant (off by default).
    /// Cosmos DB has no row level security, so this is the only check on the database side of
    /// the query filter (chapter "Without SQL: what carries over to Cosmos DB").
    /// </summary>
    public bool VerifyMaterializedTenants { get; init; }
}
