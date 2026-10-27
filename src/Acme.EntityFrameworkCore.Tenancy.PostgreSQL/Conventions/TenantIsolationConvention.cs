using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Acme.EntityFrameworkCore.Tenancy.Diagnostics;
using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Conventions;

/// <summary>
/// Enables for every <see cref="ITenantOwned"/> entity: RLS, the tenant policy,
/// an index on the tenant column and optionally a named query filter.
/// </summary>
/// <remarks>
/// Runs at ModelFinalizing: only then are column names (HasColumnName,
/// naming conventions) and explicit configuration final.
/// </remarks>
public class TenantIsolationConvention : IModelFinalizingConvention
{
    // Resolved once: the query filter is built as an expression tree, not as a lambda
    private static readonly MethodInfo CurrentTenantMethod =
        typeof(TenancyDbFunctionsExtensions).GetRuntimeMethod(
            nameof(TenancyDbFunctionsExtensions.CurrentTenant), [typeof(DbFunctions)])!;

    private static readonly PropertyInfo FunctionsProperty =
        typeof(EF).GetProperty(nameof(EF.Functions))!;

    private readonly TenancySettings _settings;
    private readonly ISqlGenerationHelper _sqlGenerationHelper;
    private readonly IDiagnosticsLogger<DbLoggerCategory.Model> _logger;
    private readonly TenancyLoggingDefinitions _loggingDefinitions;

    /// <summary>Creates the convention.</summary>
    /// <param name="settings">Model-relevant plugin settings.</param>
    /// <param name="sqlGenerationHelper">Delimits the tenant column in the policy SQL.</param>
    /// <param name="logger">Model logger.</param>
    /// <param name="loggingDefinitions">Cached log definitions of the plugin.</param>
    public TenantIsolationConvention(
        TenancySettings settings,
        ISqlGenerationHelper sqlGenerationHelper,
        IDiagnosticsLogger<DbLoggerCategory.Model> logger,
        TenancyLoggingDefinitions loggingDefinitions)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sqlGenerationHelper);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(loggingDefinitions);

        _settings = settings;
        _sqlGenerationHelper = sqlGenerationHelper;
        _logger = logger;
        _loggingDefinitions = loggingDefinitions;
    }

    /// <summary>Applies tenant isolation to every eligible root entity type.</summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="context">Convention context.</param>
    public virtual void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.BaseType is not null       // root types of a hierarchy only
                || entityType.IsOwned())              // owned types share the owner's table
            {
                continue;
            }

            // Tenant property: marked via IsTenantProperty() or "TenantId" with ITenantOwned
            var implementsTenantOwned = typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType);
            var candidate = (IConventionProperty?)entityType.FindTenantPropertyCandidate();
            if (candidate is null)
            {
                if (implementsTenantOwned)
                    throw new InvalidOperationException(
                        $"'{entityType.DisplayName()}' implements {nameof(ITenantOwned)}, but the "
                        + $"property '{nameof(ITenantOwned.TenantId)}' is not mapped. "
                        + "With an explicit interface implementation, mark the mapped property "
                        + "with IsTenantProperty().");
                continue;
            }

            if (candidate.ClrType != typeof(TenantId))
                throw new InvalidOperationException(
                    $"Tenant property '{entityType.DisplayName()}.{candidate.Name}' must be of "
                    + $"type {nameof(TenantId)}.");

            if (entityType.IsRlsOptedOut())
            {
                _logger.TenantIsolationOptedOut(_loggingDefinitions, entityType);
                continue;
            }

            // No table (view, ToSqlQuery, keyless without a table) -> no RLS
            if (StoreObjectIdentifier.Create(entityType, StoreObjectType.Table) is not { } table)
                continue;

            // Runtime annotation: this is how the SaveChanges interceptor finds the property
            entityType.Builder.HasAnnotation(TenancyAnnotationNames.TenantProperty, candidate.Name);

            ApplyRowLevelSecurity(entityType, candidate, table);

            if (_settings.CreateTenantIndex)
                EnsureTenantIndex(entityType, candidate);

            if (_settings.ApplyQueryFilter)
                ApplyQueryFilter(entityType, candidate);

            _logger.TenantIsolationApplied(_loggingDefinitions, entityType);
        }
    }

    /// <summary>Enables RLS (and FORCE, if configured) and adds the tenant policy.</summary>
    /// <param name="entityType">The root entity type.</param>
    /// <param name="property">The tenant property.</param>
    /// <param name="table">The table the entity type is mapped to.</param>
    protected virtual void ApplyRowLevelSecurity(
        IConventionEntityType entityType, IConventionProperty property, StoreObjectIdentifier table)
    {
        // Column name for exactly this table (may differ with table splitting)
        var column = _sqlGenerationHelper.DelimitIdentifier(property.GetColumnName(table)!);

        // SettingName is validated ([a-z0-9_.]) and can be embedded safely.
        // current_setting(..., true) returns NULL instead of failing if the setting is missing;
        // nullif() turns the '' of a reset connection into NULL, so no row matches (fail closed).
        var condition =
            $"{column} = nullif(current_setting('{_settings.SettingName}', true), '')::uuid";

        var policy = new RlsPolicy(
            _settings.PolicyName, PolicyCommand.All, Permissive: true,
            Roles: null, Using: condition, WithCheck: condition);

        // Convention builder: ConfigurationSource.Convention. Explicit Fluent API
        // configuration (Explicit) is NOT overwritten; the call then returns null.
        var builder = entityType.Builder;
        builder.HasAnnotation(RlsAnnotationNames.Enabled, true);

        if (_settings.ForceRowLevelSecurity)
            builder.HasAnnotation(RlsAnnotationNames.Forced, true);

        builder.HasAnnotation(RlsAnnotationNames.PolicyPrefix + _settings.PolicyName, policy.Serialize());
    }

    /// <summary>Adds an index on the tenant column unless an index or key already starts with it.</summary>
    /// <param name="entityType">The root entity type.</param>
    /// <param name="property">The tenant property.</param>
    protected virtual void EnsureTenantIndex(IConventionEntityType entityType, IConventionProperty property)
    {
        // An existing index/key that starts with the tenant column is sufficient:
        // PostgreSQL can use its leading column for the policy predicate
        var covered = entityType.GetIndexes().Any(i => i.Properties[0] == property)
                      || entityType.GetKeys().Any(k => k.Properties[0] == property);

        if (!covered)
            entityType.Builder.HasIndex(new[] { property });
    }

    /// <summary>Adds the named query filter comparing the tenant property with the current tenant.</summary>
    /// <param name="entityType">The root entity type.</param>
    /// <param name="property">The tenant property.</param>
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Conventions only run while the model is built. NativeAOT requires a "
                        + "compiled model, in which conventions do not run (chapter "
                        + "\"Compiled models, trimming and NativeAOT\").")]
    protected virtual void ApplyQueryFilter(IConventionEntityType entityType, IConventionProperty property)
    {
        // e => EF.Property<TenantId>(e, "TenantId") == EF.Functions.CurrentTenant()
        // EF.Property works for CLR and shadow properties with any name.
        var parameter = Expression.Parameter(entityType.ClrType, "e");
        var body = Expression.Equal(
            Expression.Call(
                typeof(EF), nameof(EF.Property), [typeof(TenantId)],
                parameter, Expression.Constant(property.Name)),
            Expression.Call(CurrentTenantMethod, Expression.Property(null, FunctionsProperty)));

        // Named filter (EF 10): coexists with the user's own filters and can be
        // switched off selectively via IgnoreQueryFilters(["Acme.Tenancy"])
        entityType.Builder.HasQueryFilter(
            RlsAnnotationNames.QueryFilterKey, Expression.Lambda(body, parameter));
    }
}
