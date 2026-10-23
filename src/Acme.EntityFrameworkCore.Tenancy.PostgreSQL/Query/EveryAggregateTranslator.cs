using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Query;

/// <summary>
/// Aggregate: EF.Functions.Every(group.Select(x =&gt; x.Flag)) -&gt; COALESCE(bool_and(...), TRUE).
/// </summary>
/// <remarks>
/// Instead of a single argument, aggregate translators receive an
/// <see cref="EnumerableExpression"/>: the selector plus optional DISTINCT, a predicate
/// (from a Where before the aggregate) and orderings. Npgsql's AggregateFunction renders these
/// as DISTINCT, FILTER (WHERE ...) and ORDER BY inside the aggregate.
/// </remarks>
public class EveryAggregateTranslator : IAggregateMethodCallTranslator
{
    private static readonly MethodInfo EveryMethod =
        typeof(TenancyPgDbFunctionsExtensions).GetMethod(nameof(TenancyPgDbFunctionsExtensions.Every))!;

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Creates the translator.</summary>
    /// <param name="sql">The factory; must be Npgsql's for the translation to apply.</param>
    /// <param name="typeMappingSource">Resolves the <c>boolean</c> type mapping.</param>
    public EveryAggregateTranslator(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappingSource)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(typeMappingSource);

        _sql = sql;
        _typeMappingSource = typeMappingSource;
    }

    /// <summary>
    /// Translates <c>EF.Functions.Every</c>; returns <see langword="null"/> for every other
    /// method so that the next translator gets its turn.
    /// </summary>
    /// <returns>The translated expression, or <see langword="null"/> if not responsible.</returns>
    public virtual SqlExpression? Translate(
        MethodInfo method,
        EnumerableExpression source,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method != EveryMethod || source.Selector is not SqlExpression selector)
            return null;

        // Only Npgsql's factory supports aggregate predicates and orderings (FILTER, ORDER BY);
        // the base ISqlExpressionFactory has no AggregateFunction.
        if (_sql is not NpgsqlSqlExpressionFactory npgsql)
            return null;

        var boolean = _typeMappingSource.FindMapping(typeof(bool))!;

        // bool_and returns NULL for an empty set, Enumerable.All returns true:
        // COALESCE aligns the semantics (as Npgsql does for string_agg/string.Join).
        return _sql.Coalesce(
            npgsql.AggregateFunction(
                "bool_and",
                [selector],
                source,
                nullable: true,
                argumentsPropagateNullability: [false],
                typeof(bool),
                boolean),
            _sql.Constant(true, boolean),
            boolean);
    }
}
