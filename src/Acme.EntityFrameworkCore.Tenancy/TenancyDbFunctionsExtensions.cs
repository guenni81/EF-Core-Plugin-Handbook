using Acme.EntityFrameworkCore.Tenancy;

// In the EF namespace: EF.Functions.CurrentTenant() is discoverable without an extra using
namespace Microsoft.EntityFrameworkCore;

/// <summary>Database functions for LINQ queries (EF.Functions.*).</summary>
[TenancyDbFunctions]
public static class TenancyDbFunctionsExtensions
{
    /// <summary>
    /// Tenant of the current database session. Translated to SQL by the provider package
    /// (PostgreSQL: current_setting(...)) and never executed in .NET code.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance (EF.Functions).</param>
    /// <returns>The session tenant, as evaluated by the database.</returns>
    /// <exception cref="InvalidOperationException">Always, when called outside a query.</exception>
    public static TenantId CurrentTenant(this DbFunctions _)
        => throw new InvalidOperationException(
            $"{nameof(CurrentTenant)} can only be used in LINQ to Entities queries.");
}
