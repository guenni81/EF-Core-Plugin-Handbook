using Acme.EntityFrameworkCore.Tenancy;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Runtime;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Activation through the provider's options builder:
/// <c>options.UseSqlServer(cs, sql =&gt; sql.UseAcmeTenancy())</c>.
/// </summary>
/// <remarks>
/// This style (like UseNetTopologySuite) visibly binds the plugin to the provider and avoids
/// name collisions with other provider packages.
/// </remarks>
public static class SqlServerTenancyOptionsBuilderExtensions
{
    /// <summary>
    /// Registers the tenancy plugin (type mapping, translator, conventions, migrations SQL)
    /// without runtime interceptors; add those yourself or use the overload taking
    /// <see cref="IServiceProvider" />.
    /// </summary>
    /// <param name="sqlServerOptionsBuilder">The builder passed to <c>UseSqlServer</c>.</param>
    /// <param name="sessionKey">The SESSION_CONTEXT key that holds the tenant ID.</param>
    /// <param name="policySchema">The schema for the predicate function and security policies.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentException">A value is not a plain identifier.</exception>
    public static SqlServerDbContextOptionsBuilder UseAcmeTenancy(
        this SqlServerDbContextOptionsBuilder sqlServerOptionsBuilder,
        string sessionKey = "TenantId",
        string policySchema = "acme")
    {
        ArgumentNullException.ThrowIfNull(sqlServerOptionsBuilder);

        // From the provider builder to the actual DbContextOptionsBuilder
        var optionsBuilder = ((IRelationalDbContextOptionsBuilderInfrastructure)sqlServerOptionsBuilder)
            .OptionsBuilder;

        var extension = optionsBuilder.Options.FindExtension<SqlServerTenancyOptionsExtension>()
                        ?? new SqlServerTenancyOptionsExtension();

        // Both values end up in generated SQL, so they are validated here, at configuration time
        var settings = extension.Settings with
        {
            SessionKey = SqlServerTenancySettings.Validate(sessionKey, "session key"),
            PolicySchema = SqlServerTenancySettings.Validate(policySchema, "schema name"),
        };

        // Extensions are immutable: replace it with a modified copy instead of mutating it
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(extension.WithSettings(settings));

        return sqlServerOptionsBuilder;
    }

    /// <summary>
    /// Like <see cref="UseAcmeTenancy(SqlServerDbContextOptionsBuilder, string, string)" />, and
    /// additionally adds the runtime interceptors with the tenant provider from the app container.
    /// </summary>
    /// <param name="sqlServerOptionsBuilder">The builder passed to <c>UseSqlServer</c>.</param>
    /// <param name="applicationServices">The application's service provider.</param>
    /// <param name="sessionKey">The SESSION_CONTEXT key that holds the tenant ID.</param>
    /// <param name="policySchema">The schema for the predicate function and security policies.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static SqlServerDbContextOptionsBuilder UseAcmeTenancy(
        this SqlServerDbContextOptionsBuilder sqlServerOptionsBuilder,
        IServiceProvider applicationServices,
        string sessionKey = "TenantId",
        string policySchema = "acme")
    {
        ArgumentNullException.ThrowIfNull(applicationServices);

        sqlServerOptionsBuilder.UseAcmeTenancy(sessionKey, policySchema);

        // Interceptors are not part of EF's internal service provider, so the tenant provider is
        // resolved from the application container (registered by AddAcmeTenancy)
        var tenants = applicationServices.GetRequiredService<ITenantProvider>();
        ((IRelationalDbContextOptionsBuilderInfrastructure)sqlServerOptionsBuilder).OptionsBuilder
            .AddInterceptors(
                new SqlServerTenantSessionInterceptor(tenants),
                new TenantSaveChangesInterceptor(tenants));

        return sqlServerOptionsBuilder;
    }

    /// <summary>
    /// Throws when a query materializes an entity of another tenant (default: off). Detects
    /// situations in which the security policies do not take effect. Requires the overload of
    /// <c>UseAcmeTenancy</c> that takes the application's service provider.
    /// </summary>
    /// <param name="sqlServerOptionsBuilder">The builder passed to <c>UseSqlServer</c>.</param>
    /// <param name="verify">Whether to check materialized entities.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static SqlServerDbContextOptionsBuilder VerifyMaterializedTenants(
        this SqlServerDbContextOptionsBuilder sqlServerOptionsBuilder, bool verify = true)
    {
        ArgumentNullException.ThrowIfNull(sqlServerOptionsBuilder);

        var optionsBuilder = ((IRelationalDbContextOptionsBuilderInfrastructure)sqlServerOptionsBuilder)
            .OptionsBuilder;
        var extension = optionsBuilder.Options.FindExtension<SqlServerTenancyOptionsExtension>()
                        ?? new SqlServerTenancyOptionsExtension();

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            extension.WithSettings(extension.Settings with { VerifyMaterializedTenants = verify }));

        return sqlServerOptionsBuilder;
    }
}
