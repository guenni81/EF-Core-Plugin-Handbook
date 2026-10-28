using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests;

/// <summary>
/// Migrations that combine a change of the session configuration with a schema-bound change of
/// a protected table (chapter "Testing strategy"). Each case runs Up and Down through EF's differ
/// against SQL Server and checks reading and writing for two tenants through the app login.
/// Combinations with a dropped table are in <see cref="SqlServerSessionSettingsTests"/>.
/// </summary>
public class SqlServerCombinedMigrationTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public SqlServerCombinedMigrationTests(SqlServerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private static Task MigrateAsync(string owner, MappingContext? source, MappingContext target)
        => SqlServerMigrationTests.RunAsync(owner, Versions.Commands(source, target));

    private static async Task<int> CountAsync(string owner, string sql)
    {
        await using var connection = new SqlConnection(owner);
        await connection.OpenAsync();
        return (int)(await SqlServerMigrationTests.Execute(connection, sql))!;
    }

    [Fact]
    public void Session_key_change_with_a_table_rename_drops_before_and_restores_after()
    {
        var sql = Versions.Commands(MappingContext.Create(Versions.ColumnRenamed),
                MappingContext.Create(Versions.TableRenamed, sessionKey: "NewTenant"))
            .Select(c => c.CommandText.Trim()).ToList();

        var dropPolicy = sql.FindIndex(s => s.Contains("DROP SECURITY POLICY", StringComparison.Ordinal)
                                            && s.Contains("OBJECT_ID(N'[dbo].[Invoice]')", StringComparison.Ordinal));
        var dropFunction = sql.FindIndex(s => s.StartsWith("DROP FUNCTION", StringComparison.Ordinal));
        var rename = sql.FindIndex(s => s.Contains("sp_rename N'[Invoice]'", StringComparison.Ordinal));
        var create = Assert.Single(sql, s => s.StartsWith("CREATE SECURITY POLICY", StringComparison.Ordinal));

        Assert.True(dropPolicy >= 0 && dropPolicy < dropFunction && dropFunction < rename);
        Assert.Contains("ON [dbo].[Bills]", create, StringComparison.Ordinal);
        Assert.Equal(sql.Count - 1, sql.IndexOf(create));
    }

    [Fact]
    public async Task Session_key_change_with_a_table_rename_is_migrated_up_and_down()
    {
        var (owner, app) = await SqlServerMigrationTests.CreateDatabaseAsync(_fixture);
        await MigrateAsync(owner, null, MappingContext.Create(Versions.ColumnRenamed));

        await MigrateAsync(owner, MappingContext.Create(Versions.ColumnRenamed),
            MappingContext.Create(Versions.TableRenamed, sessionKey: "NewTenant"));
        await SqlServerMigrationTests.AssertIsolatedAsync(app, "[dbo].[Bills]", "Mandant", "NewTenant");

        await MigrateAsync(owner, MappingContext.Create(Versions.TableRenamed, sessionKey: "NewTenant"),
            MappingContext.Create(Versions.ColumnRenamed));
        await SqlServerMigrationTests.AssertIsolatedAsync(app, "[dbo].[Invoice]", "Mandant");
        Assert.Equal(1, await CountAsync(owner, "SELECT count(*) FROM sys.security_policies"));
    }

    [Fact]
    public async Task Policy_schema_change_with_a_column_rename_and_a_schema_move_is_migrated_up_and_down()
    {
        var (owner, app) = await SqlServerMigrationTests.CreateDatabaseAsync(_fixture);
        await MigrateAsync(owner, null, MappingContext.Create(Versions.V1));

        // Column renamed, table renamed and moved, policy schema changed: all in one migration
        await MigrateAsync(owner, MappingContext.Create(Versions.V1),
            MappingContext.Create(Versions.SchemaMoved, policySchema: "rls"));
        await SqlServerMigrationTests.AssertIsolatedAsync(app, "[sales].[Bills]", "Mandant");
        Assert.Equal(1, await CountAsync(owner,
            "SELECT count(*) FROM sys.security_policies WHERE SCHEMA_NAME(schema_id) = N'rls'"));
        Assert.Equal(0, await CountAsync(owner,
            "SELECT count(*) FROM sys.objects WHERE name = N'fn_tenant_predicate' AND SCHEMA_NAME(schema_id) = N'acme'"));

        await MigrateAsync(owner, MappingContext.Create(Versions.SchemaMoved, policySchema: "rls"),
            MappingContext.Create(Versions.V1));
        await SqlServerMigrationTests.AssertIsolatedAsync(app, "[dbo].[Invoice]", "TenantId");
        Assert.Equal(1, await CountAsync(owner,
            "SELECT count(*) FROM sys.security_policies WHERE SCHEMA_NAME(schema_id) = N'acme'"));
    }

    [Fact]
    public async Task Failing_migration_rolls_back_and_keeps_policy_and_function()
    {
        var (owner, app) = await SqlServerMigrationTests.CreateDatabaseAsync(_fixture);
        await MigrateAsync(owner, null, MappingContext.Create(Versions.V1));

        // The generated operations drop policy and function first; a later error must undo that
        using (var target = MappingContext.Create(Versions.TableRenamed, sessionKey: "NewTenant"))
        {
            List<MigrationOperation> operations =
            [
                .. Versions.Operations(MappingContext.Create(Versions.V1).GetService<IDesignTimeModel>().Model, target),
                new SqlOperation { Sql = "THROW 50000, N'Migration failed on purpose', 1;" },
            ];
            var commands = target.GetService<IMigrationsSqlGenerator>()
                .Generate(operations, target.GetService<IDesignTimeModel>().Model);

            var ex = await Assert.ThrowsAsync<SqlException>(() => SqlServerMigrationTests.RunAsync(owner, commands));
            Assert.Contains("failed on purpose", ex.Message, StringComparison.Ordinal);
        }

        // Unchanged: table, column, policy and the function with the old key
        await SqlServerMigrationTests.AssertIsolatedAsync(app, "[dbo].[Invoice]", "TenantId");
        Assert.Equal(1, await CountAsync(owner, "SELECT count(*) FROM sys.security_policies"));
        Assert.Equal(1, await CountAsync(owner,
            "SELECT count(*) FROM sys.objects WHERE name = N'fn_tenant_predicate' "
            + "AND OBJECT_DEFINITION(object_id) LIKE N'%SESSION[_]CONTEXT(N''TenantId'')%'"));
    }
}
