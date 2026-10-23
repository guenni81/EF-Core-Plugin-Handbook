using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Fluent API at property level.</summary>
public static class TenancyPropertyBuilderExtensions
{
    /// <summary>
    /// Marks the property as the tenant property. This makes isolation work even without
    /// <c>ITenantOwned</c> or with a different property name (e.g. "OwnerTenant").
    /// The property must be of type TenantId.
    /// </summary>
    /// <param name="builder">The property builder.</param>
    /// <param name="isTenant">False removes the marking again.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static PropertyBuilder IsTenantProperty(this PropertyBuilder builder, bool isTenant = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Fluent API is explicit configuration: set directly, no ConfigurationSource check
        if (isTenant)
            builder.Metadata.SetAnnotation(TenancyAnnotationNames.IsTenantProperty, true);
        else
            builder.Metadata.RemoveAnnotation(TenancyAnnotationNames.IsTenantProperty);

        return builder;
    }

    /// <summary>
    /// Generic overload so that chaining keeps <see cref="PropertyBuilder{TProperty}"/>.
    /// </summary>
    /// <param name="builder">The property builder.</param>
    /// <param name="isTenant">False removes the marking again.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static PropertyBuilder<TProperty> IsTenantProperty<TProperty>(
        this PropertyBuilder<TProperty> builder, bool isTenant = true)
        => (PropertyBuilder<TProperty>)IsTenantProperty((PropertyBuilder)builder, isTenant);

    /// <summary>Convention variant: respects the ConfigurationSource.</summary>
    /// <param name="builder">The convention property builder.</param>
    /// <param name="isTenant">False removes the marking again.</param>
    /// <param name="fromDataAnnotation">True if the call stems from a data annotation.</param>
    /// <returns>
    /// The same builder, or null if a configuration with higher precedence prevents the change.
    /// </returns>
    public static IConventionPropertyBuilder? IsTenantProperty(
        this IConventionPropertyBuilder builder,
        bool isTenant = true,
        bool fromDataAnnotation = false)
        // A null value removes the annotation, if the configuration source allows it
        => builder.HasAnnotation(
               TenancyAnnotationNames.IsTenantProperty, isTenant ? true : null, fromDataAnnotation)
           is null
            ? null
            : builder;
}
