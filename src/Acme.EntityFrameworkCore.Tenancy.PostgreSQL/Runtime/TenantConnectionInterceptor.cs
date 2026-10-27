using System.Data.Common;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Runtime;

/// <summary>PostgreSQL: sets the session setting with set_config(...).</summary>
/// <remarks>
/// The interceptor reads the setting name from the singleton options of EF's internal
/// provider and the tenant from the application container (bridge between both containers).
/// </remarks>
public class TenantConnectionInterceptor : TenantSessionInterceptor
{
    /// <summary>Creates the interceptor.</summary>
    /// <param name="tenants">Supplies the current tenant from the application container.</param>
    public TenantConnectionInterceptor(ITenantProvider tenants)
        : base(tenants)
    {
    }

    /// <summary>Builds <c>SELECT set_config(@name, @value, false)</c> for the opened connection.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="context">The context, if known; provides the settings.</param>
    /// <param name="tenant">The current tenant, or <see langword="null"/> if there is none.</param>
    /// <returns>The command to execute on the connection.</returns>
    protected override DbCommand CreateSessionCommand(
        DbConnection connection, DbContext? context, TenantId? tenant)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var settings = context?.GetService<ITenancySingletonOptions>().Settings ?? TenancySettings.Default;
        var settingName = settings.SettingName;

        // A session value would outlive the transaction and reach other clients of the pooled
        // server connection: with transaction pooling, refuse instead of leaking
        if (settings.UseTransactionPooling)
            throw new TenancyException(
                "UseTransactionPooling() sets the tenant per transaction; remove the "
                + "TenantConnectionInterceptor and use UseAcmeTenancy(applicationServices), which "
                + "registers PostgreSqlTransactionSessionInterceptor instead.");

        // set_config() instead of SET: the name and value can be passed as parameters (no SQL
        // injection). is_local = false: valid for the session, not just the current transaction.
        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config(@name, @value, false)";
        AddParameter(command, "name", settingName);

        // Always set explicitly (even empty) so that no stale value remains, e.g. on a pooled
        // connection. The policy and EF.Functions.CurrentTenant() turn '' into NULL via nullif().
        AddParameter(command, "value", tenant?.ToString() ?? string.Empty);
        return command;
    }
}
