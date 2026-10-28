using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

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

    private static readonly Action<ModelBuilder> SharedProducts = m => m.Entity<Product>();

    private static readonly Action<ModelBuilder> TenantProducts =
        m => m.Entity<Product>().Property<TenantId>("TenantId").IsTenantProperty();

    private static List<string> Products(IReadOnlyList<string> sql)
        => sql.Where(s => s.Contains("\"Products\"")).Select(s => s.Split(" (")[0].Split(" AS ")[0]).ToList();

    [Fact]
    public void Adding_tenant_column_and_isolation_creates_the_policy_after_the_column()
    {
        using var before = Contexts.Create(model: SharedProducts);
        using var after = Contexts.Create(model: TenantProducts);

        // EF's differ: AlterTable, AddColumn, CreateIndex. The policy names the new column.
        Assert.Collection(Products(Contexts.MigrationSql(before, after)),
            s => Assert.Equal("ALTER TABLE \"Products\" ENABLE ROW LEVEL SECURITY;", s),
            s => Assert.Equal("ALTER TABLE \"Products\" FORCE ROW LEVEL SECURITY;", s),
            s => Assert.StartsWith("ALTER TABLE \"Products\" ADD \"TenantId\"", s),
            s => Assert.Equal("CREATE POLICY tenant_isolation ON \"Products\"", s),
            s => Assert.StartsWith("CREATE INDEX", s));
    }

    [Fact]
    public void Removing_tenant_column_and_isolation_drops_the_policy_before_the_column()
    {
        using var before = Contexts.Create(model: TenantProducts);
        using var after = Contexts.Create(model: SharedProducts);

        // EF's differ: DropIndex, DropColumn, AlterTable. DROP COLUMN fails while a policy uses it.
        Assert.Collection(Products(Contexts.MigrationSql(before, after)),
            s => Assert.Equal("DROP POLICY tenant_isolation ON \"Products\";", s),
            s => Assert.Equal("ALTER TABLE \"Products\" DROP COLUMN \"TenantId\";", s),
            s => Assert.Equal("ALTER TABLE \"Products\" DISABLE ROW LEVEL SECURITY;", s),
            s => Assert.Equal("ALTER TABLE \"Products\" NO FORCE ROW LEVEL SECURITY;", s));
    }

    [Fact]
    public void Renamed_table_and_column_with_changed_setting_use_the_names_valid_at_each_statement()
    {
        using var before = Contexts.Create();
        using var after = Contexts.Create(t => t.UseSettingName("app.mandant_id"),
            m => m.Entity<Order>().ToTable("Bestellungen").Property(o => o.TenantId).HasColumnName("Mandant"));

        var sql = Contexts.MigrationSql(before, after);
        var policies = sql.Where(s => s.Contains("POLICY") && s.Contains("Bestellungen")).ToList();

        // Dropped before the column rename (after the table rename), created after it, once each
        Assert.Collection(policies,
            s => Assert.Equal("DROP POLICY tenant_isolation ON \"Bestellungen\";", s),
            s => Assert.Contains("(\"Mandant\" = nullif(current_setting('app.mandant_id', true)", s));
        Assert.True(sql.ToList().IndexOf(policies[0])
                    < sql.ToList().FindIndex(s => s.Contains("RENAME COLUMN \"TenantId\" TO \"Mandant\"")));
        Assert.True(sql.ToList().IndexOf(policies[1])
                    > sql.ToList().FindIndex(s => s.Contains("RENAME COLUMN \"TenantId\" TO \"Mandant\"")));
        Assert.DoesNotContain(sql, s => s.Contains("ON \"Orders\""));
    }

    [Fact]
    public void Policy_changes_without_column_changes_keep_their_place()
    {
        using var before = Contexts.Create(t => t.UseSettingName("app.tenant_id"));
        using var after = Contexts.Create(t => t.UseSettingName("app.mandant_id"));

        var commands = Contexts.Diff(before, after);
        Assert.All(commands, c => Assert.IsType<AlterTableOperation>(c));
        Assert.Equal(2 * commands.Count, Contexts.Sql(after, commands).Count);   // DROP + CREATE per table
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
        Assert.Contains("GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO shop_app;", sql);
        Assert.Contains("ALTER DEFAULT PRIVILEGES IN SCHEMA public "
                        + "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO shop_app;", sql);
        // Future sequences too: a serial column or HiLo sequence created by a later migration
        Assert.Contains("ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO shop_app;", sql);
        Assert.Contains("DROP ROLE IF EXISTS shop_app;", sql);
    }

    [Fact]
    public void Migration_builder_validates_role_names()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");

        Assert.Throws<ArgumentException>(() => builder.CreateApplicationRole("x\"; DROP"));
    }
}
