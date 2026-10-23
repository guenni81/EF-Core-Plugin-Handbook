using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace ShopApi;

/// <summary>
/// DEVELOPMENT ONLY: prepares the database so that the sample runs without manual steps
/// (see docker-compose.yml). A real application uses migrations instead (chapter 32).
/// </summary>
public static class DevelopmentDatabase
{
    /// <summary>Name of the restricted role the application connects with.</summary>
    public const string AppRole = "shop_app";

    /// <summary>
    /// Creates database, tables, RLS and policies as the owner, then the restricted
    /// application role. Safe to run on every start: every step is idempotent.
    /// </summary>
    /// <param name="ownerConnectionString">Connection of the role that owns the tables.</param>
    public static async Task EnsureCreatedAsync(string ownerConnectionString)
    {
        // Owner context WITHOUT interceptors: it only creates the schema, it never reads data
        var options = new DbContextOptionsBuilder<ShopApiContext>()
            .UseNpgsql(ownerConnectionString)
            .UseAcmeTenancy()
            .Options;
        await using var owner = new ShopApiContext(options);

        // EnsureCreated uses the plugin's migrations SQL generator: RLS, FORCE, the policy and
        // the tenant index are created exactly as by a migration. Does nothing if tables exist.
        await owner.Database.EnsureCreatedAsync();

        // The plugin's own migration operations, executed directly. Order matters: GRANT ...
        // ON ALL TABLES only affects tables that exist already.
        var commands = owner.GetService<IMigrationsSqlGenerator>().Generate(
        [
            new CreateApplicationRoleOperation { Name = AppRole, CanLogin = true },
            new GrantTablePrivilegesOperation { Role = AppRole },
        ]);
        await owner.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(
            commands, owner.GetService<IRelationalConnection>(),
            new MigrationExecutionState(), commitTransaction: true);

        // Passwords do not belong in migrations; this one matches appsettings.json. Constant
        // SQL only: never interpolate untrusted input into raw SQL like this.
        await owner.Database.ExecuteSqlRawAsync($"ALTER ROLE {AppRole} PASSWORD 'shop_app'");
    }
}
