using Acme.EntityFrameworkCore.Tenancy.Diagnostics;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// The RLS model convention: which entity types get policies and an index, how explicit
/// configuration and opt-outs interact with it, and the opt-out warning.
/// </summary>
public class ConventionTests
{
    private static RlsPolicy Policy(IReadOnlyEntityType entityType, string name = "tenant_isolation")
        => RlsPolicy.Deserialize(entityType.GetRlsPolicies()[name]);

    [Fact]
    public void Tenant_owned_entities_get_rls_policy_and_index()
    {
        using var ctx = Contexts.Create();
        var order = ctx.DesignModel().FindEntityType(typeof(Order))!;

        Assert.True(order.IsRlsEnabled());
        Assert.True(order.IsRlsForced());
        Assert.Contains("current_setting('app.tenant_id', true)", Policy(order).Using);
        Assert.Contains(order.GetIndexes(), i => i.Properties[0].Name == nameof(Order.TenantId));
    }

    [Fact]
    public void Entities_without_marker_are_ignored()
    {
        using var ctx = Contexts.Create();
        var product = ctx.DesignModel().FindEntityType(typeof(Product))!;

        Assert.False(product.HasRlsConfiguration());
    }

    [Fact]
    public void Final_column_name_is_used()
    {
        using var ctx = Contexts.Create(
            model: m => m.Entity<Order>().Property(o => o.TenantId).HasColumnName("tenant_id"));

        Assert.StartsWith("tenant_id = ", Policy(ctx.DesignModel().FindEntityType(typeof(Order))!).Using);
    }

    [Fact]
    public void Explicit_policy_with_same_name_wins_over_convention()
    {
        using var ctx = Contexts.Create(
            model: m => m.Entity<Order>().HasPolicy("tenant_isolation", p => p.Using("true")));

        Assert.Equal("true", Policy(ctx.DesignModel().FindEntityType(typeof(Order))!).Using);
    }

    [Fact]
    public void Data_annotation_opts_out()
    {
        using var ctx = Contexts.Create();
        var audit = ctx.DesignModel().FindEntityType(typeof(AuditEntry))!;

        Assert.True(audit.IsRlsOptedOut());
        Assert.Empty(audit.GetRlsPolicies());
    }

    [Fact]
    public void Fluent_api_overrides_data_annotation()
    {
        using var ctx = Contexts.Create(model: m => m.Entity<AuditEntry>().HasRowLevelSecurity());
        var audit = ctx.DesignModel().FindEntityType(typeof(AuditEntry))!;

        Assert.True(audit.IsRlsEnabled());
        Assert.NotEmpty(audit.GetRlsPolicies());
    }

    [Fact]
    public void Opt_out_warning_can_be_escalated_to_an_error()
    {
        using var ctx = Contexts.Create(configure: o => o.ConfigureWarnings(
            w => w.Throw(TenancyEventId.TenantIsolationOptedOut)));

        // ConfigureWarnings(Throw) turns the logged warning into an exception while the
        // model is being built, i.e. on the first access to ctx.Model.
        var ex = Assert.Throws<InvalidOperationException>(() => ctx.Model);
        Assert.Contains("TenancyEventId.TenantIsolationOptedOut", ex.Message);
    }

    [Fact]
    public void Index_is_not_created_when_disabled()
    {
        using var ctx = Contexts.Create(t => t.CreateTenantIndex(false));
        var order = ctx.DesignModel().FindEntityType(typeof(Order))!;

        Assert.DoesNotContain(order.GetIndexes(), i => i.Properties[0].Name == nameof(Order.TenantId));
    }
    // ------------------------------------------------ FORCE: Fluent API beats the convention

    [Fact]
    public void Explicit_force_false_beats_the_convention_default_true()
    {
        using var ctx = Contexts.Create(model: m => m.Entity<Order>().HasRowLevelSecurity(force: false));
        var order = ctx.DesignModel().FindEntityType(typeof(Order))!;

        Assert.False(order.IsRlsForced());
        Assert.True(order.IsRlsEnabled());
        Assert.DoesNotContain("ALTER TABLE \"Orders\" FORCE ROW LEVEL SECURITY;",
            Contexts.MigrationSql(null, ctx));
        Assert.Contains("ALTER TABLE \"Customers\" FORCE ROW LEVEL SECURITY;",
            Contexts.MigrationSql(null, ctx));
    }

    [Fact]
    public void Explicit_force_true_beats_the_convention_default_false()
    {
        using var ctx = Contexts.Create(t => t.ForceRowLevelSecurity(false),
            m => m.Entity<Order>().HasRowLevelSecurity(force: true));

        Assert.True(ctx.DesignModel().FindEntityType(typeof(Order))!.IsRlsForced());
        Assert.Contains("ALTER TABLE \"Orders\" FORCE ROW LEVEL SECURITY;", Contexts.MigrationSql(null, ctx));
        Assert.DoesNotContain("ALTER TABLE \"Customers\" FORCE ROW LEVEL SECURITY;",
            Contexts.MigrationSql(null, ctx));
    }

    [Fact]
    public void Unchanged_explicit_force_false_produces_no_differences()
    {
        using var source = Contexts.Create(model: m => m.Entity<Order>().HasRowLevelSecurity(force: false));
        using var target = Contexts.Create(model: m => m.Entity<Order>().HasRowLevelSecurity(force: false));

        Assert.Empty(Contexts.Diff(source, target));
    }

    [Fact]
    public void Explicit_false_and_missing_force_are_the_same_table_for_migrations()
    {
        // Normalized by the annotation provider: switching from the option to the Fluent API
        // must not produce an empty AlterTable migration
        using var source = Contexts.Create(t => t.ForceRowLevelSecurity(false));
        using var target = Contexts.Create(t => t.ForceRowLevelSecurity(false),
            m => m.Entity<Order>().HasRowLevelSecurity(force: false));

        Assert.Empty(Contexts.Diff(source, target));
    }

    [Fact]
    public void Switching_force_off_explicitly_migrates_to_no_force()
    {
        using var source = Contexts.Create();
        using var target = Contexts.Create(model: m => m.Entity<Order>().HasRowLevelSecurity(force: false));

        Assert.Equal(["ALTER TABLE \"Orders\" NO FORCE ROW LEVEL SECURITY;"], Contexts.MigrationSql(source, target));
    }
}
