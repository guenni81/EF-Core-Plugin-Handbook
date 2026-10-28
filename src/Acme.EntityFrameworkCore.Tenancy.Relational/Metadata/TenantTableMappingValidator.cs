using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Acme.EntityFrameworkCore.Tenancy.Metadata;

/// <summary>
/// Rejects table mappings in which tenant data would end up in a table without tenant
/// isolation. Shared by all provider packages, which call it before they annotate tables.
/// </summary>
/// <remarks>
/// Tenant isolation is a property of a table, not of an entity type: the policy needs the
/// tenant column in the same table. Supported are exactly the mappings in which every table
/// with tenant data contains that column: a table per root type, TPH, owned types in the
/// owner's table or in a JSON column, and table splitting. TPT, TPC, entity splitting, owned
/// types in a separate table and implicit many-to-many join tables between tenant-owned types
/// are rejected, unless the entity type is deliberately excluded from isolation.
/// </remarks>
public static class TenantTableMappingValidator
{
    /// <summary>Throws if a tenant-owned entity type is mapped in an unsupported way.</summary>
    /// <param name="model">The model, at the earliest in ModelFinalizing.</param>
    /// <param name="isOptedOut">
    /// Whether an entity type is deliberately excluded from tenant isolation (provider-specific:
    /// attribute and, where available, Fluent API).
    /// </param>
    /// <exception cref="InvalidOperationException">An unsupported mapping was found.</exception>
    public static void Validate(IReadOnlyModel model, Func<IReadOnlyEntityType, bool> isOptedOut)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(isOptedOut);

        var entityTypes = model.GetEntityTypes().ToList();

        foreach (var root in entityTypes.Where(e => e.BaseType is null && !e.IsOwned()))
            ValidateHierarchy(root, isOptedOut);

        foreach (var owned in entityTypes.Where(e => e.IsOwned()))
            ValidateOwnedType(owned, isOptedOut);

        ValidateTableSplitting(entityTypes, isOptedOut);

        foreach (var entityType in entityTypes)
        foreach (var skip in entityType.GetDeclaredSkipNavigations())
            ValidateJoinEntityType(skip, isOptedOut);
    }

    /// <summary>Is the entity type tenant-owned (marker interface or marked property)?</summary>
    private static bool IsTenantOwned(IReadOnlyEntityType entityType)
        => typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType)
           || entityType.FindTenantPropertyCandidate() is not null;

    /// <summary>Tenant-owned and not deliberately excluded.</summary>
    private static bool IsIsolated(IReadOnlyEntityType entityType, Func<IReadOnlyEntityType, bool> isOptedOut)
        => IsTenantOwned(entityType) && !isOptedOut(entityType);

    private static void ValidateHierarchy(
        IReadOnlyEntityType root, Func<IReadOnlyEntityType, bool> isOptedOut)
    {
        var types = root.GetDerivedTypesInclusive().ToList();
        if (isOptedOut(root) || !types.Any(IsTenantOwned))
            return;

        // The tenant column must exist in the hierarchy's table for every row
        if (!IsTenantOwned(root))
        {
            var derived = types.First(IsTenantOwned);
            throw new InvalidOperationException(
                $"'{derived.DisplayName()}' is tenant-owned, but the root type "
                + $"'{root.DisplayName()}' of its hierarchy is not. Declare the tenant property on "
                + "the root type; tenant isolation protects whole tables.");
        }

        var rootTable = StoreObjectIdentifier.Create(root, StoreObjectType.Table);
        foreach (var type in types)
        {
            // TPT: derived tables lack the tenant column. TPC: one table per concrete type,
            // the plugin protects exactly one table per hierarchy.
            if (StoreObjectIdentifier.Create(type, StoreObjectType.Table) is { } table
                && table != rootTable)
            {
                throw new InvalidOperationException(
                    $"'{root.DisplayName()}' is tenant-owned and mapped to more than one table "
                    + $"('{type.DisplayName()}' is stored in '{table.DisplayName()}', mapping "
                    + $"strategy {root.GetMappingStrategy() ?? "TPT"}). Tenant isolation supports "
                    + "one table per hierarchy: use TPH (the default) or map the types as "
                    + "separate entity types.");
            }

            // Entity splitting: the additional tables have no tenant column
            if (type.GetMappingFragments(StoreObjectType.Table).FirstOrDefault() is { } fragment)
                throw new InvalidOperationException(
                    $"'{type.DisplayName()}' is tenant-owned and split across tables "
                    + $"('{fragment.StoreObject.DisplayName()}'). Entity splitting is not supported "
                    + "with tenant isolation: the additional table has no tenant column.");
        }
    }

    private static void ValidateOwnedType(
        IReadOnlyEntityType owned, Func<IReadOnlyEntityType, bool> isOptedOut)
    {
        var owner = owned;
        while (owner.IsOwned())
            owner = owner.FindOwnership()!.PrincipalEntityType;

        var ownerRoot = owner.GetRootType();
        if (!IsIsolated(ownerRoot, isOptedOut))
        {
            // The conventions never isolate owned types on their own
            if (IsTenantOwned(owned) && !isOptedOut(ownerRoot))
                throw new InvalidOperationException(
                    $"Owned type '{owned.DisplayName()}' is tenant-owned, but its owner "
                    + $"'{owner.DisplayName()}' is not. Tenant isolation is configured on the "
                    + "owner, whose table it protects.");
            return;
        }

        // JSON: the data lives in a column of the owner's table
        if (owned.IsMappedToJson())
            return;

        var table = StoreObjectIdentifier.Create(owned, StoreObjectType.Table);
        if (table is not null && table != StoreObjectIdentifier.Create(owner, StoreObjectType.Table))
            throw new InvalidOperationException(
                $"Owned type '{owned.DisplayName()}' of the tenant-owned '{owner.DisplayName()}' is "
                + $"stored in its own table '{table.Value.DisplayName()}', which has no tenant "
                + "column (OwnsMany or ToTable). Map it to JSON (ToJson), keep it in the owner's "
                + "table, or make it an entity type with its own tenant property.");

        if (owned.GetMappingFragments(StoreObjectType.Table).Any())
            throw new InvalidOperationException(
                $"Owned type '{owned.DisplayName()}' of the tenant-owned '{owner.DisplayName()}' is "
                + "split across tables. Entity splitting is not supported with tenant isolation.");
    }

    private static void ValidateTableSplitting(
        IReadOnlyList<IReadOnlyEntityType> entityTypes, Func<IReadOnlyEntityType, bool> isOptedOut)
    {
        // Root types that share a table: all of them use the same rows, so one policy protects
        // them, provided they agree on the tenant column and on the opt-out
        var groups = entityTypes
            .Where(e => e.BaseType is null && !e.IsOwned() && IsTenantOwned(e))
            .Select(e => (Type: e, Table: StoreObjectIdentifier.Create(e, StoreObjectType.Table)))
            .Where(t => t.Table is not null)
            .GroupBy(t => t.Table!.Value)
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var columns = group
                .Select(t => (OptedOut: isOptedOut(t.Type),
                    Column: t.Type.FindTenantPropertyCandidate()?.GetColumnName(group.Key)))
                .Distinct()
                .ToList();

            if (columns.Count > 1)
                throw new InvalidOperationException(
                    $"The entity types sharing table '{group.Key.DisplayName()}' ("
                    + string.Join(", ", group.Select(t => t.Type.DisplayName()))
                    + ") disagree on the tenant column or on the exclusion from tenant isolation. "
                    + "Table splitting requires one tenant column for the whole table.");
        }
    }

    private static void ValidateJoinEntityType(
        IReadOnlySkipNavigation skip, Func<IReadOnlyEntityType, bool> isOptedOut)
    {
        if (skip.JoinEntityType is not { } join)
            return;

        var tenantSide = IsIsolated(skip.DeclaringEntityType.GetRootType(), isOptedOut)
                         || IsIsolated(skip.TargetEntityType.GetRootType(), isOptedOut);

        // A join entity type with its own tenant property is an ordinary isolated table
        if (!tenantSide || IsTenantOwned(join) || isOptedOut(join))
            return;

        throw new InvalidOperationException(
            $"The many-to-many relationship '{skip.DeclaringEntityType.DisplayName()}."
            + $"{skip.Name}' uses the join table of '{join.DisplayName()}', which has no tenant "
            + "column. Configure the join entity type with UsingEntity and a tenant property "
            + "(ITenantOwned or IsTenantProperty), or exclude it from tenant isolation explicitly.");
    }
}
