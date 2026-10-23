using Acme.EntityFrameworkCore.Tenancy.Conventions;
using Acme.EntityFrameworkCore.Tenancy.Diagnostics;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Conventions;

/// <summary>Hooks the plugin's conventions into the convention set (scoped).</summary>
public class TenancyConventionSetPlugin : IConventionSetPlugin
{
    private readonly ProviderConventionSetBuilderDependencies _dependencies;
    private readonly IDbContextOptions _options;
    private readonly ISqlGenerationHelper _sqlGenerationHelper;
    private readonly TenancyLoggingDefinitions _loggingDefinitions;

    /// <summary>Creates the plugin. Resolved by EF from the internal service provider.</summary>
    /// <param name="dependencies">Dependencies of provider conventions (includes the model logger).</param>
    /// <param name="options">Options of the current context; source of the tenancy settings.</param>
    /// <param name="sqlGenerationHelper">Delimits the tenant column in the policy SQL.</param>
    /// <param name="loggingDefinitions">Cached log definitions of the plugin.</param>
    public TenancyConventionSetPlugin(
        ProviderConventionSetBuilderDependencies dependencies,
        IDbContextOptions options,
        ISqlGenerationHelper sqlGenerationHelper,
        TenancyLoggingDefinitions loggingDefinitions)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sqlGenerationHelper);
        ArgumentNullException.ThrowIfNull(loggingDefinitions);

        _dependencies = dependencies;
        _options = options;
        _sqlGenerationHelper = sqlGenerationHelper;
        _loggingDefinitions = loggingDefinitions;
    }

    /// <summary>Adds the tenancy conventions; does nothing if UseAcmeTenancy() was not called.</summary>
    /// <param name="conventionSet">The convention set to extend.</param>
    /// <returns>The same convention set.</returns>
    public virtual ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        var settings = _options.FindExtension<TenancyOptionsExtension>()?.Settings;
        if (settings is null)
            return conventionSet;

        // Add() puts the convention into every list whose interface it implements,
        // each time at the end: it runs after EF, provider and previously registered plugins.
        conventionSet.Add(new TenantIdKeyValueGenerationConvention());
        conventionSet.Add(new NoTenantIsolationAttributeConvention(_dependencies));
        conventionSet.Add(new TenantIsolationConvention(
            settings, _sqlGenerationHelper, _dependencies.Logger, _loggingDefinitions));
        conventionSet.Add(new TenancyModelValidationConvention(_dependencies.Logger, _loggingDefinitions));

        return conventionSet;
    }
}
