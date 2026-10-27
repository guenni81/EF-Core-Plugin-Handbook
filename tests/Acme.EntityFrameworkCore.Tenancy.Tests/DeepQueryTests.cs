using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// Custom SQL expression node and aggregate translator (chapters "Custom SQL expression nodes"
/// and "Aggregate translators").
/// </summary>
public class DeepQueryTests
{
    [Fact]
    public void IsNotDistinctFrom_is_written_by_custom_sql_generator()
    {
        using var ctx = Contexts.Create();

        var sql = ctx.Customers
            .Where(c => EF.Functions.IsNotDistinctFrom(c.Name, "Contoso"))
            .ToQueryString();

        Assert.Contains("IS NOT DISTINCT FROM", sql);
    }

    [Fact]
    public void IsDistinctFrom_in_projection_passes_nullability_processor()
    {
        // In the projection the nullability processor has to understand the node;
        // without a custom processor EF would throw here.
        using var ctx = Contexts.Create();

        var sql = ctx.Customers
            .Select(c => EF.Functions.IsDistinctFrom(c.Name, (string?)null))
            .ToQueryString();

        Assert.Contains("IS DISTINCT FROM", sql);
    }

    [Fact]
    public void IsDistinctFrom_as_operand_of_a_comparison_is_parenthesized()
    {
        // PostgreSQL binds <> tighter than IS DISTINCT FROM: without parentheses the SQL
        // would compare 'x' with the column
        using var ctx = Contexts.Create();

        var sql = ctx.Customers
            .Where(c => EF.Functions.IsDistinctFrom(c.Name, "x") != EF.Functions.IsDistinctFrom(c.Name, "y"))
            .ToQueryString();

        Assert.Contains(
            "WHERE (c.\"Name\" IS DISTINCT FROM 'x') <> (c.\"Name\" IS DISTINCT FROM 'y')", sql);
    }

    [Fact]
    public void Every_translates_to_bool_and_with_coalesce()
    {
        using var ctx = Contexts.Create();

        var sql = ctx.Orders
            .GroupBy(o => o.TenantId)
            .Select(g => EF.Functions.Every(g.Select(o => o.Total > 0)))
            .ToQueryString();

        Assert.Contains("bool_and(", sql);
        Assert.Contains("COALESCE(", sql);
    }

    [Fact]
    public void Every_with_predicate_uses_aggregate_filter()
    {
        using var ctx = Contexts.Create();

        var sql = ctx.Orders
            .GroupBy(o => o.TenantId)
            .Select(g => EF.Functions.Every(g.Where(o => o.Id > 10).Select(o => o.Total > 0)))
            .ToQueryString();

        Assert.Contains("FILTER (WHERE", sql);
    }

}
