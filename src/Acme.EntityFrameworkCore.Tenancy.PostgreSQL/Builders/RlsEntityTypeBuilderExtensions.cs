using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Builders;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

// In the EF namespace: discoverable without a using, and usable in the generated model snapshot
namespace Microsoft.EntityFrameworkCore;

/// <summary>Fluent API for row level security.</summary>
public static class RlsEntityTypeBuilderExtensions
{
    // ===== Non-generic: the actual implementation (used by the snapshot) =====

    /// <summary>Enables RLS for the entity's table.</summary>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="force">FORCE ROW LEVEL SECURITY: also applies to the table owner.</param>
    /// <returns>The same builder for chaining.</returns>
    public static EntityTypeBuilder HasRowLevelSecurity(
        this EntityTypeBuilder builder, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.SetAnnotation(RlsAnnotationNames.Enabled, true);

        // Store false too: an explicit decision must beat the convention, which sets FORCE when
        // ForceRowLevelSecurity is on. The annotation provider drops false for migrations, so
        // "missing" and "false" produce the same table and no empty migrations.
        builder.Metadata.SetAnnotation(RlsAnnotationNames.Forced, force);

        return builder;
    }

    /// <summary>Explicitly excludes the entity from automatic isolation.</summary>
    /// <param name="builder">The entity type builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static EntityTypeBuilder HasNoRowLevelSecurity(this EntityTypeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Stored as false (not removed): the convention must see the explicit opt-out
        builder.Metadata.SetAnnotation(RlsAnnotationNames.Enabled, false);
        builder.Metadata.RemoveAnnotation(RlsAnnotationNames.Forced);
        return builder;
    }

    /// <summary>Defines a policy (CREATE POLICY) for the entity's table.</summary>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="name">Policy name, unique per table.</param>
    /// <param name="configure">Configures the policy.</param>
    /// <returns>The same builder for chaining.</returns>
    public static EntityTypeBuilder HasPolicy(
        this EntityTypeBuilder builder, string name, Action<PolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var policyBuilder = new PolicyBuilder(name);
        configure(policyBuilder);

        // One annotation per policy: the differ sees adding, changing and removing individually
        builder.Metadata.SetAnnotation(
            RlsAnnotationNames.PolicyPrefix + name, policyBuilder.Build().Serialize());
        return builder;
    }

    // ===== Generic: delegates, but keeps the type for further chaining =====

    /// <inheritdoc cref="HasRowLevelSecurity(EntityTypeBuilder, bool)"/>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    public static EntityTypeBuilder<TEntity> HasRowLevelSecurity<TEntity>(
        this EntityTypeBuilder<TEntity> builder, bool force = false)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasRowLevelSecurity((EntityTypeBuilder)builder, force);

    /// <inheritdoc cref="HasNoRowLevelSecurity(EntityTypeBuilder)"/>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    public static EntityTypeBuilder<TEntity> HasNoRowLevelSecurity<TEntity>(
        this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasNoRowLevelSecurity((EntityTypeBuilder)builder);

    /// <inheritdoc cref="HasPolicy(EntityTypeBuilder, string, Action{PolicyBuilder})"/>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    public static EntityTypeBuilder<TEntity> HasPolicy<TEntity>(
        this EntityTypeBuilder<TEntity> builder, string name, Action<PolicyBuilder> configure)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasPolicy((EntityTypeBuilder)builder, name, configure);

    // ===== Convention builder: respects ConfigurationSource =====

    /// <summary>
    /// For custom conventions: sets RLS with the configuration source Convention or
    /// DataAnnotation. Returns null if a stronger source has decided otherwise.
    /// </summary>
    /// <param name="builder">The convention entity type builder.</param>
    /// <param name="enabled"><see langword="true"/> enables RLS, <see langword="false"/> opts out.</param>
    /// <param name="fromDataAnnotation">Use DataAnnotation instead of Convention as the source.</param>
    /// <returns>The builder, or <see langword="null"/> if the value was not applied.</returns>
    public static IConventionEntityTypeBuilder? HasRowLevelSecurity(
        this IConventionEntityTypeBuilder builder, bool enabled, bool fromDataAnnotation = false)
        => builder.HasAnnotation(RlsAnnotationNames.Enabled, enabled, fromDataAnnotation) is null
            ? null
            : builder;
}
