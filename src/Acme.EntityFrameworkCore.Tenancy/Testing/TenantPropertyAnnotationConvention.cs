using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Acme.EntityFrameworkCore.Tenancy.Testing;

/// <summary>
/// Records the tenant property of every tenant-owned root entity type in the runtime
/// annotation that the core interceptors read; the provider packages do this in their isolation
/// conventions.
/// </summary>
public class TenantPropertyAnnotationConvention : IModelFinalizingConvention
{
    /// <summary>Sets the annotation on all tenant-owned root entity types.</summary>
    /// <param name="modelBuilder">The builder for the model.</param>
    /// <param name="context">Additional information associated with convention execution.</param>
    public virtual void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.BaseType is not null || entityType.IsOwned()
                || entityType.ClrType.IsDefined(typeof(NoTenantIsolationAttribute), inherit: true)
                || entityType.FindTenantPropertyCandidate() is not { } candidate)
                continue;

            entityType.Builder.HasAnnotation(TenancyAnnotationNames.TenantProperty, candidate.Name);
        }
    }
}
