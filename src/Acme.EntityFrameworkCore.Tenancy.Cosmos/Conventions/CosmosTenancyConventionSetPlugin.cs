using Acme.EntityFrameworkCore.Tenancy.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Conventions;

/// <summary>Adds the package's conventions to the Cosmos convention set.</summary>
public class CosmosTenancyConventionSetPlugin : IConventionSetPlugin
{
    /// <summary>Adds the key value generation (core) and the partition key convention.</summary>
    /// <param name="conventionSet">The provider's convention set.</param>
    /// <returns>The same convention set.</returns>
    public virtual ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        ArgumentNullException.ThrowIfNull(conventionSet);

        conventionSet.Add(new TenantIdKeyValueGenerationConvention());   // from the core
        conventionSet.Add(new CosmosTenantPartitionKeyConvention());
        return conventionSet;
    }
}
