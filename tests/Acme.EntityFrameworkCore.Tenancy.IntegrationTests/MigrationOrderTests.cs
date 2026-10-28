using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests.MigrationOrder;

/// <summary>Entry of a ledger; the tenant column is a shadow property from version 1 on.</summary>
public class Entry
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// Version 0: shared table. Version 1: tenant column and isolation in one step. Version 2: the
/// tenant column renamed to "Mandant" (used together with another setting name).
/// </summary>
public sealed class LedgerContext : DbContext
{
    private readonly int _version;

    public LedgerContext(DbContextOptions<LedgerContext> options, int version)
        : base(options)
    {
        _version = version;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entry = modelBuilder.Entity<Entry>().ToTable("Entries");
        if (_version == 0)
            return;

        var tenant = entry.Property<TenantId>("TenantId").IsTenantProperty();
        if (_version == 2)
            tenant.HasColumnName("Mandant");
    }
}

/// <summary>
/// Migrations that change a tenant column and the isolation of the same table together, generated
/// by EF's differ and run against PostgreSQL in one transaction (chapter "Test strategy").
/// </summary>
public class MigrationOrderTests : IClassFixture<PostgresFixture>
{
    private const string OtherSetting = "app.mandant_id";

    private readonly PostgresFixture _fixture;

    public MigrationOrderTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private static LedgerContext Version(
        string connectionString, int version, ITenantProvider? tenants = null, string? setting = null)
        => new(TestDatabase.Options<LedgerContext>(connectionString, tenants,
            tenancy: t =>
            {
                t.ApplyQueryFilter(false);                         // only RLS protects
                if (setting is not null)
                    t.UseSettingName(setting);
            }), version);

    /// <summary>A database at version 0 with two rows that belong to no tenant yet.</summary>
    private async Task<(string Owner, string App)> CreateVersion0Async()
    {
        var (owner, app) = await TestDatabase.CreateAsync(_fixture);
        await using var v0 = Version(owner, 0);
        await TestDatabase.CreateSchemaAsync(v0);
        v0.AddRange(new Entry { Text = "old1" }, new Entry { Text = "old2" });
        await v0.SaveChangesAsync();
        return (owner, app);
    }

    /// <summary>A database created directly at version 1, independent of the Up migration.</summary>
    private async Task<(string Owner, string App)> CreateVersion1Async()
    {
        var (owner, app) = await TestDatabase.CreateAsync(_fixture);
        await using var v1 = Version(owner, 1);
        await TestDatabase.CreateSchemaAsync(v1);
        return (owner, app);
    }

    private static Task MigrateAsync(DbContext source, DbContext target, params MigrationOperation[] after)
        => TestDatabase.MigrateAsync(source, target, after: after);

    private static SqlOperation Fail() => new() { Sql = "DO $$ BEGIN RAISE EXCEPTION 'migration failed'; END $$;" };

    private static async Task AddAsync(string app, int version, Guid tenant, string text, string? setting = null)
    {
        await using var db = Version(app, version, new MutableTenantProvider { CurrentTenantId = new TenantId(tenant) }, setting);
        db.Add(new Entry { Text = text });
        await db.SaveChangesAsync();
    }

    private static async Task<List<string>> TextsAsync(string app, int version, Guid tenant, string? setting = null)
    {
        await using var db = Version(app, version, new MutableTenantProvider { CurrentTenantId = new TenantId(tenant) }, setting);
        return await db.Set<Entry>().OrderBy(e => e.Text).Select(e => e.Text).ToListAsync();
    }

    private static async Task<(bool Rls, long Policies, long Columns)> StateAsync(string owner, string column)
        => ((bool)(await TestDatabase.ScalarAsync(owner,
                "SELECT relrowsecurity FROM pg_class WHERE relname = 'Entries'"))!,
            (long)(await TestDatabase.ScalarAsync(owner,
                "SELECT count(*) FROM pg_policies WHERE tablename = 'Entries'"))!,
            (long)(await TestDatabase.ScalarAsync(owner,
                $"SELECT count(*) FROM information_schema.columns WHERE table_name = 'Entries' AND column_name = '{column}'"))!);

    [Fact]
    public async Task Up_adds_the_tenant_column_and_isolation_together()
    {
        var (owner, app) = await CreateVersion0Async();

        await using (var v0 = Version(owner, 0))
        await using (var v1 = Version(owner, 1))
            await MigrateAsync(v0, v1);

        Assert.Equal((true, 1L, 1L), await StateAsync(owner, "TenantId"));

        // As the restricted app role: own rows only; the rows without a tenant are hidden
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        await AddAsync(app, 1, a, "a1");
        await AddAsync(app, 1, b, "b1");
        Assert.Equal(["a1"], await TextsAsync(app, 1, a));
        Assert.Equal(["b1"], await TextsAsync(app, 1, b));
        Assert.Equal(4L, await TestDatabase.ScalarAsync(owner, "SELECT count(*) FROM \"Entries\""));
    }

    [Fact]
    public async Task Failed_up_rolls_back_to_the_previous_schema()
    {
        var (owner, _) = await CreateVersion0Async();

        await using (var v0 = Version(owner, 0))
        await using (var v1 = Version(owner, 1))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(v0, v1, Fail()));
            Assert.Contains("migration failed", ex.MessageText);
        }

        Assert.Equal((false, 0L, 0L), await StateAsync(owner, "TenantId"));
    }

    [Fact]
    public async Task Down_removes_isolation_and_tenant_column_together()
    {
        var (owner, app) = await CreateVersion1Async();
        await AddAsync(app, 1, Guid.NewGuid(), "a1");
        await AddAsync(app, 1, Guid.NewGuid(), "b1");

        await using (var v1 = Version(owner, 1))
        await using (var v0 = Version(owner, 0))
            await MigrateAsync(v1, v0);

        Assert.Equal((false, 0L, 0L), await StateAsync(owner, "TenantId"));
        await using var shared = Version(app, 0);
        Assert.Equal(2, await shared.Set<Entry>().CountAsync());         // shared table again
    }

    [Fact]
    public async Task Failed_down_keeps_the_isolation()
    {
        var (owner, app) = await CreateVersion1Async();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        await AddAsync(app, 1, a, "a1");

        // The policy is dropped before the column; the rollback has to bring both back
        await using (var v1 = Version(owner, 1))
        await using (var v0 = Version(owner, 0))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(v1, v0, Fail()));
            Assert.Contains("migration failed", ex.MessageText);          // not at DROP COLUMN
        }

        Assert.Equal((true, 1L, 1L), await StateAsync(owner, "TenantId"));
        Assert.Equal(["a1"], await TextsAsync(app, 1, a));
        Assert.Empty(await TextsAsync(app, 1, b));
    }

    [Fact]
    public async Task Renamed_tenant_column_with_another_setting_name_up_and_down()
    {
        var (owner, app) = await CreateVersion1Async();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        await AddAsync(app, 1, a, "a1");

        // Up: RENAME COLUMN and a new setting name; the policy is replaced around the rename
        await using (var v1 = Version(owner, 1))
        await using (var v2 = Version(owner, 2, setting: OtherSetting))
            await MigrateAsync(v1, v2);

        Assert.Equal((true, 1L, 1L), await StateAsync(owner, "Mandant"));
        Assert.Contains(OtherSetting, (string)(await TestDatabase.ScalarAsync(owner,
            "SELECT qual FROM pg_policies WHERE tablename = 'Entries'"))!);
        await AddAsync(app, 2, b, "b1", OtherSetting);
        Assert.Equal(["a1"], await TextsAsync(app, 2, a, OtherSetting));
        Assert.Equal(["b1"], await TextsAsync(app, 2, b, OtherSetting));
        Assert.Empty(await TextsAsync(app, 1, a));                       // old setting: nothing

        // Down: back to the old name and setting
        await using (var v2 = Version(owner, 2, setting: OtherSetting))
        await using (var v1 = Version(owner, 1))
            await MigrateAsync(v2, v1);

        Assert.Equal((true, 1L, 1L), await StateAsync(owner, "TenantId"));
        Assert.Equal(["a1"], await TextsAsync(app, 1, a));
        Assert.Equal(["b1"], await TextsAsync(app, 1, b));
    }
}
