using Acme.EntityFrameworkCore.Tenancy;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Runtime;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Entry point: <c>options.UseNpgsql(...).UseAcmeTenancy(...)</c>.</summary>
public static class TenancyDbContextOptionsBuilderExtensions
{
    /// <summary>Enables the plugin (model, queries, migrations). Without interceptors.</summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="configure">Optional configuration of the plugin settings.</param>
    /// <returns>The same options builder for chaining.</returns>
    public static DbContextOptionsBuilder UseAcmeTenancy(
        this DbContextOptionsBuilder optionsBuilder,
        Action<TenancyOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // Reuse an existing extension so repeated calls keep earlier settings
        var extension = optionsBuilder.Options.FindExtension<TenancyOptionsExtension>()
                        ?? new TenancyOptionsExtension();

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        configure?.Invoke(new TenancyOptionsBuilder(optionsBuilder));
        return optionsBuilder;
    }

    /// <summary>
    /// Like the overload above, additionally registers the runtime interceptors. They are created
    /// from the <see cref="ITenantProvider"/> of the application container (see AddAcmeTenancy).
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="applicationServices">The application's service provider.</param>
    /// <param name="configure">Optional configuration of the plugin settings.</param>
    /// <returns>The same options builder for chaining.</returns>
    public static DbContextOptionsBuilder UseAcmeTenancy(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider applicationServices,
        Action<TenancyOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(applicationServices);

        // The interceptors live in the context options, not in EF's internal provider:
        // that is the bridge to the application container that knows the current tenant
        var tenants = applicationServices.GetRequiredService<ITenantProvider>();

        return optionsBuilder
            .UseAcmeTenancy(configure)
            .AddInterceptors(
                new TenantConnectionInterceptor(tenants),
                new TenantSaveChangesInterceptor(tenants));
    }

    /// <inheritdoc cref="UseAcmeTenancy(DbContextOptionsBuilder, Action{TenancyOptionsBuilder})"/>
    /// <typeparam name="TContext">The context type; keeps method chaining typed.</typeparam>
    public static DbContextOptionsBuilder<TContext> UseAcmeTenancy<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        Action<TenancyOptionsBuilder>? configure = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseAcmeTenancy(
            (DbContextOptionsBuilder)optionsBuilder, configure);

    /// <summary>Generic variant: enables the plugin and registers the runtime interceptors.</summary>
    /// <typeparam name="TContext">The context type; keeps method chaining typed.</typeparam>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="applicationServices">The application's service provider.</param>
    /// <param name="configure">Optional configuration of the plugin settings.</param>
    /// <returns>The same options builder for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> UseAcmeTenancy<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        IServiceProvider applicationServices,
        Action<TenancyOptionsBuilder>? configure = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseAcmeTenancy(
            (DbContextOptionsBuilder)optionsBuilder, applicationServices, configure);
}
