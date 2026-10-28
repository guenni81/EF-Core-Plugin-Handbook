using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata.Internal;

/// <summary>
/// Copies RLS annotations from the entity type onto the table of the relational model.
/// Only then does the MigrationsModelDiffer see changes and generate
/// Create/AlterTableOperation with .Annotation(...) and .OldAnnotation(...).
/// </summary>
/// <remarks>
/// Inherits from the Npgsql provider: there is exactly one annotation provider per context.
/// <c>NpgsqlAnnotationProvider</c> lives in an *.Internal namespace (internal Npgsql API,
/// EF1001) and may change in any release.
/// </remarks>
public class TenancyAnnotationProvider : NpgsqlAnnotationProvider
{
    /// <summary>Creates the provider.</summary>
    /// <param name="dependencies">Dependencies of the relational annotation provider.</param>
    public TenancyAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>Returns Npgsql's annotations plus the RLS annotations of the root entity type.</summary>
    /// <param name="table">The relational table.</param>
    /// <param name="designTime">Whether the design-time model (migrations) is being built.</param>
    /// <returns>The annotations for the table.</returns>
    public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
    {
        foreach (var annotation in base.For(table, designTime))
            yield return annotation;

        // Only relevant for migrations: the runtime model stays lean
        if (!designTime)
            yield break;

        // With TPH/table splitting several types share one table. RLS is configured on root
        // types only (validation enforces this); with table splitting any of them may carry it,
        // and TenantTableMappingValidator ensures that they agree.
        var entityType = table.EntityTypeMappings
            .Select(m => (IEntityType)m.TypeBase)
            .FirstOrDefault(e => e.HasRlsConfiguration())
            ?? (IEntityType)table.EntityTypeMappings.First().TypeBase;

        foreach (var annotation in entityType.GetAnnotations())
        {
            if (!annotation.Name.StartsWith(RlsAnnotationNames.Prefix, StringComparison.Ordinal))
                continue;

            // Do not pass on the opt-out marker and an explicit force: false: otherwise
            // "missing" and "false" would differ for the differ and produce empty AlterTable
            // migrations. The model keeps them, so that they still beat the conventions.
            if (annotation.Name is RlsAnnotationNames.Enabled or RlsAnnotationNames.Forced
                && annotation.Value is false)
                continue;

            yield return annotation;
        }
    }
}
