using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Runtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests.Seeding;

/// <summary>Shared master data: no tenant, no isolation.</summary>
public class Country
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
}

/// <summary>Initial data that every tenant gets: tenant-owned, protected by RLS.</summary>
public class Setting : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Context of the seeding example; <c>seedSettingsInModel</c> shows the wrong way.</summary>
public sealed class SeedContext : DbContext
{
    private readonly bool _seedSettingsInModel;

    public SeedContext(DbContextOptions<SeedContext> options, bool seedSettingsInModel = false)
        : base(options)
    {
        _seedSettingsInModel = seedSettingsInModel;
    }

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Model managed data: static, the same for everyone, written by the migration SQL
        modelBuilder.Entity<Country>().HasData(
            new Country { Id = 1, Code = "DE" }, new Country { Id = 2, Code = "AT" });

        // One row per tenant and key: a repeated or concurrent seed cannot duplicate it
        modelBuilder.Entity<Setting>().HasIndex(s => new { s.TenantId, s.Key }).IsUnique();

        // WRONG: tenant rows in migration SQL. No interceptor runs, no tenant is set, and FORCE
        // ROW LEVEL SECURITY applies to the migration role too
        if (_seedSettingsInModel)
            modelBuilder.Entity<Setting>().HasData(
                new Setting { Id = 1, TenantId = TenantId.New(), Key = "currency", Value = "EUR" });
    }
}

/// <summary>
/// Seeding under row level security: runs as the migration role after EnsureCreated or
/// Migrate, with the plugin's interceptors (chapter "Retrofitting and operating multi-tenancy").
/// </summary>
public static class TenantSeeding
{
    /// <summary>Gives every registered tenant its initial settings, idempotently.</summary>
    /// <param name="builder">The options builder of the migration context.</param>
    /// <param name="tenants">The tenant provider of the migration context's interceptors.</param>
    /// <returns>The same builder.</returns>
    public static DbContextOptionsBuilder UseTenantSeeding(
        this DbContextOptionsBuilder builder, MutableTenantProvider tenants)
        => builder
            // Tools and migration bundles call the synchronous delegate
            .UseSeeding((context, _) =>
            {
                foreach (var tenant in context.Set<Tenant>().Select(t => t.Id).ToList())
                {
                    tenants.CurrentTenantId = tenant;
                    context.Database.ApplyTenantSession();     // the connection may be open
                    if (!context.Set<Setting>().Any(s => s.Key == "currency"))   // RLS: own rows
                    {
                        context.Add(new Setting { Key = "currency", Value = "EUR" });
                        context.SaveChanges();          // the interceptor sets the TenantId
                    }
                }

                tenants.CurrentTenantId = null;
                context.Database.ApplyTenantSession();
            })
            .UseAsyncSeeding(async (context, _, cancellationToken) =>
            {
                var all = await context.Set<Tenant>().Select(t => t.Id).ToListAsync(cancellationToken);
                foreach (var tenant in all)
                {
                    tenants.CurrentTenantId = tenant;
                    await context.Database.ApplyTenantSessionAsync(cancellationToken);
                    if (!await context.Set<Setting>().AnyAsync(s => s.Key == "currency", cancellationToken))
                    {
                        context.Add(new Setting { Key = "currency", Value = "EUR" });
                        await context.SaveChangesAsync(cancellationToken);
                    }
                }

                tenants.CurrentTenantId = null;
                await context.Database.ApplyTenantSessionAsync(cancellationToken);
            });
}

/// <summary>HasData, UseSeeding and UseAsyncSeeding against PostgreSQL with FORCE RLS.</summary>
public class SeedingTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public SeedingTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    /// <summary>The migration context: owner connection, interceptors, seeding.</summary>
    private static SeedContext Migrator(string owner, bool seed = true, bool seedSettingsInModel = false)
    {
        var tenants = new MutableTenantProvider();
        var builder = new DbContextOptionsBuilder<SeedContext>()
            .UseNpgsql(owner)
            .UseAcmeTenancy(t => t.ApplyQueryFilter(false))             // only RLS protects
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new TenantConnectionInterceptor(tenants), new TenantSaveChangesInterceptor(tenants));
        if (seed)
            builder.UseTenantSeeding(tenants);
        return new SeedContext(builder.Options, seedSettingsInModel);
    }

    private static SeedContext App(string app, TenantId tenant)
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = tenant };
        return new SeedContext(new DbContextOptionsBuilder<SeedContext>()
            .UseNpgsql(app)
            .UseAcmeTenancy(t => t.ApplyQueryFilter(false))
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new TenantConnectionInterceptor(tenants), new TenantSaveChangesInterceptor(tenants))
            .Options);
    }

    /// <summary>
    /// A database whose schema belongs to a migration role of its own: owner, but neither
    /// superuser nor BYPASSRLS. A superuser (like the container's) bypasses RLS even with FORCE.
    /// </summary>
    private async Task<(string Owner, string App)> CreateDatabaseAsync()
    {
        var (admin, app) = await TestDatabase.CreateAsync(_fixture);
        var role = "migrator_" + Guid.NewGuid().ToString("N")[..8];
        await TestDatabase.ScalarAsync(admin,
            $"CREATE ROLE {role} LOGIN PASSWORD 'migrator_pw' NOSUPERUSER NOBYPASSRLS; "
            + $"ALTER SCHEMA public OWNER TO {role}");
        var owner = new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = "migrator_pw" };
        return (owner.ConnectionString, app);
    }

    /// <summary>A database with the schema and two registered tenants, nothing seeded yet.</summary>
    private async Task<(string Owner, string App, TenantId A, TenantId B)> CreateAsync()
    {
        var (owner, app) = await CreateDatabaseAsync();
        await using (var db = Migrator(owner, seed: false))
            await TestDatabase.CreateSchemaAsync(db);

        await using (var db = Migrator(owner, seed: false))
        {
            db.AddRange(new Tenant { Name = "a" }, new Tenant { Name = "b" });
            await db.SaveChangesAsync();
            var ids = await db.Set<Tenant>().OrderBy(t => t.Name).Select(t => t.Id).ToListAsync();
            return (owner, app, ids[0], ids[1]);
        }
    }

    private static async Task<List<string>> SettingsAsync(string app, TenantId tenant)
    {
        await using var db = App(app, tenant);
        return await db.Set<Setting>().Select(s => s.Key + "=" + s.Value).ToListAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Seeding_gives_every_tenant_its_rows_and_a_repeated_run_adds_none(bool async)
    {
        var (owner, app, a, b) = await CreateAsync();

        // Twice, as after every deployment: EnsureCreated (and Migrate) call the delegate even
        // when nothing changes
        for (var run = 0; run < 2; run++)
        {
            await using var db = Migrator(owner);
            if (async)
                await db.Database.EnsureCreatedAsync();
            else
                db.Database.EnsureCreated();
        }

        Assert.Equal(["currency=EUR"], await SettingsAsync(app, a));
        Assert.Equal(["currency=EUR"], await SettingsAsync(app, b));
        Assert.Equal(2L, await TestDatabase.ScalarAsync(owner, "SELECT count(*) FROM \"Countries\""));
        // FORCE RLS: even the owner sees no tenant rows without a tenant
        Assert.Equal(0L, await TestDatabase.ScalarAsync(owner, "SELECT count(*) FROM \"Settings\""));
    }

    [Fact]
    public async Task Tenant_rows_in_HasData_are_rejected_by_the_policy()
    {
        var (owner, _) = await CreateDatabaseAsync();

        // The InsertData of the model runs as migration SQL: no interceptor, no tenant
        await using var db = Migrator(owner, seed: false, seedSettingsInModel: true);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.EnsureCreatedAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        Assert.Contains("row-level security", ex.MessageText);
    }

    [Fact]
    public async Task A_superuser_bypasses_even_FORCE_ROW_LEVEL_SECURITY()
    {
        // The container's superuser: the same HasData goes through, with a tenant nobody chose
        var (superuser, _) = await TestDatabase.CreateAsync(_fixture);
        await using var db = Migrator(superuser, seed: false, seedSettingsInModel: true);

        Assert.True(await db.Database.EnsureCreatedAsync());
        Assert.Equal(1L, await TestDatabase.ScalarAsync(superuser, "SELECT count(*) FROM \"Settings\""));
    }

    [Fact]
    public async Task Seeding_without_a_tenant_is_rejected_by_the_interceptor()
    {
        var (owner, _, _, _) = await CreateAsync();
        var builder = new DbContextOptionsBuilder<SeedContext>()
            .UseNpgsql(owner)
            .UseAcmeTenancy(t => t.ApplyQueryFilter(false))
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new TenantSaveChangesInterceptor(new MutableTenantProvider()))
            .UseAsyncSeeding(async (context, _, cancellationToken) =>
            {
                context.Add(new Setting { Key = "currency", Value = "EUR" });
                await context.SaveChangesAsync(cancellationToken);
            });

        await using var db = new SeedContext(builder.Options);
        await Assert.ThrowsAsync<TenancyException>(() => db.Database.EnsureCreatedAsync());
    }

    [Fact]
    public async Task A_generated_script_contains_model_data_but_no_seeding()
    {
        var (owner, _) = await CreateDatabaseAsync();
        await using var db = Migrator(owner);

        // What an external SQL tool would run: HasData yes, UseSeeding never
        var script = db.Database.GenerateCreateScript();
        Assert.Contains("INSERT INTO \"Countries\"", script);
        Assert.DoesNotContain("INSERT INTO \"Settings\"", script);
    }
}
