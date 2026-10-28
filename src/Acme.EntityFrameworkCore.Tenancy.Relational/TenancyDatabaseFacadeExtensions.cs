using Acme.EntityFrameworkCore.Tenancy;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Explicitly writes the current tenant into the database session, for the cases in which
/// the session interceptor does not run because EF does not open the connection itself.
/// </summary>
/// <remarks>
/// Lives in <c>Microsoft.EntityFrameworkCore</c> so that <c>db.Database.ApplyTenantSession()</c>
/// is available without an additional using, like EF's own <c>Database</c> methods.
/// </remarks>
public static class TenancyDatabaseFacadeExtensions
{
    /// <summary>
    /// Writes the current tenant into the session if the connection is open: after handing an
    /// already open connection to EF, or after the tenant changed while the connection stays
    /// open. Does nothing for a closed connection; EF's interceptor sets the tenant on opening.
    /// </summary>
    /// <param name="database">The context's <see cref="DbContext.Database"/>.</param>
    /// <returns><see langword="true"/> if the session was updated.</returns>
    /// <exception cref="TenancyException">The context has no tenant session interceptor.</exception>
    public static bool ApplyTenantSession(this DatabaseFacade database)
    {
        var (context, interceptor) = Resolve(database);
        return interceptor.ApplyToOpenConnection(context);
    }

    /// <inheritdoc cref="ApplyTenantSession"/>
    /// <param name="database">The context's <see cref="DbContext.Database"/>.</param>
    /// <param name="cancellationToken">Cancels the database call.</param>
    public static Task<bool> ApplyTenantSessionAsync(
        this DatabaseFacade database, CancellationToken cancellationToken = default)
    {
        var (context, interceptor) = Resolve(database);
        return interceptor.ApplyToOpenConnectionAsync(context, cancellationToken);
    }

    private static (DbContext Context, TenantSessionInterceptor Interceptor) Resolve(
        DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);

        // The facade exposes the context's services; ICurrentDbContext leads back to the context
        var context = database.GetService<ICurrentDbContext>().Context;

        // The interceptors added with AddInterceptors live in the core options extension
        var interceptor = context.GetService<IDbContextOptions>()
                              .FindExtension<CoreOptionsExtension>()?.Interceptors?
                              .OfType<TenantSessionInterceptor>().FirstOrDefault()
                          ?? throw new TenancyException(
                              "No tenant session interceptor is registered for this context. "
                              + "Activate the plugin with UseAcmeTenancy(applicationServices).");

        return (context, interceptor);
    }
}
