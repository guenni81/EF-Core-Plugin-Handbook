namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata;

/// <summary>Design-time annotations of the SQL Server package (own prefix).</summary>
public static class SqlServerRlsAnnotationNames
{
    /// <summary>Common prefix, so the annotations cannot collide with EF's or other packages'.</summary>
    public const string Prefix = "Acme:SqlServerRls:";

    /// <summary>On the entity type/table: name of the tenant column (= RLS active).</summary>
    public const string TenantColumn = Prefix + "TenantColumn";
}
