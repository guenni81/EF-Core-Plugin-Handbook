using Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.SqlServer.Metadata.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata.Internal;

/// <summary>Copies the RLS annotation from the entity type to the table (design time only).</summary>
/// <remarks>
/// Derives from the internal <c>SqlServerAnnotationProvider</c> (EF1001) so that SQL Server's own
/// table annotations (memory-optimized, temporal, ...) are kept. The migrations model differ only
/// sees table annotations; this is how a changed tenant column becomes an AlterTableOperation.
/// </remarks>
public class SqlServerTenancyAnnotationProvider : SqlServerAnnotationProvider
{
    /// <summary>Creates the annotation provider.</summary>
    /// <param name="dependencies">The annotation provider dependencies.</param>
    public SqlServerTenancyAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>Returns SQL Server's model annotations plus session key and policy schema at design time.</summary>
    /// <param name="model">The relational model.</param>
    /// <param name="designTime">Whether the annotations are requested for migrations.</param>
    /// <returns>The annotations for the database.</returns>
    public override IEnumerable<IAnnotation> For(IRelationalModel model, bool designTime)
    {
        foreach (var annotation in base.For(model, designTime))
            yield return annotation;

        // A change becomes an AlterDatabaseOperation with old and new values
        if (!designTime)
            yield break;

        foreach (var name in new[] { SqlServerRlsAnnotationNames.SessionKey, SqlServerRlsAnnotationNames.PolicySchema })
        {
            if (model.Model.FindAnnotation(name) is { } annotation)
                yield return annotation;
        }
    }

    /// <summary>Returns SQL Server's table annotations plus the tenant column at design time.</summary>
    /// <param name="table">The table.</param>
    /// <param name="designTime">Whether the annotations are requested for migrations.</param>
    /// <returns>The annotations for the table.</returns>
    public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
    {
        foreach (var annotation in base.For(table, designTime))
            yield return annotation;

        // Only migrations need the column; the runtime relational model stays free of it
        if (!designTime)
            yield break;

        // The convention annotates root types only; with table splitting the annotated type need
        // not be the first mapping (TenantTableMappingValidator ensures that they agree)
        var column = table.EntityTypeMappings
            .Select(m => m.TypeBase.FindAnnotation(SqlServerRlsAnnotationNames.TenantColumn))
            .FirstOrDefault(a => a is not null);
        if (column is not null)
            yield return column;
    }
}
