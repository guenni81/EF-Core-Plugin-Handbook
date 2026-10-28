using Acme.EntityFrameworkCore.Tenancy.Conventions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.EntityFrameworkCore.Tenancy.Testing;

/// <summary>
/// Test mode for applications (chapter "Test strategy"): the plugin's application-side behavior
/// on SQLite or the InMemory provider, for business logic tests that need no row level
/// security. Refuses the providers that have a package with real isolation.
/// </summary>
public class TenancyTestingOptionsExtension : IDbContextOptionsExtension
{
    private static readonly string[] ProvidersWithIsolation =
    [
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Microsoft.EntityFrameworkCore.Cosmos",
    ];

    private DbContextOptionsExtensionInfo? _info;

    /// <inheritdoc />
    public virtual DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>Registers the type mapping of TenantId and the conventions.</summary>
    /// <param name="services">The internal service collection.</param>
    public virtual void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, ConventionSetPlugin>();

        // A single service; only in test mode, where no other plugin is expected to replace it
        services.Replace(ServiceDescriptor
            .Singleton<IValueConverterSelector, TenancyValueConverterSelector>());
    }

    /// <summary>Refuses the test mode together with a provider that has real isolation.</summary>
    /// <param name="options">The context's options.</param>
    /// <exception cref="InvalidOperationException">The context uses PostgreSQL, SQL Server or Cosmos DB.</exception>
    public virtual void Validate(IDbContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var provider = options.Extensions
            .FirstOrDefault(e => e.Info.IsDatabaseProvider)?.GetType().Assembly.GetName().Name;
        if (provider is not null && ProvidersWithIsolation.Contains(provider))
            throw new InvalidOperationException(
                $"UseAcmeTenancyForTesting() is meant for SQLite or InMemory in application tests, "
                + $"not for {provider}. Use UseAcmeTenancy() of its provider package.");
    }

    private sealed class ConventionSetPlugin : IConventionSetPlugin
    {
        public ConventionSet ModifyConventions(ConventionSet conventionSet)
        {
            conventionSet.Add(new TenantIdKeyValueGenerationConvention());
            conventionSet.Add(new TenantPropertyAnnotationConvention());
            return conventionSet;
        }
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(TenancyTestingOptionsExtension extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "AcmeTenancy(Testing) ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Acme:Tenancy:Testing"] = "1";
    }
}
