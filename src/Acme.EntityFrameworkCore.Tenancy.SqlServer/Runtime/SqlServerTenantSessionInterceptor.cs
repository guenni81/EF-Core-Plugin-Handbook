using System.Data.Common;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Runtime;

/// <summary>SQL Server: sets the tenant with sp_set_session_context.</summary>
/// <remarks>
/// The session context lives as long as the physical connection. The base class runs the
/// command every time EF opens a connection, so a pooled connection never keeps the tenant of
/// a previous request.
/// </remarks>
public class SqlServerTenantSessionInterceptor : TenantSessionInterceptor
{
    /// <summary>Creates the interceptor.</summary>
    /// <param name="tenants">Supplies the current tenant when a connection is opened.</param>
    public SqlServerTenantSessionInterceptor(ITenantProvider tenants)
        : base(tenants)
    {
    }

    /// <summary>Creates the <c>sp_set_session_context</c> call for the given tenant.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="context">The context, if known; gives access to its services.</param>
    /// <param name="tenant">The current tenant, or <see langword="null" /> if there is none.</param>
    /// <returns>The command to execute on the connection.</returns>
    protected override DbCommand CreateSessionCommand(
        DbConnection connection, DbContext? context, TenantId? tenant)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Without a context (e.g. raw connection use) fall back to the default key
        var key = context?.GetService<ISqlServerTenancySingletonOptions>().Settings.SessionKey
                  ?? SqlServerTenancySettings.Default.SessionKey;

        var command = connection.CreateCommand();
        // Parameters instead of literals: the tenant value never becomes part of the SQL text
        command.CommandText = "EXEC sp_set_session_context @key = @key, @value = @value";
        AddParameter(command, "@key", key);
        // NULL without a tenant: the predicate returns no rows
        AddParameter(command, "@value", tenant?.Value);
        return command;
    }
}
