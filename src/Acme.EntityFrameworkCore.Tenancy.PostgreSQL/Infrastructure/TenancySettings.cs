namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;

/// <summary>
/// All model-relevant settings. As a record: Equals/GetHashCode over all fields,
/// which is used directly for the service provider hash (chapter 4).
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
}
