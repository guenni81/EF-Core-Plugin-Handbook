namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;

/// <summary>
/// All model-relevant settings. As a record: Equals/GetHashCode over all fields,
/// which is used directly for the service provider hash (chapter "The options extension").
/// </summary>
public sealed record TenancySettings
{
    /// <summary>The default settings.</summary>
    public static TenancySettings Default { get; } = new();

    /// <summary>Session setting holding the current tenant ID.</summary>
    public string SettingName { get; init; } = "app.tenant_id";

    /// <summary>Name of the automatically generated policy.</summary>
    public string PolicyName { get; init; } = "tenant_isolation";

    /// <summary>FORCE ROW LEVEL SECURITY: policies also apply to the table owner.</summary>
    public bool ForceRowLevelSecurity { get; init; } = true;

    /// <summary>Additional named EF query filter (defense in depth, off by default).</summary>
    public bool ApplyQueryFilter { get; init; }

    /// <summary>Create an index on the tenant column if none starts with it.</summary>
    public bool CreateTenantIndex { get; init; } = true;

    /// <summary>
    /// Check every materialized tenant-owned entity against the current tenant (defense in
    /// depth, off by default). Registers a singleton materialization interceptor.
    /// </summary>
    public bool VerifyMaterializedTenants { get; init; }

    /// <summary>
    /// Reject ExecuteUpdate calls that set the tenant property (off by default). Registers a
    /// singleton query expression interceptor.
    /// </summary>
    public bool ProtectBulkOperations { get; init; }

    /// <summary>
    /// Set the tenant per transaction (<c>set_config(..., true)</c>) instead of per session, for
    /// PgBouncer in transaction mode (off by default). Commands outside a transaction then have
    /// no tenant.
    /// </summary>
    public bool UseTransactionPooling { get; init; }

    /// <summary>
    /// Tag the current activity with <c>tenant.id</c> before every command (default: off).
    /// Registers a command interceptor in the internal service provider.
    /// </summary>
    public bool EnrichOpenTelemetry { get; init; }
}
