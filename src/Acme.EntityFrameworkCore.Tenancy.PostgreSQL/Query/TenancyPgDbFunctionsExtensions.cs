using Acme.EntityFrameworkCore.Tenancy;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// PostgreSQL-specific EF.Functions of the plugin. They live in the provider package because
/// there is no meaningful translation for other databases.
/// </summary>
/// <remarks>
/// The methods only serve as markers in LINQ expression trees and are never executed. The
/// <see cref="TenancyDbFunctionsAttribute"/> keeps EF from evaluating them on the client
/// (see chapter 13 of the handbook).
/// </remarks>
[TenancyDbFunctions]
public static class TenancyPgDbFunctionsExtensions
{
    /// <summary>
    /// Null-safe comparison: <c>a IS NOT DISTINCT FROM b</c>. Returns true if both values are
    /// equal or both are NULL, and is never NULL itself.
    /// </summary>
    /// <typeparam name="T">The type of the compared values.</typeparam>
    /// <param name="_">The <see cref="DbFunctions"/> instance (<c>EF.Functions</c>).</param>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>Never returns; translated to SQL.</returns>
    /// <exception cref="InvalidOperationException">Always, when called outside a query.</exception>
    public static bool IsNotDistinctFrom<T>(this DbFunctions _, T a, T b)
        => throw NotInLinq(nameof(IsNotDistinctFrom));

    /// <summary>Negation: <c>a IS DISTINCT FROM b</c>.</summary>
    /// <typeparam name="T">The type of the compared values.</typeparam>
    /// <param name="_">The <see cref="DbFunctions"/> instance (<c>EF.Functions</c>).</param>
    /// <param name="a">The left operand.</param>
    /// <param name="b">The right operand.</param>
    /// <returns>Never returns; translated to SQL.</returns>
    /// <exception cref="InvalidOperationException">Always, when called outside a query.</exception>
    public static bool IsDistinctFrom<T>(this DbFunctions _, T a, T b)
        => throw NotInLinq(nameof(IsDistinctFrom));

    /// <summary>
    /// Aggregate: true if all values are true (PostgreSQL <c>bool_and</c>). Like
    /// <c>Enumerable.All</c>, it returns true for an empty set.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance (<c>EF.Functions</c>).</param>
    /// <param name="values">The aggregated values, typically <c>group.Select(x =&gt; x.Flag)</c>.</param>
    /// <returns>Never returns; translated to SQL.</returns>
    /// <exception cref="InvalidOperationException">Always, when called outside a query.</exception>
    public static bool Every(this DbFunctions _, IEnumerable<bool> values)
        => throw NotInLinq(nameof(Every));

    private static InvalidOperationException NotInLinq(string name)
        => new($"{name} can only be used in LINQ to Entities queries.");
}
