using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Operations;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests.Sequences;

/// <summary>Key from a serial column: INSERT calls nextval() on its own sequence.</summary>
public class LaterSerial
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Key from a HiLo sequence: EF calls nextval() before the INSERT.</summary>
public class LaterHiLo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Tables and sequences that a migration creates after the privileges were granted.</summary>
public sealed class LaterSequenceContext : DbContext
{
    public LaterSequenceContext(DbContextOptions<LaterSequenceContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LaterSerial>().Property(e => e.Id).UseSerialColumn();
        modelBuilder.Entity<LaterHiLo>().Property(e => e.Id).UseHiLo("later_hilo");
    }
}

/// <summary>
/// GrantTablePrivileges sets default privileges for objects the migrating role creates later:
/// a sequence created by a later migration must be usable by the app role without a GRANT of
/// its own.
/// </summary>
public class SequencePrivilegesTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public SequencePrivilegesTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    [Fact]
    public async Task Sequences_created_after_the_grant_are_usable_by_the_app_role()
    {
        var (owner, app) = await TestDatabase.CreateAsync(_fixture);

        // 1. Privileges first, on an empty schema: only the default privileges can cover what follows
        await using (var script = new ScriptContext(TestDatabase.Options<ScriptContext>(owner)))
            await TestDatabase.RunAsync(script, [new GrantTablePrivilegesOperation { Role = PostgresFixture.AppRole }]);

        // 2. A later migration by the same role creates a serial column and a HiLo sequence
        await using (var migration = new LaterSequenceContext(TestDatabase.Options<LaterSequenceContext>(owner)))
            await TestDatabase.MigrateAsync(null, migration);

        // 3. The app role inserts through both; before the fix: 42501, permission denied for sequence
        await using var db = new LaterSequenceContext(TestDatabase.Options<LaterSequenceContext>(app));
        var serial = new LaterSerial { Name = "a" };
        var hilo = new LaterHiLo { Name = "b" };
        db.AddRange(serial, hilo);
        await db.SaveChangesAsync();

        Assert.True(serial.Id > 0);
        Assert.True(hilo.Id > 0);
    }
}
