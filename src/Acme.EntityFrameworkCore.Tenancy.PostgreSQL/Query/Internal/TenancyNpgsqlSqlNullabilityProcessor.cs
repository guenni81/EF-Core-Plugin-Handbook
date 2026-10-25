using System.Linq.Expressions;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Query.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Query.Internal;

/// <summary>
/// Teaches the nullability processor about the plugin's own node. Without this override, EF
/// throws an InvalidOperationException for unknown nodes.
/// </summary>
/// <remarks>
/// Derives from Npgsql's internal processor (namespace <c>...Query.Internal</c>, warning EF1001):
/// the base class may change in any Npgsql release, so re-check it on every upgrade.
/// </remarks>
public class TenancyNpgsqlSqlNullabilityProcessor : NpgsqlSqlNullabilityProcessor
{
    /// <summary>Creates the processor; parameters are passed through to Npgsql's processor.</summary>
    /// <param name="dependencies">EF's parameter object for parameter-based SQL processors.</param>
    /// <param name="parameters">Per-query parameters (e.g. whether relational nulls are used).</param>
    public TenancyNpgsqlSqlNullabilityProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters)
        : base(dependencies, parameters)
    {
    }

    /// <summary>
    /// Handles <see cref="DistinctFromExpression"/> (never nullable) and delegates all other
    /// custom nodes to Npgsql.
    /// </summary>
    protected override SqlExpression VisitCustomSqlExpression(
        SqlExpression sqlExpression,
        bool allowOptimizedExpansion,
        out bool nullable)
    {
        if (sqlExpression is DistinctFromExpression distinctFrom)
        {
            // Visit the children anyway: they can be optimized themselves. Their
            // nullability does not matter for the result.
            var left = Visit(distinctFrom.Left, out _)!;
            var right = Visit(distinctFrom.Right, out _)!;

            // IS [NOT] DISTINCT FROM is never NULL
            nullable = false;
            return distinctFrom.Update(left, right);
        }

        return base.VisitCustomSqlExpression(sqlExpression, allowOptimizedExpansion, out nullable);
    }
}

/// <summary>Creates the plugin's own nullability processor.</summary>
/// <remarks>
/// The processor is not a service of its own: it is created inside this class, so the whole
/// chain (factory -&gt; processor -&gt; nullability processor) has to be replaced. Derives from
/// Npgsql's internal processor (EF1001).
/// </remarks>
public class TenancyNpgsqlParameterBasedSqlProcessor : NpgsqlParameterBasedSqlProcessor
{
    /// <summary>Creates the processor; parameters are passed through to Npgsql's processor.</summary>
    /// <param name="dependencies">EF's parameter object for parameter-based SQL processors.</param>
    /// <param name="parameters">Per-query parameters (e.g. whether relational nulls are used).</param>
    public TenancyNpgsqlParameterBasedSqlProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters)
        : base(dependencies, parameters)
    {
    }

    /// <summary>Runs <see cref="TenancyNpgsqlSqlNullabilityProcessor"/> instead of Npgsql's.</summary>
    protected override Expression ProcessSqlNullability(
        Expression selectExpression, ParametersCacheDecorator parametersDecorator)
        => new TenancyNpgsqlSqlNullabilityProcessor(Dependencies, Parameters)
            .Process(selectExpression, parametersDecorator);
}

/// <summary>Replaces the Npgsql factory (single service, scoped).</summary>
/// <remarks>
/// A single service has exactly one implementation, so only one plugin can replace it.
/// See chapter "Custom SQL expression nodes" of the handbook for the trade-offs of custom nodes.
/// </remarks>
public class TenancyNpgsqlParameterBasedSqlProcessorFactory : IRelationalParameterBasedSqlProcessorFactory
{
    private readonly RelationalParameterBasedSqlProcessorDependencies _dependencies;

    /// <summary>Creates the factory.</summary>
    /// <param name="dependencies">EF's parameter object, handed to every processor created.</param>
    public TenancyNpgsqlParameterBasedSqlProcessorFactory(
        RelationalParameterBasedSqlProcessorDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        _dependencies = dependencies;
    }

    /// <summary>Creates a <see cref="TenancyNpgsqlParameterBasedSqlProcessor"/>.</summary>
    /// <param name="parameters">Per-query parameters supplied by EF.</param>
    /// <returns>The processor for one query compilation.</returns>
    public virtual RelationalParameterBasedSqlProcessor Create(
        RelationalParameterBasedSqlProcessorParameters parameters)
        => new TenancyNpgsqlParameterBasedSqlProcessor(_dependencies, parameters);
}
