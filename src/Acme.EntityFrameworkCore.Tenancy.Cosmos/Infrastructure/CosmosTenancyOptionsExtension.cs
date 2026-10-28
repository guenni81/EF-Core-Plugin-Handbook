using Acme.EntityFrameworkCore.Tenancy.Cosmos.Conventions;
using Acme.EntityFrameworkCore.Tenancy.Cosmos.Storage;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Infrastructure;

/// <summary>
/// Options extension of the Cosmos package. Registers only plugin interfaces of EF Core itself:
/// Cosmos is not a relational provider, so there is no SQL generator, no migrations and no
/// session to replace.
/// </summary>
public class CosmosTenancyOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>Creates an extension with <see cref="CosmosTenancySettings.Default" />.</summary>
    public CosmosTenancyOptionsExtension()
    {
    }

    /// <summary>Copy constructor used by <see cref="Clone" />.</summary>
    /// <param name="copyFrom">The extension to copy.</param>
    protected CosmosTenancyOptionsExtension(CosmosTenancyOptionsExtension copyFrom)
    {
        ArgumentNullException.ThrowIfNull(copyFrom);

        Settings = copyFrom.Settings;
    }

    /// <summary>The configured settings.</summary>
    public virtual CosmosTenancySettings Settings { get; private set; } = CosmosTenancySettings.Default;

    /// <inheritdoc />
    public virtual DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>Creates a copy; derived extensions override this to keep their own type.</summary>
    /// <returns>The copy.</returns>
    protected virtual CosmosTenancyOptionsExtension Clone() => new(this);

    /// <summary>Returns a copy with other settings; options extensions are treated as immutable.</summary>
    /// <param name="settings">The new settings.</param>
    /// <returns>The modified copy.</returns>
    public virtual CosmosTenancyOptionsExtension WithSettings(CosmosTenancySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var clone = Clone();
        clone.Settings = settings;
        return clone;
    }

    /// <summary>Registers the package's services in EF's internal service provider.</summary>
    /// <param name="services">The internal service collection.</param>
    public virtual void ApplyServices(IServiceCollection services)
    {
        // EntityFrameworkServicesBuilder, not the relational one: only core services exist here
        new EntityFrameworkServicesBuilder(services)
            .TryAdd<ITypeMappingSourcePlugin, CosmosTenantIdTypeMappingSourcePlugin>()
            .TryAdd<IConventionSetPlugin, CosmosTenancyConventionSetPlugin>();

        // Optional materialization check from the core package, unchanged
        if (Settings.VerifyMaterializedTenants)
            new EntityFrameworkServicesBuilder(services)
                .TryAdd<ISingletonInterceptor, TenantMaterializationInterceptor>();
    }

    /// <summary>Ensures that the context uses the Cosmos provider.</summary>
    /// <param name="options">The context's options.</param>
    /// <exception cref="InvalidOperationException"><c>UseCosmos()</c> was not called.</exception>
    public virtual void Validate(IDbContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // CosmosOptionsExtension lives in an *.Internal namespace (EF1001). The provider's
        // assembly name is public knowledge, and EF's own IsCosmos() compares it the same way.
        if (!options.Extensions.Any(e => e.Info.IsDatabaseProvider
                                         && e.GetType().Assembly.GetName().Name
                                         == "Microsoft.EntityFrameworkCore.Cosmos"))
            throw new InvalidOperationException("This package requires UseCosmos().");
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(CosmosTenancyOptionsExtension extension)
            : base(extension)
        {
        }

        private new CosmosTenancyOptionsExtension Extension
            => (CosmosTenancyOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "AcmeTenancy(Cosmos) ";

        public override int GetServiceProviderHashCode() => Extension.Settings.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo o && o.Extension.Settings == Extension.Settings;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Acme:Tenancy:Cosmos"] = Extension.Settings.GetHashCode().ToString();
    }
}
