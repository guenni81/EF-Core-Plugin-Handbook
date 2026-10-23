using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// SQL produced by the model differ and the plugin's migrations SQL generator: RLS on/off,
/// policy changes and the custom role operations.
/// </summary>
/// <remarks>
/// Npgsql only quotes identifiers that require it (upper-case letters, special characters,
/// keywords): <c>"Orders"</c> is quoted, <c>tenant_isolation</c> and <c>shop_app</c> are not.
/// The expected SQL below follows that rule.
/// </remarks>
public class MigrationSqlTests
{
    [Fact]
    public void Initial_migration_enables_rls_and_creates_policy()
    {
        using var ctx = Contexts.Create();

        var sql = Contexts.MigrationSql(null, ctx);

        Assert.Contains("ALTER TABLE \"Orders\" ENABLE ROW LEVEL SECURITY;", sql);
        Assert.Contains("ALTER TABLE \"Orders\" FORCE ROW LEVEL SECURITY;", sql);
        Assert.Contains(sql, s => s.StartsWith("CREATE POLICY tenant_isolation ON \"Orders\""));
        Assert.DoesNotContain(sql, s => s.Contains("ON \"Products\""));
        Assert.DoesNotContain(sql, s => s.Contains("ON \"AuditEntries\""));
    }

    [Fact]
    public void Unchanged_model_produces_no_operations()
        => Assert.Empty(Contexts.Diff(Contexts.Create(), Contexts.Create()));

    [Fact]
    public void Changed_setting_drops_and_recreates_policy()
    {
        using var before = Contexts.Create(t => t.UseSettingName("app.tenant_id"));
        using var after = Contexts.Create(t => t.UseSettingName("app.mandant_id"));

        // DROP + CREATE instead of ALTER POLICY: ALTER POLICY can change neither FOR nor
        // PERMISSIVE/RESTRICTIVE, so the generator always replaces a changed policy.
        var orders = Contexts.MigrationSql(before, after).Where(s => s.Contains("ON \"Orders\"")).ToList();

        Assert.Collection(orders,
            s => Assert.StartsWith("DROP POLICY tenant_isolation", s),
            s => Assert.StartsWith("CREATE POLICY tenant_isolation", s));
    }

    [Fact]
    public void Opt_out_disables_rls_and_drops_policy()
    {
        using var before = Contexts.Create();
        using var after = Contexts.Create(model: m => m.Entity<Order>().HasNoRowLevelSecurity());

        var sql = Contexts.MigrationSql(before, after);

        Assert.Contains("DROP POLICY tenant_isolation ON \"Orders\";", sql);
        Assert.Contains("ALTER TABLE \"Orders\" DISABLE ROW LEVEL SECURITY;", sql);
        Assert.Contains("ALTER TABLE \"Orders\" NO FORCE ROW LEVEL SECURITY;", sql);
    }

    [Fact]
    public void Custom_operations_generate_expected_sql()
    {
        using var ctx = Contexts.Create();

        var sql = Contexts.Sql(ctx,
        [
            new CreateApplicationRoleOperation { Name = "shop_app", CanLogin = true },
            new GrantTablePrivilegesOperation { Role = "shop_app" },
            new DropApplicationRoleOperation { Name = "shop_app" },
        ]);

        Assert.Contains(sql, s => s.Contains("WHERE rolname = 'shop_app'")
                                  && s.Contains("CREATE ROLE shop_app LOGIN NOSUPERUSER NOBYPASSRLS;"));
        Assert.Contains("GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO shop_app;", sql);
        Assert.Contains("DROP ROLE IF EXISTS shop_app;", sql);
    }

    [Fact]
    public void Migration_builder_validates_role_names()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        Assert.Throws<ArgumentException>(() => builder.CreateApplicationRole("x\"; DROP"));
    }
}
