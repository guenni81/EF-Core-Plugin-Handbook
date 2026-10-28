using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.Interceptors;

/// <summary>
/// Provider-independent base: transfers the current tenant into the database session every time
/// the connection is opened. Provider packages only supply the SQL command
/// (PostgreSQL: set_config, SQL Server: sp_set_session_context).
/// </summary>
/// <remarks>
/// By default EF opens and closes the connection for every operation, and the connection pool
/// resets the session state when a connection is returned. The value is therefore written on
/// every opening, never only once per physical connection.
/// <para>
/// The interceptor only runs when EF opens the connection. For a connection that is already
/// open when it is handed to EF, or that stays open while the tenant changes, call
/// <see cref="ApplyToOpenConnection"/> (or <c>Database.ApplyTenantSession()</c>) explicitly.
/// </para>
/// </remarks>
public abstract class TenantSessionInterceptor : DbConnectionInterceptor
{
    /// <summary>Creates the interceptor.</summary>
    /// <param name="tenants">The tenant provider from the application container.</param>
    protected TenantSessionInterceptor(ITenantProvider tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        Tenants = tenants;
    }

    /// <summary>The tenant provider that supplies the tenant for each opening.</summary>
    protected ITenantProvider Tenants { get; }

    /// <summary>Writes the current tenant into the session after a synchronous open.</summary>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateSessionCommand(connection, eventData.Context, Tenants.CurrentTenantId);
        command.ExecuteNonQuery();
    }

    /// <summary>Writes the current tenant into the session after an asynchronous open.</summary>
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateSessionCommand(connection, eventData.Context, Tenants.CurrentTenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Writes the current tenant into the session of the context's connection if that
    /// connection is open. Needed where EF does not open the connection itself: a connection
    /// that was already open when it was handed to EF, or one that stays open while the tenant
    /// changes. A closed connection is left alone; the interceptor sets the tenant when EF
    /// opens it.
    /// </summary>
    /// <param name="context">The context whose connection is updated.</param>
    /// <returns><see langword="true"/> if the session was updated.</returns>
    public virtual bool ApplyToOpenConnection(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            return false;

        using var command =
            CreateSessionCommand(connection, context, Tenants.CurrentTenantId);
        // Inside an explicit transaction the command must be enlisted in it
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.ExecuteNonQuery();
        return true;
    }

    /// <inheritdoc cref="ApplyToOpenConnection"/>
    /// <param name="context">The context whose connection is updated.</param>
    /// <param name="cancellationToken">Cancels the database call.</param>
    public virtual async Task<bool> ApplyToOpenConnectionAsync(
        DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            return false;

        await using var command =
            CreateSessionCommand(connection, context, Tenants.CurrentTenantId);
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Creates the command that sets the tenant. Also called without a tenant (null): the value
    /// must then be cleared explicitly so that no stale value remains.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="context">
    /// The context, if known; gives access to the internal service provider (options).
    /// </param>
    /// <param name="tenant">The current tenant, or null if there is no tenant context.</param>
    /// <returns>A command on <paramref name="connection"/>; the caller disposes it.</returns>
    protected abstract DbCommand CreateSessionCommand(
        DbConnection connection, DbContext? context, TenantId? tenant);

    /// <summary>
    /// Adds a provider-neutral parameter. Values are always passed as parameters, never
    /// concatenated into the SQL, so tenant values cannot inject SQL.
    /// </summary>
    /// <param name="command">The command to add the parameter to.</param>
    /// <param name="name">The parameter name, without prefix.</param>
    /// <param name="value">The value; null becomes <see cref="DBNull.Value"/>.</param>
    protected static void AddParameter(DbCommand command, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(command);

        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
