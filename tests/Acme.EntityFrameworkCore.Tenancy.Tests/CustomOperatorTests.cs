using System.Linq.Expressions;
using System.Reflection;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Query.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using ExpressionExtensions = Microsoft.EntityFrameworkCore.Query.ExpressionExtensions;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

// Practical example of chapter "Custom SQL expression nodes": the pgvector cosine distance
// operator <=> as a node of its own. Test code only: a real application uses
// Pgvector.EntityFrameworkCore, which brings the vector type mapping and these translations.

/// <summary>EF.Functions.CosineDistance(a, b): only translatable, never executed in .NET.</summary>
public static class VectorDbFunctionsExtensions
{
    /// <summary>Cosine distance of two vectors, <c>a &lt;=&gt; b</c> in SQL.</summary>
    public static double CosineDistance(this DbFunctions _, float[] a, float[] b)
        => throw new InvalidOperationException("CosineDistance can only be used in LINQ queries.");
}

/// <summary>
/// A binary operator that EF's <see cref="SqlBinaryExpression"/> cannot express: its operator
/// is an <see cref="ExpressionType"/>, and there is none for <c>&lt;=&gt;</c>.
/// </summary>
public class VectorDistanceExpression : SqlExpression
{
    private static ConstructorInfo? _quotingConstructor;

    public VectorDistanceExpression(SqlExpression left, SqlExpression right, string @operator,
        RelationalTypeMapping? typeMapping)
        : base(typeof(double), typeMapping)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentException.ThrowIfNullOrEmpty(@operator);

        Left = left;
        Right = right;
        Operator = @operator;
    }

    public virtual SqlExpression Left { get; }
    public virtual SqlExpression Right { get; }

    /// <summary><c>&lt;=&gt;</c> (cosine), <c>&lt;-&gt;</c> (L2) or <c>&lt;#&gt;</c> (inner product).</summary>
    public virtual string Operator { get; }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update((SqlExpression)visitor.Visit(Left), (SqlExpression)visitor.Visit(Right));

    public virtual VectorDistanceExpression Update(SqlExpression left, SqlExpression right)
        => left == Left && right == Right
            ? this
            : new VectorDistanceExpression(left, right, Operator, TypeMapping);

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(VectorDistanceExpression).GetConstructor(
                [typeof(SqlExpression), typeof(SqlExpression), typeof(string),
                 typeof(RelationalTypeMapping)])!,
            Left.Quote(), Right.Quote(), Constant(Operator),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    // Same parentheses as in SQL, so that DebugView shows what the database will parse
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        PrintOperand(expressionPrinter, Left);
        expressionPrinter.Append($" {Operator} ");
        PrintOperand(expressionPrinter, Right);
    }

    private static void PrintOperand(ExpressionPrinter expressionPrinter, SqlExpression operand)
    {
        var simple = IsSimple(operand);
        if (!simple) expressionPrinter.Append("(");
        expressionPrinter.Visit(operand);
        if (!simple) expressionPrinter.Append(")");
    }

    internal static bool IsSimple(SqlExpression operand)
        => operand is ColumnExpression or SqlConstantExpression or SqlParameterExpression
            or SqlFunctionExpression;

    public override bool Equals(object? obj)
        => obj is VectorDistanceExpression other
           && (ReferenceEquals(this, other)
               || base.Equals(other) && Left.Equals(other.Left)
                  && Right.Equals(other.Right) && Operator == other.Operator);

    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), Left, Right, Operator);
}

/// <summary>CosineDistance(a, b) -&gt; VectorDistanceExpression with <c>&lt;=&gt;</c>.</summary>
public class VectorDistanceTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo CosineDistanceMethod =
        typeof(VectorDbFunctionsExtensions).GetMethod(nameof(VectorDbFunctionsExtensions.CosineDistance))!;

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    public VectorDistanceTranslator(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappingSource)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(typeMappingSource);

        _sql = sql;
        _typeMappingSource = typeMappingSource;
    }

    public virtual SqlExpression? Translate(SqlExpression? instance, MethodInfo method,
        IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method != CosineDistanceMethod)
            return null;

        // Both operands get the column's mapping, so the parameter is sent with the column's
        // store type; the result is a double precision
        var left = arguments[1];                        // arguments[0] is DbFunctions
        var right = arguments[2];
        var operandMapping = ExpressionExtensions.InferTypeMapping(left, right)
                             ?? _typeMappingSource.FindMapping(left.Type);

        return new VectorDistanceExpression(
            _sql.ApplyTypeMapping(left, operandMapping)!,
            _sql.ApplyTypeMapping(right, operandMapping)!,
            "<=>",
            _typeMappingSource.FindMapping(typeof(double)));
    }
}

public class VectorTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public VectorTranslatorPlugin(ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappingSource)
        => Translators = [new VectorDistanceTranslator(sql, typeMappingSource)];

    public IEnumerable<IMethodCallTranslator> Translators { get; }
}

/// <summary>
/// Derives from the tenancy plugin's generator: both replace the same single service, so the
/// second one has to inherit from the first (chapter "Custom SQL expression nodes").
/// </summary>
public class VectorQuerySqlGenerator : TenancyNpgsqlQuerySqlGenerator
{
    public VectorQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        bool reverseNullOrderingEnabled, Version postgresVersion)
        : base(dependencies, typeMappingSource, reverseNullOrderingEnabled, postgresVersion)
    {
    }

    protected override Expression VisitExtension(Expression extensionExpression)
        => extensionExpression is VectorDistanceExpression e
            ? VisitVectorDistance(e)
            : base.VisitExtension(extensionExpression);

    protected virtual Expression VisitVectorDistance(VectorDistanceExpression expression)
    {
        VisitOperand(expression.Left);
        Sql.Append($" {expression.Operator} ");
        VisitOperand(expression.Right);
        return expression;
    }

    private void VisitOperand(SqlExpression operand)
    {
        var simple = VectorDistanceExpression.IsSimple(operand);
        if (!simple) Sql.Append("(");
        Visit(operand);
        if (!simple) Sql.Append(")");
    }

    // The other direction: the node as an operand of +, *, <. EF does not know the operator's
    // precedence and would write a <=> b * 2, which PostgreSQL reads as a <=> (b * 2)
    protected override bool RequiresParentheses(SqlExpression outerExpression, SqlExpression innerExpression)
        => innerExpression is VectorDistanceExpression
           || base.RequiresParentheses(outerExpression, innerExpression);
}

public class VectorQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly INpgsqlSingletonOptions _npgsqlOptions;

    public VectorQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource, INpgsqlSingletonOptions npgsqlOptions)
    {
        _dependencies = dependencies;
        _typeMappingSource = typeMappingSource;
        _npgsqlOptions = npgsqlOptions;
    }

    public QuerySqlGenerator Create()
        => new VectorQuerySqlGenerator(_dependencies, _typeMappingSource,
            _npgsqlOptions.ReverseNullOrderingEnabled, _npgsqlOptions.PostgresVersion);
}

public class VectorSqlNullabilityProcessor : TenancyNpgsqlSqlNullabilityProcessor
{
    public VectorSqlNullabilityProcessor(RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters)
        : base(dependencies, parameters)
    {
    }

    protected override SqlExpression VisitCustomSqlExpression(
        SqlExpression sqlExpression, bool allowOptimizedExpansion, out bool nullable)
    {
        if (sqlExpression is VectorDistanceExpression distance)
        {
            // Like any ordinary operator: NULL in, NULL out
            var left = Visit(distance.Left, out var leftNullable)!;
            var right = Visit(distance.Right, out var rightNullable)!;

            nullable = leftNullable || rightNullable;
            return distance.Update(left, right);
        }

        return base.VisitCustomSqlExpression(sqlExpression, allowOptimizedExpansion, out nullable);
    }
}

public class VectorParameterBasedSqlProcessor : TenancyNpgsqlParameterBasedSqlProcessor
{
    public VectorParameterBasedSqlProcessor(RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters)
        : base(dependencies, parameters)
    {
    }

    protected override Expression ProcessSqlNullability(
        Expression selectExpression, ParametersCacheDecorator parametersDecorator)
        => new VectorSqlNullabilityProcessor(Dependencies, Parameters)
            .Process(selectExpression, parametersDecorator);
}

public class VectorParameterBasedSqlProcessorFactory : IRelationalParameterBasedSqlProcessorFactory
{
    private readonly RelationalParameterBasedSqlProcessorDependencies _dependencies;

    public VectorParameterBasedSqlProcessorFactory(RelationalParameterBasedSqlProcessorDependencies dependencies)
        => _dependencies = dependencies;

    public RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters)
        => new VectorParameterBasedSqlProcessor(_dependencies, parameters);
}

public class EmbeddedDocument
{
    public int Id { get; set; }
    public float[] Embedding { get; set; } = [];
    public float[]? DraftEmbedding { get; set; }
}

public sealed class VectorContext : DbContext
{
    public VectorContext(DbContextOptions<VectorContext> options)
        : base(options)
    {
    }

    public DbSet<EmbeddedDocument> Documents => Set<EmbeddedDocument>();
}

/// <summary>
/// The pgvector cosine distance as a custom operator node (chapter "Custom SQL expression
/// nodes", practical example on vector search): translator, SQL generator and nullability
/// processor, checked with ToQueryString.
/// </summary>
public class CustomOperatorTests
{

    private static VectorContext Create(bool withNullabilityProcessor = true)
    {
        var builder = new DbContextOptionsBuilder<VectorContext>();
        builder
            .UseNpgsql("Host=localhost;Database=unused")    // no connection is ever opened
            .UseAcmeTenancy()
            .UseFakePlugin<VectorTranslatorPlugin>(services =>
                new EntityFrameworkRelationalServicesBuilder(services)
                    .TryAdd<IMethodCallTranslatorPlugin, VectorTranslatorPlugin>())
            .ReplaceService<IQuerySqlGeneratorFactory, VectorQuerySqlGeneratorFactory>();
        if (withNullabilityProcessor)
            builder.ReplaceService<IRelationalParameterBasedSqlProcessorFactory,
                VectorParameterBasedSqlProcessorFactory>();
        return new VectorContext(builder.Options);
    }

    [Fact]
    public void Nearest_neighbours_order_by_the_operator()
    {
        using var ctx = Create();
        var query = new[] { 0.1f, 0.2f, 0.3f };

        var sql = ctx.Documents
            .OrderBy(d => EF.Functions.CosineDistance(d.Embedding, query))
            .Take(5)
            .ToQueryString();

        Assert.Contains("ORDER BY d.\"Embedding\" <=> @query", sql);
    }

    [Fact]
    public void Operator_inside_a_comparison_is_parenthesized()
    {
        using var ctx = Create();
        var query = new[] { 0.1f, 0.2f, 0.3f };

        var sql = ctx.Documents
            .Where(d => EF.Functions.CosineDistance(d.Embedding, query) < 0.5)
            .ToQueryString();

        Assert.Contains("WHERE (d.\"Embedding\" <=> @query) < 0.5", sql);
    }

    [Fact]
    public void Operator_inside_arithmetic_is_parenthesized()
    {
        using var ctx = Create();
        var query = new[] { 0.1f, 0.2f, 0.3f };

        var sql = ctx.Documents
            .Select(d => EF.Functions.CosineDistance(d.Embedding, query) * 2 + 1)
            .ToQueryString();

        Assert.Contains("SELECT (d.\"Embedding\" <=> @query) * 2.0 + 1.0", sql);
    }

    [Fact]
    public void Nullable_operand_propagates_null_into_negated_comparisons()
    {
        using var ctx = Create();
        var query = new[] { 0.1f, 0.2f, 0.3f };

        // In C#, !(x < 0.5) is true for a NULL x; in SQL, NOT (NULL < 0.5) is NULL. EF only
        // writes the CASE for that because the processor reports the operand as nullable.
        var required = ctx.Documents
            .Where(d => !(EF.Functions.CosineDistance(d.Embedding, query) < 0.5))
            .ToQueryString();
        var nullable = ctx.Documents
            .Where(d => !(EF.Functions.CosineDistance(d.DraftEmbedding!, query) < 0.5))
            .ToQueryString();

        Assert.Contains("WHERE (d.\"Embedding\" <=> @query) >= 0.5", required);
        Assert.Contains("WHEN (d.\"DraftEmbedding\" <=> @query) < 0.5 THEN FALSE", nullable);
    }

    [Fact]
    public void Without_its_nullability_processor_the_node_fails_to_translate()
    {
        using var ctx = Create(withNullabilityProcessor: false);
        var query = new[] { 0.1f, 0.2f, 0.3f };

        Assert.Throws<InvalidOperationException>(() => ctx.Documents
            .Where(d => EF.Functions.CosineDistance(d.Embedding, query) < 0.5)
            .ToQueryString());
    }

    [Fact]
    public void Print_parenthesizes_like_the_sql()
    {
        using var ctx = Create();
        var query = new[] { 0.1f, 0.2f, 0.3f };
        var sql = ctx.GetService<ISqlExpressionFactory>();
        var column = sql.Constant(query);
        var sum = sql.Add(sql.Constant(1.0), sql.Constant(2.0));

        var printed = new ExpressionPrinter().PrintExpression(
            new VectorDistanceExpression(column, sum, "<->", null));

        Assert.EndsWith(" <-> (1 + 2)", printed);
    }
}
