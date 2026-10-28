using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// docs/deployment/check-tenant-policies.postgresql.sql against databases in a correct and in a
/// deliberately broken state (chapter "Row Level Security in production").
/// </summary>
public class PolicyDiagnosisTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public PolicyDiagnosisTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    /// <summary>
    /// The expected tables from the design-time model: every table with RLS, with its tenant
    /// column; deliberate exceptions have no RLS and are left out.
    /// </summary>
    public static string ExpectedTables(DbContext context)
    {
        var tables =
            from entityType in context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            where entityType.IsRlsEnabled()
            let table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            where table is not null
            let property = entityType.FindProperty(entityType.GetTenantPropertyName()!)!
            select $"{table.Value.Schema ?? "public"}.{table.Value.Name}:"
                   + property.GetColumnName(table.Value);
        return string.Join(",", tables);
    }

    internal static string Script(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "Acme.EntityFrameworkCore.Tenancy.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory.FullName, "docs", "deployment", name));
    }

    private static async Task<Dictionary<string, (string Status, string? Reason)>> DiagnoseAsync(
        string connectionString, string expected)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var configure = new NpgsqlCommand(
                         "SELECT set_config('diag.expected', @expected, false), "
                         + "set_config('diag.app_role', @role, false)", connection))
        {
            configure.Parameters.AddWithValue("expected", expected);
            configure.Parameters.AddWithValue("role", PostgresFixture.AppRole);
            await configure.ExecuteNonQueryAsync();
        }

        var result = new Dictionary<string, (string, string?)>();
        await using var command = new NpgsqlCommand(Script("check-tenant-policies.postgresql.sql"), connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[reader.IsDBNull(1) ? "" : reader.GetString(1)] =
                (reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3));
        return result;
    }

    private async Task<(string Owner, string App, string Expected)> CreateDatabaseAsync()
    {
        var (owner, app) = await TestDatabase.CreateAsync(_fixture);
        await using var db = new ShopContext(TestDatabase.Options<ShopContext>(owner));
        await TestDatabase.CreateSchemaAsync(db);
        return (owner, app, ExpectedTables(db));
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Correct_database_passes_for_every_expected_table()
    {
        var (owner, app, expected) = await CreateDatabaseAsync();
        Assert.Equal("public.Orders:TenantId,public.Shipments:TenantId", expected);

        // As owner and as the app role itself: the default catalog privileges suffice
        foreach (var connectionString in new[] { owner, app })
        {
            var result = await DiagnoseAsync(connectionString, expected);
            Assert.Equal(["Orders", "Shipments"], result.Keys.Order());
            Assert.All(result.Values, r => Assert.Equal(("OK", null), r));
        }
    }

    [Theory]
    [InlineData("ALTER TABLE \"Orders\" DISABLE ROW LEVEL SECURITY", "row level security is disabled")]
    [InlineData("ALTER TABLE \"Orders\" NO FORCE ROW LEVEL SECURITY", "FORCE ROW LEVEL SECURITY is missing")]
    [InlineData("DROP POLICY tenant_isolation ON \"Orders\"", "policy tenant_isolation is missing")]
    [InlineData("CREATE POLICY open_read ON \"Orders\" FOR SELECT USING (true)", "further permissive policies")]
    [InlineData("ALTER TABLE \"Orders\" OWNER TO shop_app", "owns the table")]
    public async Task Broken_protection_is_reported_for_the_affected_table(string breakIt, string reason)
    {
        var (owner, _, expected) = await CreateDatabaseAsync();
        await ExecuteAsync(owner, breakIt);

        var result = await DiagnoseAsync(owner, expected);

        Assert.Equal("NOT PROTECTED", result["Orders"].Status);
        Assert.Contains(reason, result["Orders"].Reason, StringComparison.Ordinal);
        Assert.Equal(("OK", null), result["Shipments"]);
    }

    [Fact]
    public async Task Policies_for_other_roles_do_not_widen_the_app_role()
    {
        var (owner, _, expected) = await CreateDatabaseAsync();
        await ExecuteAsync(owner, "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'diag_support') "
                                  + "THEN CREATE ROLE diag_support; END IF; END $$");
        await ExecuteAsync(owner, "CREATE POLICY support_read ON \"Orders\" FOR SELECT TO diag_support USING (true)");

        Assert.Equal(("OK", null), (await DiagnoseAsync(owner, expected))["Orders"]);
    }

    [Fact]
    public async Task Missing_expected_table_is_not_protected()
    {
        var (owner, _, expected) = await CreateDatabaseAsync();

        var result = await DiagnoseAsync(owner, expected + ",public.Invoices");

        Assert.Equal(("NOT PROTECTED", "table not found"), result["Invoices"]);
    }

    [Fact]
    public async Task Without_catalog_privileges_the_result_is_not_verifiable()
    {
        var (owner, app, expected) = await CreateDatabaseAsync();
        await ExecuteAsync(owner, "REVOKE SELECT ON pg_catalog.pg_policy FROM PUBLIC");

        var result = await DiagnoseAsync(app, expected);

        Assert.All(result.Values, r => Assert.Equal(
            ("NOT VERIFIABLE", "no SELECT privilege on pg_catalog.pg_policy"), r));
        Assert.All((await DiagnoseAsync(owner, expected)).Values, r => Assert.Equal("OK", r.Status));
    }

    [Fact]
    public async Task Empty_expectation_is_never_reported_as_success()
    {
        var (owner, _, _) = await CreateDatabaseAsync();

        Assert.Equal(("NOT VERIFIABLE", "diag.expected names no table"),
            Assert.Single(await DiagnoseAsync(owner, "")).Value);
    }
}
