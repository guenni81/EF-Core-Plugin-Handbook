using Acme.EntityFrameworkCore.Tenancy.Diagnostics;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Conventions;

/// <summary>
/// Validates the RLS configuration on the finished model. Structural errors throw,
/// questionable configuration produces a warning (can be escalated via ConfigureWarnings).
/// </summary>
/// <remarks>
/// Custom validation as an IModelFinalizedConvention instead of replacing IModelValidator:
/// there is only one of those, and it belongs to the provider.
/// </remarks>
public class TenancyModelValidationConvention : IModelFinalizedConvention
{
    private readonly IDiagnosticsLogger<DbLoggerCategory.Model> _logger;
    private readonly TenancyLoggingDefinitions _loggingDefinitions;

    /// <summary>Creates the convention.</summary>
    /// <param name="logger">Model logger for the warnings.</param>
    /// <param name="loggingDefinitions">Cached log definitions of the plugin.</param>
    public TenancyModelValidationConvention(
        IDiagnosticsLogger<DbLoggerCategory.Model> logger,
        TenancyLoggingDefinitions loggingDefinitions)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(loggingDefinitions);

        _logger = logger;
        _loggingDefinitions = loggingDefinitions;
    }

    /// <summary>Checks every entity type that carries RLS configuration.</summary>
    /// <param name="model">The finalized (read-only) model.</param>
    /// <returns>The unchanged model.</returns>
    public virtual IModel ProcessModelFinalized(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            if (!entityType.HasRlsConfiguration())
                continue;

            // RLS is a table property: with TPH several types share the table, and the
            // annotation provider only reads the root type (TenancyAnnotationProvider)
            if (entityType.BaseType is not null)
                throw new InvalidOperationException(
                    $"RLS configuration on '{entityType.DisplayName()}' is invalid: it is only "
                    + "allowed on the root type of the hierarchy "
                    + $"('{entityType.GetRootType().DisplayName()}').");

            if (StoreObjectIdentifier.Create(entityType, StoreObjectType.Table) is null)
                throw new InvalidOperationException(
                    $"RLS configuration on '{entityType.DisplayName()}' is invalid: "
                    + "the entity is not mapped to a table.");

            // Policies without ENABLE ROW LEVEL SECURITY are created but never applied
            if (!entityType.IsRlsEnabled() && entityType.GetRlsPolicies().Count > 0)
                _logger.PoliciesWithoutRowLevelSecurity(_loggingDefinitions, entityType);
        }

        return model;
    }
}
