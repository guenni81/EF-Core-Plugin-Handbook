using Acme.EntityFrameworkCore.Tenancy.Diagnostics;
using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Conventions;

/// <summary>
/// SQL Server counterpart of the PostgreSQL convention. The candidate search from the core and
/// the runtime annotation stay the same; only what is recorded for migrations differs (a column
/// for the security policy instead of policy expressions).
/// </summary>
/// <remarks>
/// A model finalizing convention, because only then are base types, owned types and table
/// mappings settled.
/// </remarks>
public class SqlServerTenantIsolationConvention : IModelFinalizingConvention
{
    private readonly IDiagnosticsLogger<DbLoggerCategory.Model> _logger;
    private readonly TenancyLoggingDefinitions _loggingDefinitions;

    /// <summary>Creates the convention.</summary>
    /// <param name="logger">The model logger.</param>
    /// <param name="loggingDefinitions">The tenancy package's cached log definitions.</param>
    public SqlServerTenantIsolationConvention(
        IDiagnosticsLogger<DbLoggerCategory.Model> logger,
        TenancyLoggingDefinitions loggingDefinitions)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(loggingDefinitions);

        _logger = logger;
        _loggingDefinitions = loggingDefinitions;
    }

    /// <summary>
    /// Annotates every tenant-owned root entity type with its tenant column and ensures an index
    /// on it.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="context">The convention context.</param>
    /// <exception cref="InvalidOperationException">The tenant property is not a TenantId.</exception>
    public virtual void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        // Mappings that would leave tenant data in tables without a tenant column are rejected
        // before any table is annotated (same contract as the PostgreSQL package)
        TenantTableMappingValidator.Validate(modelBuilder.Metadata, IsOptedOut);

        // Policy name -> table: each protected table needs its own policy name
        var policies = new Dictionary<string, StoreObjectIdentifier>(StringComparer.OrdinalIgnoreCase);
        var defaultSchema = modelBuilder.Metadata.GetDefaultSchema() ?? "dbo";

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            // Derived types share the root's table (TPH) and owned types the owner's (validated
            // above): one policy per table, attached to the root
            if (entityType.BaseType is not null || entityType.IsOwned())
                continue;

            if (entityType.FindTenantPropertyCandidate() is not IConventionProperty property)
                continue;

            if (property.ClrType != typeof(TenantId))
                throw new InvalidOperationException(
                    $"Tenant property '{entityType.DisplayName()}.{property.Name}' must be of type TenantId.");

            // Opt-out via data annotation (only the PostgreSQL package offers the Fluent API variant)
            if (IsOptedOut(entityType))
            {
                _logger.TenantIsolationOptedOut(_loggingDefinitions, entityType);
                continue;
            }

            // Views, queries and keyless types without a table get no policy
            if (StoreObjectIdentifier.Create(entityType, StoreObjectType.Table) is not { } table)
                continue;

            // Runtime annotation (core) and design-time annotation (migrations) are separate:
            // the column name is only needed for the generated DDL
            entityType.Builder.HasAnnotation(TenancyAnnotationNames.TenantProperty, property.Name);
            entityType.Builder.HasAnnotation(
                SqlServerRlsAnnotationNames.TenantColumn, property.GetColumnName(table));
            EnsureUniquePolicyName(policies, table, defaultSchema);

            // Every query filters by the tenant column via the predicate: index it unless an
            // existing index or key already starts with it
            if (!entityType.GetIndexes().Any(i => i.Properties[0] == property)
                && !entityType.GetKeys().Any(k => k.Properties[0] == property))
            {
                entityType.Builder.HasIndex(new[] { property });
            }

            _logger.TenantIsolationApplied(_loggingDefinitions, entityType);
        }
    }

    // SQL Server compares names case-insensitively by default; a collision would only show up as
    // a failing CREATE SECURITY POLICY in the database
    private static void EnsureUniquePolicyName(
        Dictionary<string, StoreObjectIdentifier> policies, StoreObjectIdentifier table, string defaultSchema)
    {
        var name = SqlServerTenancyNames.PolicyName(table.Schema ?? defaultSchema, table.Name);
        if (name.Length > SqlServerTenancyNames.MaxIdentifierLength)
            throw new InvalidOperationException(
                $"The security policy name '{name}' for table '{table.DisplayName()}' is longer than "
                + $"{SqlServerTenancyNames.MaxIdentifierLength} characters. Shorten the table or schema name.");

        if (policies.TryGetValue(name, out var other) && other != table)
            throw new InvalidOperationException(
                $"Tables '{other.DisplayName()}' and '{table.DisplayName()}' would get the same security "
                + $"policy name '{name}'. Rename one of them.");

        policies[name] = table;
    }

    private static bool IsOptedOut(IReadOnlyEntityType entityType)
        => entityType.ClrType.IsDefined(typeof(NoTenantIsolationAttribute), inherit: true);
}
