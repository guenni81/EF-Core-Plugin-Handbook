using System.Reflection;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Query.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Query;

/// <summary>EF.Functions.Is[Not]DistinctFrom(a, b) -&gt; DistinctFromExpression.</summary>
/// <remarks>
/// Produces the custom <see cref="DistinctFromExpression"/> node, because the operator cannot be
/// expressed with EF's built-in SQL expression types (see chapter 37 of the handbook).
/// </remarks>
public class DistinctFromTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo IsNotDistinctFromMethod =
        typeof(TenancyPgDbFunctionsExtensions).GetMethod(nameof(TenancyPgDbFunctionsExtensions.IsNotDistinctFrom))!;

    private static readonly MethodInfo IsDistinctFromMethod =
        typeof(TenancyPgDbFunctionsExtensions).GetMethod(nameof(TenancyPgDbFunctionsExtensions.IsDistinctFrom))!;

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Creates the translator.</summary>
    /// <param name="sql">The factory used to apply type mappings.</param>
    /// <param name="typeMappingSource">Resolves fallback and <c>bool</c> type mappings.</param>
    public DistinctFromTranslator(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappingSource)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(typeMappingSource);

        _sql = sql;
        _typeMappingSource = typeMappingSource;
    }

    /// <summary>
    /// Translates <c>IsNotDistinctFrom</c>/<c>IsDistinctFrom</c>; returns <see langword="null"/>
    /// for every other method so that the next translator gets its turn.
    /// </summary>
    /// <returns>The translated expression, or <see langword="null"/> if not responsible.</returns>
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // Generic methods arrive closed (IsDistinctFrom<string>): compare the open definition
        if (!method.IsGenericMethod)
            return null;

        var definition = method.GetGenericMethodDefinition();
        if (definition != IsNotDistinctFromMethod && definition != IsDistinctFromMethod)
            return null;

        // arguments[0] is the DbFunctions parameter. Both sides get the same type mapping
        // (inferred from the side that has one, e.g. a column), so a constant on the other
        // side becomes the matching literal.
        var left = arguments[1];
        var right = arguments[2];
        var typeMapping = ExpressionExtensions.InferTypeMapping(left, right)
                          ?? _typeMappingSource.FindMapping(left.Type);

        return new DistinctFromExpression(
            _sql.ApplyTypeMapping(left, typeMapping)!,
            _sql.ApplyTypeMapping(right, typeMapping)!,
            negated: definition == IsDistinctFromMethod,
            _typeMappingSource.FindMapping(typeof(bool)));
    }
}
