using Acme.EntityFrameworkCore.Tenancy.Diagnostics;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.Query;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Conventions;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata.Internal;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Migrations.Internal;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Query;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;

/// <summary>Options extension of the SQL Server package. Same pattern as in the PostgreSQL package.</summary>
public class SqlServerTenancyOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>Creates an extension with <see cref="SqlServerTenancySettings.Default" />.</summary>
    public SqlServerTenancyOptionsExtension()
    {
    }

    /// <summary>Copy constructor used by <see cref="Clone" />.</summary>
    /// <param name="copyFrom">The extension to copy.</param>
    protected SqlServerTenancyOptionsExtension(SqlServerTenancyOptionsExtension copyFrom)
    {
        ArgumentNullException.ThrowIfNull(copyFrom);

        Settings = copyFrom.Settings;
    }

    /// <summary>The configured settings.</summary>
    public virtual SqlServerTenancySettings Settings { get; private set; } = SqlServerTenancySettings.Default;

    /// <inheritdoc />
    public virtual DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>Creates a copy; derived extensions override this to keep their own type.</summary>
    /// <returns>The copy.</returns>
    protected virtual SqlServerTenancyOptionsExtension Clone() => new(this);

    /// <summary>Returns a copy with other settings; options extensions are treated as immutable.</summary>
    /// <param name="settings">The new settings.</param>
    /// <returns>The modified copy.</returns>
    public virtual SqlServerTenancyOptionsExtension WithSettings(SqlServerTenancySettings settings)
    {
        var clone = Clone();
        clone.Settings = settings;
        return clone;
    }

    /// <summary>Registers the package's services in EF's internal service provider.</summary>
    /// <param name="services">The internal service collection.</param>
    public virtual void ApplyServices(IServiceCollection services)
    {
        // TryAdd suffices for the plugin interfaces (multi-registration services); services the
        // provider registers as single instances are replaced below.
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IRelationalTypeMappingSourcePlugin, SqlServerTenancyTypeMappingSourcePlugin>()
            .TryAdd<IMethodCallTranslatorPlugin, SqlServerTenancyMethodCallTranslatorPlugin>()
            // Provider-independent, reused from the core package
            .TryAdd<IEvaluatableExpressionFilterPlugin, TenancyEvaluatableExpressionFilterPlugin>()
            .TryAdd<IConventionSetPlugin, SqlServerTenancyConventionSetPlugin>()
            // Registered as ISingletonOptions too, so EF calls Initialize/Validate on it
            .TryAdd<ISingletonOptions, ISqlServerTenancySingletonOptions>(
                p => p.GetRequiredService<ISqlServerTenancySingletonOptions>())
            .TryAddProviderSpecificServices(m => m
                .TryAddSingleton<ISqlServerTenancySingletonOptions, SqlServerTenancySingletonOptions>()
                .TryAddSingleton<TenancyLoggingDefinitions, TenancyLoggingDefinitions>());

        // Optional materialization check from the core package; a singleton interceptor in the
        // internal provider, part of the hash through the settings
        if (Settings.VerifyMaterializedTenants)
            new EntityFrameworkServicesBuilder(services)
                .TryAdd<ISingletonInterceptor, TenantMaterializationInterceptor>();

        // Single-instance services of the provider: replace them; the classes derive from the
        // SQL Server implementations, so all non-tenancy behaviour stays unchanged
        services.Replace(ServiceDescriptor
            .Scoped<IMigrationsSqlGenerator, SqlServerTenancyMigrationsSqlGenerator>());
        services.Replace(ServiceDescriptor
            .Singleton<IRelationalAnnotationProvider, SqlServerTenancyAnnotationProvider>());
    }

    /// <summary>Ensures that the context uses SQL Server.</summary>
    /// <param name="options">The context's options.</param>
    /// <exception cref="InvalidOperationException"><c>UseSqlServer()</c> was not called.</exception>
    public virtual void Validate(IDbContextOptions options)
    {
        // SqlServerOptionsExtension lives in an *.Internal namespace (internal EF API, EF1001);
        // it is the most direct way to detect the SQL Server provider in the options
        if (options.FindExtension<SqlServerOptionsExtension>() is null)
            throw new InvalidOperationException("This package requires UseSqlServer().");
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(SqlServerTenancyOptionsExtension extension)
            : base(extension)
        {
        }

        private new SqlServerTenancyOptionsExtension Extension
            => (SqlServerTenancyOptionsExtension)base.Extension;

        // A plugin, not a provider: EF requires exactly one extension that returns true here
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => $"AcmeTenancy(SessionKey={Extension.Settings.SessionKey}) ";

        // Hash and equality decide whether contexts share an internal service provider:
        // different settings need a separate one, because the model and singletons depend on them
        public override int GetServiceProviderHashCode() => Extension.Settings.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo o && o.Extension.Settings == Extension.Settings;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Acme:Tenancy:SqlServer"] = Extension.Settings.GetHashCode().ToString();
    }
}
