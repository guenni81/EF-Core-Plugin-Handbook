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

    /// <summary>Creates the plugin.</summary>
    /// <param name="dependencies">Provides the model logger for the conventions.</param>
    /// <param name="loggingDefinitions">The tenancy package's cached log definitions.</param>
    public SqlServerTenancyConventionSetPlugin(
        ProviderConventionSetBuilderDependencies dependencies,
        TenancyLoggingDefinitions loggingDefinitions)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(loggingDefinitions);

        _dependencies = dependencies;
        _loggingDefinitions = loggingDefinitions;
    }

    /// <summary>Adds the key value generation and the tenant isolation convention.</summary>
    /// <param name="conventionSet">The provider's convention set.</param>
    /// <returns>The same convention set.</returns>
    public virtual ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.Add(new TenantIdKeyValueGenerationConvention());   // from the core
        conventionSet.Add(new SqlServerTenantIsolationConvention(_dependencies.Logger, _loggingDefinitions));
        return conventionSet;
    }
}
