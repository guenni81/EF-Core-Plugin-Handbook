using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Acme.EntityFrameworkCore.Tenancy.Diagnostics;

/// <summary>
/// Logging methods in EF style: ILogger (incl. warning-as-error) AND DiagnosticSource/LogTo.
/// </summary>
public static class TenancyLoggerExtensions
{
    /// <summary>Logs <see cref="TenancyEventId.TenantIsolationApplied"/>.</summary>
    /// <param name="diagnostics">The model logger.</param>
    /// <param name="definitions">The cached event definitions of the plugin.</param>
    /// <param name="entityType">The entity type that is now tenant-isolated.</param>
    public static void TenantIsolationApplied(
        this IDiagnosticsLogger<DbLoggerCategory.Model> diagnostics,
        TenancyLoggingDefinitions definitions,
        IReadOnlyEntityType entityType)
        => LogEntityType(diagnostics, definitions.LogTenantIsolationApplied(diagnostics), entityType);

    /// <summary>Logs <see cref="TenancyEventId.TenantIsolationOptedOut"/>.</summary>
    /// <param name="diagnostics">The model logger.</param>
    /// <param name="definitions">The cached event definitions of the plugin.</param>
    /// <param name="entityType">The ITenantOwned entity type excluded from isolation.</param>
    public static void TenantIsolationOptedOut(
        this IDiagnosticsLogger<DbLoggerCategory.Model> diagnostics,
        TenancyLoggingDefinitions definitions,
        IReadOnlyEntityType entityType)
        => LogEntityType(diagnostics, definitions.LogTenantIsolationOptedOut(diagnostics), entityType);

    /// <summary>Logs <see cref="TenancyEventId.PoliciesWithoutRowLevelSecurity"/>.</summary>
    /// <param name="diagnostics">The model logger.</param>
    /// <param name="definitions">The cached event definitions of the plugin.</param>
    /// <param name="entityType">The entity type with policies but without RLS.</param>
    public static void PoliciesWithoutRowLevelSecurity(
        this IDiagnosticsLogger<DbLoggerCategory.Model> diagnostics,
        TenancyLoggingDefinitions definitions,
        IReadOnlyEntityType entityType)
        => LogEntityType(
            diagnostics, definitions.LogPoliciesWithoutRowLevelSecurity(diagnostics), entityType);

    // Same pattern as EF's CoreLoggerExtensions: both channels are checked separately,
    // so the event data is only allocated when someone actually listens.
    private static void LogEntityType(
        IDiagnosticsLogger<DbLoggerCategory.Model> diagnostics,
        EventDefinition<string> definition,
        IReadOnlyEntityType entityType)
    {
        // 1. Classic logging (throws with ConfigureWarnings(w => w.Throw(...)))
        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics, entityType.DisplayName());
        }

        // 2. DiagnosticSource and LogTo (simple logging)
        if (diagnostics.NeedsEventData(definition, out var diagnosticSourceEnabled, out var simpleLogEnabled))
        {
            var eventData = new EntityTypeEventData(definition, GenerateMessage, entityType);
            diagnostics.DispatchEventData(definition, eventData, diagnosticSourceEnabled, simpleLogEnabled);
        }
    }

    // Static so no closure is allocated; EF calls it lazily when the message is needed
    private static string GenerateMessage(EventDefinitionBase definition, EventData payload)
        => ((EventDefinition<string>)definition)
            .GenerateMessage(((EntityTypeEventData)payload).EntityType.DisplayName());
}
