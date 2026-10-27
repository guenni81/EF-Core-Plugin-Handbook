using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Acme.EntityFrameworkCore.Tenancy.Interceptors;

/// <summary>
/// Provider-independent, optional: checks every tenant-owned entity a query materializes and
/// throws if it belongs to another tenant than the context. Row level security makes this
/// impossible as long as it is in effect; the check detects the situations in which it is not
/// (a connection opened outside of EF with a stale session, a role that bypasses the
/// policies) instead of silently returning another tenant's data.
/// </summary>
/// <remarks>
/// A materialization interceptor is a singleton interceptor: EF builds it into the compiled
/// shaper of every query, which is cached per internal service provider. One instance
/// therefore serves all contexts and threads. It must not keep per-request state; the
/// tenant is looked up from the context of each call (chapter "Materialization: from rows to
/// objects"). The provider packages register it in the internal service provider when the
/// option is enabled; creating an instance per context instead would create an internal
/// service provider per context.
/// <para>
/// Only entities pass through here. Projections of individual columns, entities that are
/// already tracked and bulk operations are not materialized and therefore not checked.
/// </para>
/// </remarks>
public class TenantMaterializationInterceptor : IMaterializationInterceptor
{
    /// <summary>
    /// Checks the tenant before the instance is created. The value is read from the query
    /// result, so a foreign row never becomes an object, and shadow properties work as well.
    /// </summary>
    /// <exception cref="TenancyException">The row belongs to another tenant.</exception>
    public virtual InterceptionResult<object> CreatingInstance(
        MaterializationInterceptionData materializationData,
        InterceptionResult<object> result)
    {
        Verify(materializationData.Context, materializationData.EntityType,
            materializationData.GetPropertyValue);

        return result;
    }

    // Internal so that unit tests can call it via InternalsVisibleTo without a database
    internal static void Verify(
        DbContext context, IEntityType entityType, Func<string, object?> readValue)
    {
        // Runtime annotation of the isolation convention; without it, no isolation
        if (entityType.GetTenantPropertyName() is not { } propertyName)
            return;

        // Looked up for every row instead of being remembered: this instance is shared by all
        // contexts, and every context can have a different tenant
        var current = FindTenantProvider(context).CurrentTenantId;
        var value = (TenantId)readValue(propertyName)!;

        if (value != current)
            throw new TenancyException(
                $"A query returned a row of entity type '{entityType.DisplayName()}' that does "
                + "not belong to the current tenant. Row level security did not take effect: "
                + "check that the application connects with a restricted role and that the "
                + "tenant session is set (Database.ApplyTenantSession() for connections opened "
                + "outside of EF).");
    }

    private static ITenantProvider FindTenantProvider(DbContext context)
        // The interceptors added with AddInterceptors live in the core options extension;
        // the SaveChanges interceptor carries the tenant provider of this context
        => context.GetService<IDbContextOptions>()
               .FindExtension<CoreOptionsExtension>()?.Interceptors?
               .OfType<TenantSaveChangesInterceptor>().FirstOrDefault()?.Tenants
           ?? throw new TenancyException(
               "Verifying materialized tenants requires the tenant provider of the context. "
               + "Activate the plugin with UseAcmeTenancy(applicationServices).");
}
