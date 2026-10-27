using Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.Conventions;
using Acme.EntityFrameworkCore.Tenancy.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Conventions;

/// <summary>Adds the package's model building conventions to the provider's convention set.</summary>
public class SqlServerTenancyConventionSetPlugin : IConventionSetPlugin
{
    private readonly ProviderConventionSetBuilderDependencies _dependencies;
    private readonly TenancyLoggingDefinitions _loggingDefinitions;
    private readonly SqlServerTenancySettings? _settings;

    /// <summary>Creates the plugin.</summary>
    /// <param name="dependencies">Provides the model logger for the conventions.</param>
    /// <param name="loggingDefinitions">The tenancy package's cached log definitions.</param>
    [Obsolete("Without the options the model does not record session key and policy schema, so "
              + "migrations cannot detect changes. Use the constructor with the options.")]
    public SqlServerTenancyConventionSetPlugin(
        ProviderConventionSetBuilderDependencies dependencies,
        TenancyLoggingDefinitions loggingDefinitions)
        : this(dependencies, loggingDefinitions, options: null)
    {
    }

    /// <summary>Creates the plugin.</summary>
    /// <param name="dependencies">EF's convention dependencies (logger, type mapping source, ...).</param>
    /// <param name="loggingDefinitions">The tenancy package's cached log definitions.</param>
    /// <param name="options">Session key and policy schema, recorded in the model.</param>
    public SqlServerTenancyConventionSetPlugin(
        ProviderConventionSetBuilderDependencies dependencies,
        TenancyLoggingDefinitions loggingDefinitions,
        ISqlServerTenancySingletonOptions? options)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(loggingDefinitions);

        _dependencies = dependencies;
        _loggingDefinitions = loggingDefinitions;
        _settings = options?.Settings;
    }

    /// <summary>Adds the key value generation and the tenant isolation convention.</summary>
    /// <param name="conventionSet">The provider's convention set.</param>
    /// <returns>The same convention set.</returns>
    public virtual ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.Add(new TenantIdKeyValueGenerationConvention());   // from the core
        conventionSet.Add(new SqlServerTenantIsolationConvention(
            _dependencies.Logger, _loggingDefinitions, _settings));
        return conventionSet;
    }
}
