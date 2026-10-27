namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata.Internal;

/// <summary>Names of the database objects the SQL Server package creates.</summary>
internal static class SqlServerTenancyNames
{
    /// <summary>Maximum length of an identifier in SQL Server.</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>
    /// Policy name of a table: <c>tenant_schema_Table</c>. Until 1.0 it was <c>tenant_Table</c>,
    /// which made tables with the same name in different schemas collide.
    /// </summary>
    public static string PolicyName(string schema, string table) => $"tenant_{schema}_{table}";
}
