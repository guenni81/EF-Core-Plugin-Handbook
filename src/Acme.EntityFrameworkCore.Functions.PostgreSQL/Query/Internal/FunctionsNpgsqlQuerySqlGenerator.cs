using System.Linq.Expressions;
using Acme.EntityFrameworkCore.Functions.PostgreSQL.Query.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;

namespace Acme.EntityFrameworkCore.Functions.PostgreSQL.Query.Internal;

/// <summary>Writes the plugin's own expression nodes as SQL.</summary>
/// <remarks>
/// Without this override the base generator would merely "walk through" the unknown node and
/// write NO SQL: a silent failure with broken SQL. Derives from Npgsql's internal generator
/// (namespace <c>...Query.Internal</c>, warning EF1001); re-check it on every Npgsql upgrade.
/// </remarks>
public class FunctionsNpgsqlQuerySqlGenerator : NpgsqlQuerySqlGenerator
{
    /// <summary>Creates the generator; parameters are passed through to Npgsql's generator.</summary>
    /// <param name="dependencies">EF's parameter object for SQL generators.</param>
    /// <param name="typeMappingSource">The provider's type mapping source.</param>
    /// <param name="reverseNullOrderingEnabled">Npgsql option for NULL ordering.</param>
    /// <param name="postgresVersion">The targeted PostgreSQL version.</param>
    public FunctionsNpgsqlQuerySqlGenerator(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        bool reverseNullOrderingEnabled,
        Version postgresVersion)
        : base(dependencies, typeMappingSource, reverseNullOrderingEnabled, postgresVersion)
    {
    }

    /// <summary>Dispatches the plugin's nodes; everything else goes to Npgsql.</summary>
    protected override Expression VisitExtension(Expression extensionExpression)
        => extensionExpression switch
        {
            DistinctFromExpression e => VisitDistinctFrom(e),
            _ => base.VisitExtension(extensionExpression),
        };

    /// <summary>Writes <c>left IS [NOT] DISTINCT FROM right</c>.</summary>
    /// <param name="expression">The node to write.</param>
    /// <returns>The unchanged node.</returns>
    protected virtual Expression VisitDistinctFrom(DistinctFromExpression expression)
    {
        VisitOperand(expression.Left);
        Sql.Append(expression.IsNegated ? " IS DISTINCT FROM " : " IS NOT DISTINCT FROM ");
        VisitOperand(expression.Right);
        return expression;
    }

    // Simple operands without parentheses, everything else parenthesized (operator precedence)
    private void VisitOperand(SqlExpression operand)
    {
        var simple = operand is ColumnExpression or SqlConstantExpression
            or SqlParameterExpression or SqlFunctionExpression;

        if (!simple) Sql.Append("(");
        Visit(operand);
        if (!simple) Sql.Append(")");
    }

    /// <summary>
    /// Parenthesizes the plugin's nodes as operands of other operators. EF does not know their
    /// precedence; PostgreSQL binds <c>=</c> and <c>&lt;&gt;</c> tighter than
    /// <c>IS DISTINCT FROM</c>, so <c>a IS DISTINCT FROM b &lt;&gt; c</c> would compare b and c.
    /// </summary>
    protected override bool RequiresParentheses(SqlExpression outerExpression, SqlExpression innerExpression)
        => innerExpression is DistinctFromExpression
           || base.RequiresParentheses(outerExpression, innerExpression);
}

/// <summary>Replaces the Npgsql factory (single service, singleton).</summary>
/// <remarks>
/// Takes the same dependencies as Npgsql's factory. <see cref="INpgsqlSingletonOptions"/> is an
/// internal Npgsql API (EF1001).
/// </remarks>
public class FunctionsNpgsqlQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly INpgsqlSingletonOptions _npgsqlOptions;

    /// <summary>Creates the factory.</summary>
    /// <param name="dependencies">EF's parameter object for SQL generators.</param>
    /// <param name="typeMappingSource">The provider's type mapping source.</param>
    /// <param name="npgsqlOptions">Npgsql's singleton options (NULL ordering, version).</param>
    public FunctionsNpgsqlQuerySqlGeneratorFactory(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        INpgsqlSingletonOptions npgsqlOptions)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(typeMappingSource);
        ArgumentNullException.ThrowIfNull(npgsqlOptions);

        _dependencies = dependencies;
        _typeMappingSource = typeMappingSource;
        _npgsqlOptions = npgsqlOptions;
    }

    /// <summary>Creates a new generator; generators hold state and are not reused.</summary>
    /// <returns>A <see cref="FunctionsNpgsqlQuerySqlGenerator"/>.</returns>
    public virtual QuerySqlGenerator Create()
        => new FunctionsNpgsqlQuerySqlGenerator(
            _dependencies,
            _typeMappingSource,
            _npgsqlOptions.ReverseNullOrderingEnabled,
            _npgsqlOptions.PostgresVersion);
}
