using System.Diagnostics.CodeAnalysis;
using Acme.EntityFrameworkCore.Tenancy;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration in the application container, shared by all provider packages.</summary>
public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tenant provider. The Use methods of the provider packages create the
    /// interceptors themselves from this provider.
    /// </summary>
    /// <typeparam name="TTenantProvider">The application's <see cref="ITenantProvider"/>.</typeparam>
    /// <param name="services">The application container.</param>
    /// <param name="lifetime">
    /// Scoped (default) for AddDbContext. Singleton for AddDbContextPool; the provider must then
    /// determine the tenant anew on every access (e.g. via IHttpContextAccessor).
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddAcmeTenancy<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTenantProvider>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TTenantProvider : class, ITenantProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd: a provider registered earlier by the application is not overwritten
        services.TryAdd(ServiceDescriptor.Describe(typeof(ITenantProvider), typeof(TTenantProvider), lifetime));
        return services;
    }
}
