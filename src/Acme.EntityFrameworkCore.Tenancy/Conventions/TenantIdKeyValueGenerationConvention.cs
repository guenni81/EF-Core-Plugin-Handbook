using Acme.EntityFrameworkCore.Tenancy.ValueGeneration;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Acme.EntityFrameworkCore.Tenancy.Conventions;

/// <summary>
/// Gives primary keys of type <see cref="TenantId"/> client-side value generation.
/// Provider-independent: lives in the core package and is plugged in by the provider package.
/// </summary>
/// <remarks>
/// Deliberately uses HasValueGeneratorFactory(Type): only a factory type can be carried over into
/// a compiled model. EF Core 10's compiled model generator rejects HasValueGenerator(Type) as well
/// as factory delegates with an error.
/// </remarks>
public class TenantIdKeyValueGenerationConvention : IModelFinalizingConvention
{
    /// <summary>
    /// Configures value generation on single-property <see cref="TenantId"/> keys of all root
    /// entity types. Runs at model finalization so that keys configured by the user are known.
    /// </summary>
    /// <param name="modelBuilder">The builder for the model.</param>
    /// <param name="context">Additional information associated with convention execution.</param>
    public virtual void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            // Derived types share the root's key; composite keys are left alone
            if (entityType.BaseType is not null
                || entityType.FindPrimaryKey() is not { Properties: [var property] }
                || property.ClrType != typeof(TenantId))
            {
                continue;
            }

            // With ConfigurationSource.Convention: an explicit ValueGeneratedNever()
            // by the user wins, and the call then returns null.
            if (property.Builder.ValueGenerated(ValueGenerated.OnAdd) is null)
                continue;

            property.Builder.HasValueGeneratorFactory(typeof(TenantIdValueGeneratorFactory));
        }
    }
}
