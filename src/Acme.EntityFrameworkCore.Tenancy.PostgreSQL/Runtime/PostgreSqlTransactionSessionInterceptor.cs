using System.Data.Common;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Runtime;

/// <summary>
/// Sets the tenant for one transaction instead of for the session, for PgBouncer in transaction
/// mode: <c>set_config(name, value, true)</c> right after EF began a transaction. The value ends
/// with the transaction, so no other client of the pooled server connection inherits it.
/// </summary>
/// <remarks>
/// Activated by <c>UseTransactionPooling()</c> together with
/// <c>UseAcmeTenancy(applicationServices)</c>, which then registers this interceptor instead of
/// <see cref="TenantConnectionInterceptor"/>. Commands outside a transaction run without a tenant
/// and see no rows (fail closed): reads that need the tenant belong in a transaction
/// (chapter "Row Level Security in production"). As a SaveChanges interceptor it makes EF wrap
/// every SaveChanges in a transaction; by default EF omits it for a single command, which would
/// then run without a tenant.
/// </remarks>
public class PostgreSqlTransactionSessionInterceptor : DbTransactionInterceptor, ISaveChangesInterceptor
{
    private readonly ITenantProvider _tenants;

    /// <summary>Creates the interceptor.</summary>
    /// <param name="tenants">The tenant provider from the application container.</param>
    public PostgreSqlTransactionSessionInterceptor(ITenantProvider tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        _tenants = tenants;
    }

    /// <summary>Sets the tenant for the transaction EF has just begun.</summary>
    /// <param name="connection">The connection of the transaction.</param>
    /// <param name="eventData">Context and transaction information.</param>
    /// <param name="result">The transaction.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
    public override DbTransaction TransactionStarted(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        using var command = CreateSessionCommand(connection, result, eventData.Context);
        command.ExecuteNonQuery();
        return result;
    }

    /// <summary>Sets the tenant for the transaction EF has just begun.</summary>
    /// <param name="connection">The connection of the transaction.</param>
    /// <param name="eventData">Context and transaction information.</param>
    /// <param name="result">The transaction.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await using var command = CreateSessionCommand(connection, result, eventData.Context);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    /// <summary>Makes EF begin a transaction for this SaveChanges, even for a single command.</summary>
    /// <param name="eventData">The context.</param>
    /// <param name="result">The interception result.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
    public virtual InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        RequireTransaction(eventData);
        return result;
    }

    /// <summary>Makes EF begin a transaction for this SaveChanges, even for a single command.</summary>
    /// <param name="eventData">The context.</param>
    /// <param name="result">The interception result.</param>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
    public virtual ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction(eventData);
        return ValueTask.FromResult(result);
    }

    // EF resets the setting when a pooled context is returned
    private static void RequireTransaction(DbContextEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is { } context)
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }

    /// <summary>
    /// <c>SELECT set_config(@name, @value, true)</c> in the transaction: <c>is_local = true</c>
    /// limits the value to the transaction.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="transaction">The transaction the command is enlisted in.</param>
    /// <param name="context">The context, for the configured setting name.</param>
    /// <returns>The command.</returns>
    protected virtual DbCommand CreateSessionCommand(
        DbConnection connection, DbTransaction transaction, DbContext? context)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var settingName = context?.GetService<ITenancySingletonOptions>().Settings.SettingName
                          ?? TenancySettings.Default.SettingName;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT set_config(@name, @value, true)";
        Add(command, "name", settingName);
        Add(command, "value", _tenants.CurrentTenantId?.ToString() ?? string.Empty);
        return command;
    }

    private static void Add(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
