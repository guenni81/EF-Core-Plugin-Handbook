using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.Interceptors;

/// <summary>
/// Optional: tags the current <see cref="Activity"/> with the tenant (<c>tenant.id</c>) before
/// every command, so that traces show which tenant ran a slow or failing query (chapter
/// "Diagnostics and logging").
/// </summary>
/// <remarks>
/// EF Core starts no activities itself. With the OpenTelemetry instrumentation for EF Core, the
/// activity of the command already exists when the interceptor runs; with only the database
/// driver's tracing, the driver starts its activity later, and the tag lands on the enclosing
/// one (typically the request). Costs nothing when no activity is recorded. Registered in EF's
/// internal service provider as an <c>IInterceptor</c>, which EF resolves per context (scoped);
/// the tenant comes from the context of each command.
/// </remarks>
public class TenantActivityEnrichmentInterceptor : DbCommandInterceptor
{
    /// <summary>The tag name, following the OpenTelemetry naming style.</summary>
    public const string TagName = "tenant.id";

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Enrich(eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Enrich(eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Enrich(eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Enrich(eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Enrich(eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Enrich(eventData);
        return ValueTask.FromResult(result);
    }

    /// <summary>Sets the tag if an activity is recorded and the context has a tenant.</summary>
    /// <param name="eventData">The command's event data, with the context.</param>
    protected virtual void Enrich(CommandEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // The cheap checks first: without a recorded activity there is nothing to do
        if (Activity.Current is not { IsAllDataRequested: true } activity
            || eventData.Context is not { } context)
            return;

        // The tenant provider of this context travels with its SaveChanges interceptor
        var tenant = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?.Interceptors?
            .OfType<TenantSaveChangesInterceptor>().FirstOrDefault()?.Tenants.CurrentTenantId;

        // Without a tenant no tag: an empty or placeholder value would be misleading
        if (tenant is { } id)
            activity.SetTag(TagName, id.Value.ToString("D"));
    }
}
