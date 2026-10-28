namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// PostgreSQL-specific EF.Functions of the sample functions plugin: a custom SQL node and an
/// aggregate (chapters "Custom SQL expression nodes" and "Aggregate translators").
/// </summary>
/// <remarks>
/// The methods only serve as markers in LINQ expression trees and are never executed.
/// FunctionsEvaluatableExpressionFilterPlugin keeps EF from evaluating them on the client
/// (chapter "Preventing client-side evaluation"). Activated with
/// <c>UseNpgsql(cs, o =&gt; o.UseAcmeFunctions())</c>.
/// </remarks>
public static class AcmePgDbFunctionsExtensions
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
