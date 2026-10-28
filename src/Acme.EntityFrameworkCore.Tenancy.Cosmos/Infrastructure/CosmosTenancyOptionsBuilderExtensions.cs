using Acme.EntityFrameworkCore.Tenancy;
using Acme.EntityFrameworkCore.Tenancy.Cosmos.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Activation through the provider's options builder:
/// <c>options.UseCosmos(cs, db, cosmos =&gt; cosmos.UseAcmeTenancy(sp))</c>.
/// </summary>
public static class CosmosTenancyOptionsBuilderExtensions
{
    /// <summary>
    /// Registers the tenancy plugin for Cosmos DB: tenant as partition key, type mapping, and the
    /// SaveChanges interceptor with the tenant provider from the application container.
    /// </summary>
    /// <param name="cosmosOptionsBuilder">The builder passed to <c>UseCosmos</c>.</param>
    /// <param name="applicationServices">The application's service provider.</param>
    /// <param name="verifyMaterializedTenants">
    /// Also check every loaded tenant-owned entity against the current tenant.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    public static CosmosDbContextOptionsBuilder UseAcmeTenancy(
        this CosmosDbContextOptionsBuilder cosmosOptionsBuilder,
        IServiceProvider applicationServices,
        bool verifyMaterializedTenants = false)
    {
        ArgumentNullException.ThrowIfNull(cosmosOptionsBuilder);
        ArgumentNullException.ThrowIfNull(applicationServices);

        var optionsBuilder = ((ICosmosDbContextOptionsBuilderInfrastructure)cosmosOptionsBuilder)
            .OptionsBuilder;

        var extension = optionsBuilder.Options.FindExtension<CosmosTenancyOptionsExtension>()
                        ?? new CosmosTenancyOptionsExtension();
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            extension.WithSettings(extension.Settings with
            {
                VerifyMaterializedTenants = verifyMaterializedTenants,
            }));

        // The same SaveChanges interceptor as for PostgreSQL and SQL Server; it also carries the
        // tenant provider that the query filter and the materialization check read
        var tenants = applicationServices.GetRequiredService<ITenantProvider>();
        optionsBuilder.AddInterceptors(new TenantSaveChangesInterceptor(tenants));

        return cosmosOptionsBuilder;
    }
}
