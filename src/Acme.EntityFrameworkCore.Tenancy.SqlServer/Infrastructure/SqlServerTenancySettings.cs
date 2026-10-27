using System.Text.RegularExpressions;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;

/// <summary>
/// Model-relevant settings; as a record (value equality) directly usable in the service
/// provider hash.
/// </summary>
public sealed partial record SqlServerTenancySettings
{
    /// <summary>The defaults: key <c>TenantId</c>, schema <c>acme</c>.</summary>
    public static SqlServerTenancySettings Default { get; } = new();

    /// <summary>Key in SESSION_CONTEXT that holds the current tenant ID.</summary>
    public string SessionKey { get; init; } = "TenantId";

    /// <summary>Schema for the predicate function and the security policies.</summary>
    public string PolicySchema { get; init; } = "acme";

    /// <summary>
    /// Check every materialized tenant-owned entity against the current tenant (defense in
    /// depth, off by default). Registers a singleton materialization interceptor.
    /// </summary>
    public bool VerifyMaterializedTenants { get; init; }

    // Stricter than SQL Server's identifier rules (only the 128-character limit matches): no
    // quotes, brackets or spaces that could break out of the nested EXEC(N'...') literals
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex IdentifierRegex();

    /// <summary>Both values end up in generated SQL and are therefore validated.</summary>
    internal static string Validate(string value, string what)
        => IdentifierRegex().IsMatch(value)
            ? value
            : throw new ArgumentException($"Invalid {what} '{value}'.", nameof(value));
}
