using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Conventions;

/// <summary>
/// Data annotation [NoTenantIsolation] -&gt; opt-out annotation with
/// ConfigurationSource.DataAnnotation. The Fluent API (Explicit) can override it.
/// </summary>
public class NoTenantIsolationAttributeConvention
    : TypeAttributeConventionBase<NoTenantIsolationAttribute>
{
    /// <summary>Creates the convention.</summary>
    /// <param name="dependencies">Dependencies of provider conventions.</param>
    public NoTenantIsolationAttributeConvention(ProviderConventionSetBuilderDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>Marks the entity type as opted out when it carries the attribute.</summary>
    /// <param name="entityTypeBuilder">Builder of the entity type that was added.</param>
    /// <param name="attribute">The attribute found on the CLR type.</param>
    /// <param name="context">Convention context.</param>
    protected override void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        NoTenantIsolationAttribute attribute,
        IConventionContext<IConventionEntityTypeBuilder> context)
        => entityTypeBuilder.HasRowLevelSecurity(enabled: false, fromDataAnnotation: true);
}
