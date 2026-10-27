using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design.Internal;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Scaffolding;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// The plugin's design-time services replace EF's and Npgsql's registrations without changing
/// their lifetimes (chapter "Choosing the right extension point", appendix A).
/// </summary>
public class DesignTimeServicesTests
{
    public static TheoryData<Type, Type> ReplacedServices => new()
    {
        { typeof(IAnnotationCodeGenerator), typeof(TenancyAnnotationCodeGenerator) },
        { typeof(ICSharpRuntimeAnnotationCodeGenerator), typeof(TenancyCSharpRuntimeAnnotationCodeGenerator) },
        { typeof(IDatabaseModelFactory), typeof(TenancyDatabaseModelFactory) },
    };

    [Theory]
    [MemberData(nameof(ReplacedServices))]
    public void Replacement_keeps_the_lifetime_of_the_provider_registration(
        Type serviceType, Type implementationType)
    {
        // NpgsqlDesignTimeServices is an Npgsql internal API (EF1001)
        var provider = new ServiceCollection().AddEntityFrameworkDesignTimeServices();
        new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(provider);
        var expected = provider.Single(d => d.ServiceType == serviceType).Lifetime;

        var services = new ServiceCollection().AddEntityFrameworkDesignTimeServices();
        new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
        new TenancyDesignTimeServices().ConfigureDesignTimeServices(services);

        var descriptor = Assert.Single(services, d => d.ServiceType == serviceType);
        Assert.Equal(implementationType, descriptor.ImplementationType);
        Assert.Equal(expected, descriptor.Lifetime);
    }

    [Fact]
    public void Database_model_factory_resolves_with_scope_validation()
    {
        var services = new ServiceCollection().AddEntityFrameworkDesignTimeServices();
        new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
        new TenancyDesignTimeServices().ConfigureDesignTimeServices(services);

        // A singleton that captures the scoped scaffolding logger would fail here
        using var root = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = root.CreateScope();

        Assert.IsType<TenancyDatabaseModelFactory>(
            scope.ServiceProvider.GetRequiredService<IDatabaseModelFactory>());
    }
}
