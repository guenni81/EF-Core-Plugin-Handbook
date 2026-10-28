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
    public void Inequality_with_CurrentTenant_never_matches_without_a_tenant()
    {
        // CurrentTenant() is not nullable for EF (CLR type TenantId). Without a tenant the SQL
        // value is NULL and "<>" is not true: no rows instead of all rows wherever row level
        // security does not apply (chapter "Translating methods and members")
        using var ctx = Contexts.Create();

        var sql = ctx.Orders.Where(o => o.TenantId != EF.Functions.CurrentTenant()).ToQueryString();

        Assert.EndsWith("WHERE o.\"TenantId\" <> nullif(current_setting('app.tenant_id', TRUE), '')::uuid", sql);
        Assert.DoesNotContain("IS NULL", sql);
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

    private const string SoftDelete = "NOT (o.\"IsDeleted\")";

    [Fact]
    public void Named_filters_combine_with_soft_delete_and_can_be_switched_off_one_by_one()
    {
        // The application's own soft delete filter next to the plugin's tenant filter (EF 10
        // named query filters; chapter "Conventions")
        using var ctx = Contexts.Create(t => t.ApplyQueryFilter(), model: m =>
        {
            m.Entity<Order>().Property<bool>("IsDeleted");
            m.Entity<Order>().HasQueryFilter("SoftDelete", o => !EF.Property<bool>(o, "IsDeleted"));
        });

        var both = ctx.Orders.ToQueryString();
        Assert.Contains("current_setting(", both);
        Assert.Contains(SoftDelete, both);

        // Deleted rows become visible, the tenant filter stays
        var withDeleted = ctx.Orders.IgnoreQueryFilters(["SoftDelete"]).ToQueryString();
        Assert.Contains("current_setting(", withDeleted);
        Assert.DoesNotContain(SoftDelete, withDeleted);

        // Only the tenant filter is off (with RLS the database still filters)
        var allTenants = ctx.Orders.IgnoreQueryFilters(["Acme.Tenancy"]).ToQueryString();
        Assert.DoesNotContain("current_setting(", allTenants);
        Assert.Contains(SoftDelete, allTenants);
    }
}
