namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata;

/// <summary>Design-time annotations of the SQL Server package (own prefix).</summary>
public static class SqlServerRlsAnnotationNames
{
    /// <summary>Common prefix, so the annotations cannot collide with EF's or other packages'.</summary>
    public const string Prefix = "Acme:SqlServerRls:";

    /// <summary>On the entity type/table: name of the tenant column (= RLS active).</summary>
    public const string TenantColumn = Prefix + "TenantColumn";

    /// <summary>
    /// On the model: the session key the predicate function reads. Recorded in the snapshot, so
    /// that a migration uses the value of its own model, not the application's current options.
    /// </summary>
    public const string SessionKey = Prefix + "SessionKey";

    /// <summary>On the model: the schema of the predicate function and the security policies.</summary>
    public const string PolicySchema = Prefix + "PolicySchema";
}
