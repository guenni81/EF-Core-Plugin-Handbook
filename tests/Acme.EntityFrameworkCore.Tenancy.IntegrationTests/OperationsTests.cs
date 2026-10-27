using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests.Operations;

/// <summary>Version 0: the table before multi-tenancy.</summary>
public class LegacyTicket
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

/// <summary>Versions 1 and 2: the same table with a tenant column.</summary>
public class Ticket : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Title { get; set; } = "";
}

/// <summary>The three model versions of the retrofit.</summary>
public sealed class TicketContext : DbContext
{
    private readonly int _version;

    public TicketContext(DbContextOptions<TicketContext> options, int version)
        : base(options)
    {
        _version = version;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (_version == 0)
        {
            modelBuilder.Entity<LegacyTicket>().ToTable("Tickets");
            return;
        }

        var ticket = modelBuilder.Entity<Ticket>().ToTable("Tickets");
        if (_version == 1)
            ticket.HasNoRowLevelSecurity();     // column first, protection in the next step
    }
}

/// <summary>
/// Retrofitting an existing table (expand, backfill, guard, protect) and the plugin under
/// execution strategies and lost connections, against PostgreSQL.
/// </summary>
public class OperationsTests : IClassFixture<PostgresFixture>
{
    private const string EmptyTenant = "00000000-0000-0000-0000-000000000000";

    private readonly PostgresFixture _fixture;

    public OperationsTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private static TicketContext Version(string connectionString, int version, ITenantProvider? tenants = null)
        => new(TestDatabase.Options<TicketContext>(connectionString, tenants), version);

    /// <summary>Stops the migration before RLS is enabled while rows lack a tenant.</summary>
    private static SqlOperation Guard() => new()
    {
        Sql = $"""
               DO $$ BEGIN
                 IF EXISTS (SELECT 1 FROM "Tickets" WHERE "TenantId" = '{EmptyTenant}') THEN
                   RAISE EXCEPTION 'Tickets without a tenant: backfill first';
                 END IF;
               END $$;
               """,
    };

    private async Task<(string Owner, string App, Guid A, Guid B)> RetrofitToVersion1Async()
    {
        var (owner, app) = await TestDatabase.CreateAsync(_fixture);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        // Version 0 with data
        await using (var v0 = Version(owner, 0))
        {
            await TestDatabase.CreateSchemaAsync(v0);
            v0.AddRange(new LegacyTicket { Title = "a1" }, new LegacyTicket { Title = "b1" },
                new LegacyTicket { Title = "a2" });
            await v0.SaveChangesAsync();
        }

        // Version 1: add the column (NOT NULL with the empty default), no RLS yet
        await using (var v0 = Version(owner, 0))
        await using (var v1 = Version(owner, 1))
            await TestDatabase.MigrateAsync(v0, v1);

        // Backfill as the owner, from whatever knows the assignment (here: the title)
        await TestDatabase.ScalarAsync(owner,
            $"""UPDATE "Tickets" SET "TenantId" = '{a}' WHERE "Title" LIKE 'a%'""");
        return (owner, app, a, b);
    }

    private static async Task<List<string>> TitlesAsync(string app, Guid tenant)
    {
        await using var db = Version(app, 2, new MutableTenantProvider { CurrentTenantId = new TenantId(tenant) });
        return await db.Set<Ticket>().OrderBy(t => t.Title).Select(t => t.Title).ToListAsync();
    }

    [Fact]
    public async Task Guard_stops_enabling_rls_while_rows_lack_a_tenant()
    {
        var (owner, _, _, _) = await RetrofitToVersion1Async();      // "b1" has no tenant yet

        await using (var v1 = Version(owner, 1))
        await using (var v2 = Version(owner, 2))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(
                () => TestDatabase.MigrateAsync(v1, v2, [Guard()]));
            Assert.Contains("backfill first", ex.MessageText);
        }

        // The transaction rolled back: no half-enabled protection
        Assert.Equal(false, await TestDatabase.ScalarAsync(owner,
            "SELECT rowsecurity FROM pg_tables WHERE tablename = 'Tickets'"));
    }

    [Fact]
    public async Task Retrofit_protects_the_table_and_down_removes_the_protection_again()
    {
        var (owner, app, a, b) = await RetrofitToVersion1Async();
        await TestDatabase.ScalarAsync(owner,
            $"""UPDATE "Tickets" SET "TenantId" = '{b}' WHERE "TenantId" = '{EmptyTenant}'""");

        await using (var v1 = Version(owner, 1))
        await using (var v2 = Version(owner, 2))
            await TestDatabase.MigrateAsync(v1, v2, [Guard()]);

        Assert.Equal(["a1", "a2"], await TitlesAsync(app, a));
        Assert.Equal(["b1"], await TitlesAsync(app, b));

        // Down: the policy goes, the column and its values stay
        await using (var v2 = Version(owner, 2))
        await using (var v1 = Version(owner, 1))
            await TestDatabase.MigrateAsync(v2, v1);
        Assert.Equal(0L, await TestDatabase.ScalarAsync(owner,
            "SELECT count(*) FROM pg_policies WHERE tablename = 'Tickets'"));
        Assert.Equal(3, (await TitlesAsync(app, a)).Count);           // unprotected again
    }

    [Fact]
    public async Task Retry_after_a_lost_connection_sets_the_tenant_on_the_new_connection()
    {
        var tenant = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using (var db = _fixture.CreateAppContext(tenant))
        {
            db.Orders.Add(new Order { Total = 7 });
            await db.SaveChangesAsync();
        }

        // Own application name: own pool, and only these sessions are terminated below
        var name = "retry_" + Guid.NewGuid().ToString("N")[..8];
        var connectionString = new NpgsqlConnectionStringBuilder(_fixture.AppConnectionString)
            { ApplicationName = name }.ConnectionString;
        await using var context = _fixture.CreateAppContext(tenant,
            configure: o => o.UseNpgsql(connectionString, n => n.EnableRetryOnFailure(3)));
        Assert.NotEmpty(await context.Orders.ToListAsync());

        // The server ends the session; the pooled connection is dead
        Assert.Equal(1L, await TestDatabase.ScalarAsync(_fixture.OwnerConnectionString,
            $"SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE application_name = '{name}'"));

        // The retry opens a new physical connection, and the interceptor sets the tenant again
        var orders = await context.Orders.ToListAsync();
        Assert.NotEmpty(orders);
        Assert.All(orders, o => Assert.Equal(tenant.CurrentTenantId, o.TenantId));
    }

    [Fact]
    public async Task Explicit_transaction_with_a_retrying_strategy_runs_inside_the_strategy()
    {
        var tenant = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var db = _fixture.CreateAppContext(tenant,
            configure: o => o.UseNpgsql(_fixture.AppConnectionString, n => n.EnableRetryOnFailure(3)));

        // Without the strategy EF refuses the first command in the transaction: a retry could
        // not repeat the whole transaction
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Orders.ToListAsync();
        });
        Assert.Contains("user-initiated transactions", ex.Message);

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Orders.Add(new Order { Total = 1 });
            db.Orders.Add(new Order { Total = 2 });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Orders.CountAsync(o => o.TenantId == tenant.CurrentTenantId));
    }
}
