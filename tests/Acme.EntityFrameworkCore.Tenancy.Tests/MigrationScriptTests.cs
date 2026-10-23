using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>Empty context that only exists to own <see cref="RolesMigration"/>.</summary>
public sealed class ScriptContext : DbContext
{
    public ScriptContext(DbContextOptions<ScriptContext> options)
        : base(options)
    {
    }
}

/// <summary>Hand-written migration that uses the plugin's custom operations.</summary>
[DbContext(typeof(ScriptContext))]
[Migration("20261021000000_Roles")]
public class RolesMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateApplicationRole("script_role");
        migrationBuilder.GrantTablePrivileges("script_role");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropApplicationRole("script_role");
}

/// <summary>Custom migration operations inside an idempotent migration script.</summary>
public class MigrationScriptTests
{
    [Fact]
    public void Custom_operations_appear_in_idempotent_script()
    {
        var options = new DbContextOptionsBuilder<ScriptContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseAcmeTenancy()
            .Options;
        using var ctx = new ScriptContext(options);

        var script = ctx.GetService<IMigrator>()
            .GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        // Npgsql wraps every operation in DO $EF$ ... $EF$; our own DO $$ block is nested inside.
        // Whether that is valid SQL is checked by the integration test, which executes it.
        Assert.Contains("DO $EF$", script);
        Assert.Contains("DO $$", script);
        Assert.Contains("CREATE ROLE script_role NOLOGIN NOSUPERUSER NOBYPASSRLS;", script);
    }
}
