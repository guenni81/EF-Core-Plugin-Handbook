using Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests;

/// <summary>
/// docs/deployment/check-tenant-policies.sqlserver.sql against databases in a correct and in a
/// deliberately broken state (chapter "Row Level Security in production").
/// </summary>
public class SqlServerPolicyDiagnosisTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public SqlServerPolicyDiagnosisTests(SqlServerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    /// <summary>The expected tables from the design-time model: every table with a policy column.</summary>
    private static string ExpectedTables(DbContext context)
        => string.Join(",", context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            .Where(t => t[SqlServerRlsAnnotationNames.TenantColumn] is string)
            .Select(t => $"{t.Schema ?? "dbo"}.{t.Name}:{t[SqlServerRlsAnnotationNames.TenantColumn]}"));

    private static string Script()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "Acme.EntityFrameworkCore.Tenancy.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory.FullName, "docs", "deployment",
            "check-tenant-policies.sqlserver.sql"));
    }

    private static async Task<Dictionary<string, (string Status, string? Reason)>> DiagnoseAsync(
        string connectionString, string expected, string sessionKey = "TenantId", string? asUser = null)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await SqlServerMigrationTests.Execute(connection,
            "EXEC sp_set_session_context N'diag.expected', @e; "
            + "EXEC sp_set_session_context N'diag.app_user', N'shop_app'; "
            + "EXEC sp_set_session_context N'diag.session_key', @k;",
            ("@e", expected), ("@k", sessionKey));
        if (asUser is not null)
            await SqlServerMigrationTests.Execute(connection, $"EXECUTE AS USER = N'{asUser}'");

        var result = new Dictionary<string, (string, string?)>();
        await using var command = new SqlCommand(Script(), connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[reader.IsDBNull(1) ? "" : reader.GetString(1)] =
                (reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3));
        return result;
    }

    private async Task<(string Owner, string App, string Expected)> CreateDatabaseAsync()
    {
        var (owner, app) = await SqlServerMigrationTests.CreateDatabaseAsync(_fixture);
        await SqlServerMigrationTests.RunAsync(owner, Versions.Commands(null, Versions.V1, owner));
        using var context = MappingContext.Create(Versions.V1);
        return (owner, app, ExpectedTables(context));
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await SqlServerMigrationTests.Execute(connection, sql);
    }

    [Fact]
    public async Task Correct_database_passes()
    {
        var (owner, _, expected) = await CreateDatabaseAsync();
        Assert.Equal("dbo.Invoice:TenantId", expected);

        Assert.Equal(("OK", null), Assert.Single(await DiagnoseAsync(owner, expected)).Value);
    }

    [Fact]
    public async Task View_definition_is_the_read_right_and_without_it_the_result_is_not_verifiable()
    {
        var (owner, app, expected) = await CreateDatabaseAsync();
        await ExecuteAsync(owner, "CREATE USER diag_auditor WITHOUT LOGIN; GRANT VIEW DEFINITION TO diag_auditor;");

        // The app login has no VIEW DEFINITION: it cannot see the policies, so no verdict
        Assert.Equal(("NOT VERIFIABLE", "no VIEW DEFINITION on the database"),
            Assert.Single(await DiagnoseAsync(app, expected)).Value);
        Assert.Equal(("OK", null), Assert.Single(await DiagnoseAsync(owner, expected, asUser: "diag_auditor")).Value);
    }

    [Theory]
    [InlineData("ALTER SECURITY POLICY [acme].[tenant_dbo_Invoice] WITH (STATE = OFF)", "disabled")]
    [InlineData("DROP SECURITY POLICY [acme].[tenant_dbo_Invoice]", "no FILTER predicate")]
    [InlineData("ALTER SECURITY POLICY [acme].[tenant_dbo_Invoice] DROP BLOCK PREDICATE ON [dbo].[Invoice] AFTER UPDATE",
        "BLOCK predicates")]
    [InlineData("ALTER ROLE db_owner ADD MEMBER shop_app", "can switch security policies off")]
    [InlineData("ALTER TABLE [dbo].[Invoice] ADD [ValidFrom] datetime2 GENERATED ALWAYS AS ROW START HIDDEN "
                + "NOT NULL DEFAULT SYSUTCDATETIME(), [ValidTo] datetime2 GENERATED ALWAYS AS ROW END HIDDEN "
                + "NOT NULL DEFAULT CONVERT(datetime2, '9999-12-31 23:59:59.9999999'), "
                + "PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]); "
                + "ALTER TABLE [dbo].[Invoice] SET (SYSTEM_VERSIONING = ON);", "history table")]
    public async Task Broken_protection_is_reported(string breakIt, string reason)
    {
        var (owner, _, expected) = await CreateDatabaseAsync();
        await ExecuteAsync(owner, breakIt);

        var (status, actual) = Assert.Single(await DiagnoseAsync(owner, expected)).Value;

        Assert.Equal("NOT PROTECTED", status);
        Assert.Contains(reason, actual, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Function_with_another_session_key_is_reported()
    {
        var (owner, _, expected) = await CreateDatabaseAsync();

        var (status, reason) = Assert.Single(await DiagnoseAsync(owner, expected, sessionKey: "NewTenant")).Value;

        Assert.Equal("NOT PROTECTED", status);
        Assert.Contains("does not read the session key NewTenant", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_table_and_empty_expectation_are_never_success()
    {
        var (owner, _, expected) = await CreateDatabaseAsync();

        Assert.Equal(("NOT PROTECTED", "table not found"),
            (await DiagnoseAsync(owner, expected + ",dbo.Payments"))["Payments"]);
        Assert.Equal(("NOT VERIFIABLE", "diag.expected names no table"),
            Assert.Single(await DiagnoseAsync(owner, "")).Value);
    }
}
