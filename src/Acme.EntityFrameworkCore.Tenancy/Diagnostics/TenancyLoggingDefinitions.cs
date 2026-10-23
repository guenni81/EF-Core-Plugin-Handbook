using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Acme.EntityFrameworkCore.Tenancy.Diagnostics;

/// <summary>
/// Cached event definitions (same pattern as EF's LoggingDefinitions). Registered as a singleton
/// in the internal service provider; the warning behavior (log/throw/ignore) comes from the
/// logging options of that provider.
/// </summary>
public class TenancyLoggingDefinitions
{
    private EventDefinition<string>? _tenantIsolationApplied;
    private EventDefinition<string>? _tenantIsolationOptedOut;
    private EventDefinition<string>? _policiesWithoutRowLevelSecurity;

    /// <summary>Definition for <see cref="TenancyEventId.TenantIsolationApplied"/> (debug).</summary>
    /// <param name="logger">The logger whose options determine the warning behavior.</param>
    /// <returns>The cached event definition.</returns>
    public virtual EventDefinition<string> LogTenantIsolationApplied(IDiagnosticsLogger logger)
        => LazyInitializer.EnsureInitialized(ref _tenantIsolationApplied, () =>
            Create(logger, TenancyEventId.TenantIsolationApplied, LogLevel.Debug,
                nameof(TenancyEventId.TenantIsolationApplied),
                "Tenant isolation enabled for entity '{entityType}'."));

    /// <summary>Definition for <see cref="TenancyEventId.TenantIsolationOptedOut"/> (warning).</summary>
    /// <param name="logger">The logger whose options determine the warning behavior.</param>
    /// <returns>The cached event definition.</returns>
    public virtual EventDefinition<string> LogTenantIsolationOptedOut(IDiagnosticsLogger logger)
        => LazyInitializer.EnsureInitialized(ref _tenantIsolationOptedOut, () =>
            Create(logger, TenancyEventId.TenantIsolationOptedOut, LogLevel.Warning,
                nameof(TenancyEventId.TenantIsolationOptedOut),
                "Entity '{entityType}' implements ITenantOwned but is excluded from "
                + "tenant isolation. Its data is NOT protected by RLS."));

    /// <summary>
    /// Definition for <see cref="TenancyEventId.PoliciesWithoutRowLevelSecurity"/> (warning).
    /// </summary>
    /// <param name="logger">The logger whose options determine the warning behavior.</param>
    /// <returns>The cached event definition.</returns>
    public virtual EventDefinition<string> LogPoliciesWithoutRowLevelSecurity(IDiagnosticsLogger logger)
        => LazyInitializer.EnsureInitialized(ref _policiesWithoutRowLevelSecurity, () =>
            Create(logger, TenancyEventId.PoliciesWithoutRowLevelSecurity, LogLevel.Warning,
                nameof(TenancyEventId.PoliciesWithoutRowLevelSecurity),
                "Policies are defined for '{entityType}', but row level security is not "
                + "enabled. The policies have no effect."));

    // The logger is only used on first access: the options (and with them the warning
    // behavior) are fixed per internal service provider, which this singleton belongs to.
    private static EventDefinition<string> Create(
        IDiagnosticsLogger logger, EventId eventId, LogLevel level, string name, string message)
        => new(
            logger.Options,             // contains the warning behavior
            eventId,
            level,
            "TenancyEventId." + name,   // appears in the message for warning-as-error
            l => LoggerMessage.Define<string>(l, eventId, message));
}
