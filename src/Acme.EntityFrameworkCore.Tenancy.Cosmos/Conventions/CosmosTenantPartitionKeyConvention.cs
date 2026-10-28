using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Conventions;

/// <summary>
/// Makes the tenant property the partition key of every tenant-owned root entity type and
/// records it in the runtime annotation that the core interceptors read.
/// </summary>
/// <remarks>
/// The same candidate lookup as in the relational providers (marked property, or
/// <c>TenantId</c> on <see cref="ITenantOwned"/>). Set with convention source: an explicit
/// <c>HasPartitionKey</c> of the user wins. EF's own
/// <c>CosmosPartitionKeyInPrimaryKeyConvention</c> then adds the partition key to the key.
/// </remarks>
public class CosmosTenantPartitionKeyConvention : IModelFinalizingConvention
{
    /// <summary>Configures the partition key of all tenant-owned root entity types.</summary>
    /// <param name="modelBuilder">The builder for the model.</param>
    /// <param name="context">Additional information associated with convention execution.</param>
    public virtual void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            // Cosmos only accepts the partition key on the root of a hierarchy; owned types are
            // embedded in their owner's document
            if (entityType.BaseType is not null || entityType.IsOwned()
                || entityType.ClrType.IsDefined(typeof(NoTenantIsolationAttribute), inherit: true))
                continue;

            if (entityType.FindTenantPropertyCandidate() is not { } candidate)
                continue;

            entityType.Builder.HasAnnotation(TenancyAnnotationNames.TenantProperty, candidate.Name);

            string[] partitionKey = [candidate.Name];
            if (entityType.Builder.CanSetPartitionKey(partitionKey))
                entityType.Builder.HasPartitionKey(partitionKey);
        }
    }
}
