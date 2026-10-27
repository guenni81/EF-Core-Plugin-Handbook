using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;

/// <summary>Fluent builder for the plugin options inside <c>UseAcmeTenancy(...)</c>.</summary>
public class TenancyOptionsBuilder
{
    /// <summary>Creates the builder.</summary>
    /// <param name="optionsBuilder">The options builder the settings are written to.</param>
    public TenancyOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        OptionsBuilder = optionsBuilder;
    }

    /// <summary>The options builder the settings are written to.</summary>
    protected virtual DbContextOptionsBuilder OptionsBuilder { get; }

    /// <summary>Session setting holding the tenant ID (default: app.tenant_id).</summary>
    /// <param name="settingName">Name in the form 'prefix.name'.</param>
    /// <returns>The same builder for chaining.</returns>
    public virtual TenancyOptionsBuilder UseSettingName(string settingName)
        => With(s => s with { SettingName = TenancyIdentifiers.ValidateSettingName(settingName) });

    /// <summary>Name of the automatically generated policy (default: tenant_isolation).</summary>
    /// <param name="policyName">A simple lowercase PostgreSQL identifier.</param>
    /// <returns>The same builder for chaining.</returns>
    public virtual TenancyOptionsBuilder UsePolicyName(string policyName)
        => With(s => s with { PolicyName = TenancyIdentifiers.ValidateIdentifier(policyName, "policy name") });

    /// <summary>FORCE ROW LEVEL SECURITY (default: on).</summary>
    /// <param name="force">Whether policies also apply to the table owner.</param>
    /// <returns>The same builder for chaining.</returns>
    public virtual TenancyOptionsBuilder ForceRowLevelSecurity(bool force = true)
        => With(s => s with { ForceRowLevelSecurity = force });

    /// <summary>Additional named query filter "Acme.Tenancy" (default: off).</summary>
    /// <param name="apply">Whether to add the filter.</param>
    /// <returns>The same builder for chaining.</returns>
    public virtual TenancyOptionsBuilder ApplyQueryFilter(bool apply = true)
        => With(s => s with { ApplyQueryFilter = apply });

    /// <summary>Index on the tenant column (default: on).</summary>
    /// <param name="create">Whether to create the index.</param>
    /// <returns>The same builder for chaining.</returns>
    public virtual TenancyOptionsBuilder CreateTenantIndex(bool create = true)
        => With(s => s with { CreateTenantIndex = create });

    /// <summary>
    /// Throws when a query materializes an entity of another tenant (default: off). Detects
    /// situations in which row level security does not take effect, such as a connection opened
    /// outside of EF. Requires <c>UseAcmeTenancy(applicationServices)</c>.
    /// </summary>
    /// <param name="verify">Whether to check materialized entities.</param>
    /// <returns>The same builder for chaining.</returns>
    public virtual TenancyOptionsBuilder VerifyMaterializedTenants(bool verify = true)
        => With(s => s with { VerifyMaterializedTenants = verify });

    /// <summary>
    /// Rejects <c>ExecuteUpdate</c> calls that set the tenant property, when the query is
    /// compiled (default: off). Bulk operations bypass the SaveChanges interceptor; row level
    /// security still limits which rows they reach.
    /// </summary>
    /// <param name="protect">Whether to check bulk updates.</param>
    /// <returns>The same builder for chaining.</returns>
    public virtual TenancyOptionsBuilder ProtectBulkOperations(bool protect = true)
        => With(s => s with { ProtectBulkOperations = protect });

    /// <summary>
    /// Sets the tenant per transaction instead of per session, for PgBouncer in transaction
    /// mode (default: off). <c>UseAcmeTenancy(applicationServices)</c> then registers
    /// <see cref="Runtime.PostgreSqlTransactionSessionInterceptor"/> instead of the connection
    /// interceptor, and the connection interceptor refuses to set the session value. Reads
    /// outside a transaction see no rows.
    /// </summary>
    /// <param name="enabled">Whether to set the tenant per transaction.</param>
    /// <returns>The same builder for chaining.</returns>
    public virtual TenancyOptionsBuilder UseTransactionPooling(bool enabled = true)
        => With(s => s with { UseTransactionPooling = enabled });

    /// <summary>
    /// Applies a change to the settings. Options extensions are immutable, so a modified copy
    /// replaces the registered extension.
    /// </summary>
    /// <param name="change">Produces the new settings from the current ones.</param>
    /// <returns>The same builder for chaining.</returns>
    protected virtual TenancyOptionsBuilder With(Func<TenancySettings, TenancySettings> change)
    {
        var extension = OptionsBuilder.Options.FindExtension<TenancyOptionsExtension>()
                        ?? new TenancyOptionsExtension();

        ((IDbContextOptionsBuilderInfrastructure)OptionsBuilder)
            .AddOrUpdateExtension(extension.WithSettings(change(extension.Settings)));
        return this;
    }
}
