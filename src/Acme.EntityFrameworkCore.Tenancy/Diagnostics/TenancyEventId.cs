using Microsoft.Extensions.Logging;

namespace Acme.EntityFrameworkCore.Tenancy.Diagnostics;

/// <summary>
/// Event IDs of the plugin. Users can filter them or turn them into errors via
/// <c>ConfigureWarnings(w =&gt; w.Throw(TenancyEventId.TenantIsolationOptedOut))</c>.
/// </summary>
public static class TenancyEventId
{
    /// <summary>
    /// Own range that does not collide with EF (0-99999) or providers.
    /// A real package should choose a unique range and document it.
    /// </summary>
    public const int TenancyBaseId = 1_150_000;

    // Never reorder or remove members: the numeric IDs are public and users filter on them
    private enum Id
    {
        // Model
        TenantIsolationApplied = TenancyBaseId,
        TenantIsolationOptedOut,
        PoliciesWithoutRowLevelSecurity,
    }

    // EF's naming scheme "Assembly.Category.Event", so that prefix filters work
    private static readonly string ModelPrefix = "Acme.EntityFrameworkCore.Tenancy.Model.";

    /// <summary>Debug: tenant isolation was enabled for an entity.</summary>
    public static readonly EventId TenantIsolationApplied = Make(Id.TenantIsolationApplied);

    /// <summary>Warning: an ITenantOwned entity was excluded from isolation.</summary>
    public static readonly EventId TenantIsolationOptedOut = Make(Id.TenantIsolationOptedOut);

    /// <summary>Warning: policies are defined but RLS is not enabled (policies have no effect).</summary>
    public static readonly EventId PoliciesWithoutRowLevelSecurity =
        Make(Id.PoliciesWithoutRowLevelSecurity);

    private static EventId Make(Id id) => new((int)id, ModelPrefix + id);
}
