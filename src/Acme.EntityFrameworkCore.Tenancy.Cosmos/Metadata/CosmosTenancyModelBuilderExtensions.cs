using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Acme.EntityFrameworkCore.Tenancy;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Query filter on the current tenant for Cosmos DB, called in <c>OnModelCreating</c>:
/// <c>modelBuilder.HasTenantQueryFilters(this)</c>.
/// </summary>
/// <remarks>
/// Unlike the relational providers, there is no database session that knows the tenant. The
/// filter therefore references the context, which EF replaces by the current context for every
/// query; the tenant becomes a query parameter. Because the filter compares the partition key
/// with that parameter, EF Core sends the query to a single partition.
/// </remarks>
public static class CosmosTenancyModelBuilderExtensions
{
    /// <summary>The name of the filter, for <c>IgnoreQueryFilters(["Acme.Tenancy"])</c>.</summary>
    public const string QueryFilterKey = "Acme.Tenancy";

    private static readonly ConditionalWeakTable<DbContext, ITenantProvider> Providers = new();

    /// <summary>
    /// Adds a named query filter <c>e.TenantId == current tenant</c> to every tenant-owned root
    /// entity type.
    /// </summary>
    /// <param name="modelBuilder">The model builder passed to <c>OnModelCreating</c>.</param>
    /// <param name="context">The context itself (<see langword="this"/>).</param>
    /// <returns>The same model builder.</returns>
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Runs only while the model is built; NativeAOT uses a compiled model.")]
    public static ModelBuilder HasTenantQueryFilters(this ModelBuilder modelBuilder, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        var currentTenant = Expression.Call(
            typeof(CosmosTenancyModelBuilderExtensions).GetMethod(nameof(CurrentTenant))!,
            Expression.Constant(context));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.BaseType is not null || entityType.IsOwned()
                || entityType.ClrType.IsDefined(typeof(NoTenantIsolationAttribute), inherit: true)
                || entityType.FindTenantPropertyCandidate() is not { } property)
                continue;

            // e => (TenantId?)EF.Property<TenantId>(e, "TenantId") == CurrentTenant(context)
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Convert(
                    Expression.Call(typeof(EF), nameof(EF.Property), [typeof(TenantId)],
                        parameter, Expression.Constant(property.Name)),
                    typeof(TenantId?)),
                currentTenant);

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(QueryFilterKey, Expression.Lambda(body, parameter));
        }

        return modelBuilder;
    }

    /// <summary>
    /// The current tenant of a context, from the tenant provider of its SaveChanges interceptor.
    /// Evaluated by EF before every query, never translated.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The current tenant, or <see langword="null"/> (then the filter matches nothing).</returns>
    /// <exception cref="TenancyException">The context was not configured with UseAcmeTenancy(sp).</exception>
    public static TenantId? CurrentTenant(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Providers.GetValue(context, LookUpTenantProvider).CurrentTenantId;
    }

    private static ITenantProvider LookUpTenantProvider(DbContext context)
        => context.GetService<IDbContextOptions>()
               .FindExtension<CoreOptionsExtension>()?.Interceptors?
               .OfType<TenantSaveChangesInterceptor>().FirstOrDefault()?.Tenants
           ?? throw new TenancyException(
               "The tenant query filter needs the tenant provider of the context. Activate the "
               + "plugin with UseCosmos(..., cosmos => cosmos.UseAcmeTenancy(applicationServices)).");
}
