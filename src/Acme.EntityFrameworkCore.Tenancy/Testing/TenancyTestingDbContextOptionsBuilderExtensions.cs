using Acme.EntityFrameworkCore.Tenancy;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.Testing;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Test mode for application tests on SQLite or InMemory:
/// <c>options.UseSqlite(connection).UseAcmeTenancyForTesting(sp)</c>, together with
/// <c>modelBuilder.HasTenantQueryFilters(this)</c> in <c>OnModelCreating</c>.
/// </summary>
/// <remarks>
/// Gives business logic tests the application-side behavior: TenantId mapped as a Guid, the
/// tenant set and checked on saving, and queries filtered to the current tenant. There is no
/// row level security, no migration SQL and no session: the plugin itself is tested against
/// the real databases (chapter "Test strategy").
/// </remarks>
public static class TenancyTestingDbContextOptionsBuilderExtensions
{
    /// <summary>Activates the test mode with the tenant provider from the application container.</summary>
    /// <param name="optionsBuilder">The context's options builder.</param>
    /// <param name="applicationServices">The application's (or the test's) service provider.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static DbContextOptionsBuilder UseAcmeTenancyForTesting(
        this DbContextOptionsBuilder optionsBuilder, IServiceProvider applicationServices)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(applicationServices);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            optionsBuilder.Options.FindExtension<TenancyTestingOptionsExtension>()
            ?? new TenancyTestingOptionsExtension());

        var tenants = applicationServices.GetRequiredService<ITenantProvider>();
        return optionsBuilder.AddInterceptors(new TenantSaveChangesInterceptor(tenants));
    }

    /// <inheritdoc cref="UseAcmeTenancyForTesting(DbContextOptionsBuilder, IServiceProvider)"/>
    /// <typeparam name="TContext">The context type.</typeparam>
    public static DbContextOptionsBuilder<TContext> UseAcmeTenancyForTesting<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder, IServiceProvider applicationServices)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseAcmeTenancyForTesting(
            (DbContextOptionsBuilder)optionsBuilder, applicationServices);
}
