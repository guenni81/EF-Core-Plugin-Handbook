using Acme.EntityFrameworkCore.Tenancy.Diagnostics;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.Query;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Conventions;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata.Internal;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Internal;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Query;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;

/// <summary>
/// Heart of the plugin: configuration plus registration of all services in EF's internal
/// service provider. Immutable: every change creates a copy via <see cref="Clone"/>.
/// </summary>
/// <remarks>
/// Uses the internal Npgsql type NpgsqlOptionsExtension (EF1001) to detect UseNpgsql().
/// </remarks>
public class TenancyOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>Creates an extension with <see cref="TenancySettings.Default"/>.</summary>
    public TenancyOptionsExtension()
    {
    }

    /// <summary>Copy constructor used by <see cref="Clone"/>.</summary>
    /// <param name="copyFrom">The extension to copy.</param>
    protected TenancyOptionsExtension(TenancyOptionsExtension copyFrom)
    {
        ArgumentNullException.ThrowIfNull(copyFrom);

        Settings = copyFrom.Settings;
    }

    /// <summary>The model-relevant plugin settings.</summary>
    public virtual TenancySettings Settings { get; private set; } = TenancySettings.Default;

    /// <summary>Metadata EF uses for logging, debug info and service provider caching.</summary>
    public virtual DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>Virtual so that derived extensions clone their own type.</summary>
    /// <returns>A copy of this extension.</returns>
    protected virtual TenancyOptionsExtension Clone() => new(this);

    /// <summary>Returns a copy with the given settings; this instance stays unchanged.</summary>
    /// <param name="settings">The new settings.</param>
    /// <returns>The modified copy.</returns>
    public virtual TenancyOptionsExtension WithSettings(TenancySettings settings)
    {
        var clone = Clone();
        clone.Settings = settings;
        return clone;
    }

    /// <summary>Registers the plugin's services in EF's internal service provider.</summary>
    /// <param name="services">The internal service collection.</param>
    public virtual void ApplyServices(IServiceCollection services)
    {
        // 1) Plugin interfaces: multiple registrations, composable with other plugins.
        //    The services builder knows the lifetime and cardinality of every EF service.
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IRelationalTypeMappingSourcePlugin, TenancyTypeMappingSourcePlugin>()
            .TryAdd<IMethodCallTranslatorPlugin, TenancyMethodCallTranslatorPlugin>()
            .TryAdd<IMemberTranslatorPlugin, TenancyMemberTranslatorPlugin>()
            .TryAdd<IEvaluatableExpressionFilterPlugin, TenancyEvaluatableExpressionFilterPlugin>()
            .TryAdd<IConventionSetPlugin, TenancyConventionSetPlugin>()
            .TryAdd<ISingletonOptions, ITenancySingletonOptions>(
                p => p.GetRequiredService<ITenancySingletonOptions>())
            // 2) Own services that EF does not know
            .TryAddProviderSpecificServices(m => m
                .TryAddSingleton<ITenancySingletonOptions, TenancySingletonOptions>()
                .TryAddSingleton<TenancyLoggingDefinitions, TenancyLoggingDefinitions>());

        // Optional materialization check (chapter "Materialization: from rows to objects"). A
        // singleton interceptor belongs in the internal provider: one instance per provider,
        // never a new instance per context. The setting is part of the hash.
        if (Settings.VerifyMaterializedTenants)
            new EntityFrameworkServicesBuilder(services)
                .TryAdd<ISingletonInterceptor, TenantMaterializationInterceptor>();

        // Optional check of bulk updates (chapter "Bulk operations and the limits of
        // interceptors"). Registered as IInterceptor: EF does not call query expression
        // interceptors registered as ISingletonInterceptor (BulkOperationTests)
        if (Settings.ProtectBulkOperations)
            new EntityFrameworkServicesBuilder(services)
                .TryAdd<IInterceptor, TenantQueryExpressionInterceptor>();

        // Optional tagging of traces (chapter "Diagnostics and logging"); IInterceptor is scoped,
        // one instance per context, the tenant comes from the context of each command
        if (Settings.EnrichOpenTelemetry)
            new EntityFrameworkServicesBuilder(services)
                .TryAdd<IInterceptor, TenantActivityEnrichmentInterceptor>();

        // 3) Replace single services of the provider. Replace instead of TryAdd: Npgsql registers
        //    via TryAdd, so our service wins regardless of the order.
        services.Replace(ServiceDescriptor
            .Scoped<IMigrationsSqlGenerator, TenancyMigrationsSqlGenerator>());
        services.Replace(ServiceDescriptor
            .Singleton<IRelationalAnnotationProvider, TenancyAnnotationProvider>());
    }

    /// <summary>Ensures the plugin is combined with the Npgsql provider.</summary>
    /// <param name="options">The complete context options.</param>
    /// <exception cref="InvalidOperationException">UseNpgsql() was not called.</exception>
    public virtual void Validate(IDbContextOptions options)
    {
        if (options.FindExtension<NpgsqlOptionsExtension>() is null)
            throw new InvalidOperationException(
                "UseAcmeTenancy() requires UseNpgsql(). Other databases need their own "
                + "provider package.");
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(TenancyOptionsExtension extension)
            : base(extension)
        {
        }

        private new TenancyOptionsExtension Extension => (TenancyOptionsExtension)base.Extension;

        // Not a provider: UseNpgsql() stays the one database provider of the context
        public override bool IsDatabaseProvider => false;

        // Appears in the "Entity Framework Core initialized ..." log message
        public override string LogFragment
            => $"AcmeTenancy(Setting={Extension.Settings.SettingName}) ";

        // All settings change the model -> all of them go into the hash.
        // Values that vary per context (tenant, connection string) must NOT go in.
        public override int GetServiceProviderHashCode() => Extension.Settings.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo o && o.Extension.Settings == Extension.Settings;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            // Hash codes instead of raw strings, as EF's own extensions do
            var s = Extension.Settings;
            debugInfo["Acme:Tenancy:SettingName"] = s.SettingName.GetHashCode().ToString();
            debugInfo["Acme:Tenancy:PolicyName"] = s.PolicyName.GetHashCode().ToString();
            debugInfo["Acme:Tenancy:Force"] = s.ForceRowLevelSecurity.ToString();
            debugInfo["Acme:Tenancy:QueryFilter"] = s.ApplyQueryFilter.ToString();
            debugInfo["Acme:Tenancy:Index"] = s.CreateTenantIndex.ToString();
            debugInfo["Acme:Tenancy:VerifyMaterialized"] = s.VerifyMaterializedTenants.ToString();
            debugInfo["Acme:Tenancy:ProtectBulkOperations"] = s.ProtectBulkOperations.ToString();
            debugInfo["Acme:Tenancy:TransactionPooling"] = s.UseTransactionPooling.ToString();
            debugInfo["Acme:Tenancy:EnrichOpenTelemetry"] = s.EnrichOpenTelemetry.ToString();
        }
    }
}
