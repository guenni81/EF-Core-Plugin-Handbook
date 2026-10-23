using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// Query translation of <c>EF.Functions.CurrentTenant()</c>, <c>TenantId.IsEmpty</c> and the
/// optional query filter. <c>ToQueryString</c> produces SQL without a database connection.
/// </summary>
public class QueryTranslationTests
{
    [Fact]
    public void CurrentTenant_is_translated_and_not_evaluated_on_the_client()
    {
        // Without the IEvaluatableExpressionFilterPlugin, EF would evaluate the method up front
        // in .NET (parameter extraction) and hit the InvalidOperationException in CurrentTenant().
        using var ctx = Contexts.Create();

        var sql = ctx.Orders.Where(o => o.TenantId == EF.Functions.CurrentTenant()).ToQueryString();

        Assert.Contains("current_setting('app.tenant_id', TRUE)", sql);
        Assert.Contains("nullif(", sql);
    }

    [Fact]
    public void CurrentTenant_uses_configured_setting_name()
    {
        using var ctx = Contexts.Create(t => t.UseSettingName("app.mandant"));

        var sql = ctx.Orders.Where(o => o.TenantId == EF.Functions.CurrentTenant()).ToQueryString();

        Assert.Contains("current_setting('app.mandant', TRUE)", sql);
    }

    [Fact]
    public void IsEmpty_member_is_translated()
    {
        using var ctx = Contexts.Create();

        var sql = ctx.Orders.Where(o => o.TenantId.IsEmpty).ToQueryString();

        Assert.Contains("'00000000-0000-0000-0000-000000000000'::uuid", sql);
    }

    [Fact]
    public void Query_filter_is_applied_when_enabled()
    {
        using var ctx = Contexts.Create(t => t.ApplyQueryFilter());

        Assert.Contains("current_setting(", ctx.Orders.ToQueryString());
        Assert.DoesNotContain("current_setting(", ctx.Products.ToQueryString());
    }

    [Fact]
    public void Query_filter_is_not_applied_by_default()
    {
        using var ctx = Contexts.Create();

        Assert.DoesNotContain("current_setting(", ctx.Orders.ToQueryString());
    }
}
