using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Operations;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>Abstract tenant-owned root of a hierarchy.</summary>
public abstract class Payment : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
}

/// <summary>Concrete type of the <see cref="Payment"/> hierarchy.</summary>
public class CardPayment : Payment
{
    public string Card { get; set; } = "";
}

/// <summary>Second concrete type of the <see cref="Payment"/> hierarchy.</summary>
public class TransferPayment : Payment
{
    public string Iban { get; set; } = "";
}

/// <summary>Tenant-owned owner of an owned reference (same table) and a JSON collection.</summary>
public class Basket : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public BasketAddress Delivery { get; set; } = new();
    public List<BasketLine> Lines { get; set; } = [];
    public List<Label> Labels { get; set; } = [];
}

/// <summary>Owned reference in the owner's table.</summary>
public class BasketAddress
{
    public string City { get; set; } = "";
}

/// <summary>Owned collection stored as JSON.</summary>
public class BasketLine
{
    public string Sku { get; set; } = "";
}

/// <summary>Tenant-owned side of a many-to-many relationship.</summary>
public class Label : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Name { get; set; } = "";
    public List<Basket> Baskets { get; set; } = [];
}

/// <summary>TPC: every concrete type gets its own table, the abstract root none.</summary>
public sealed class TpcContext : DbContext
{
    public TpcContext(DbContextOptions<TpcContext> options)
        : base(options)
    {
    }

    public DbSet<CardPayment> CardPayments => Set<CardPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Payment>().UseTpcMappingStrategy();
}

/// <summary>Supported mappings: TPH, owned reference, JSON collection, join entity with tenant.</summary>
public sealed class SupportedMappingsContext : DbContext
{
    public SupportedMappingsContext(DbContextOptions<SupportedMappingsContext> options)
        : base(options)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Basket> Baskets => Set<Basket>();
    public DbSet<Label> Labels => Set<Label>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CardPayment>();
        modelBuilder.Entity<TransferPayment>();
        modelBuilder.Entity<Basket>(b =>
        {
            b.OwnsOne(x => x.Delivery);
            b.OwnsMany(x => x.Lines, l => l.ToJson());
            b.HasMany(x => x.Labels).WithMany(l => l.Baskets).UsingEntity("BasketLabel",
                j => j.Property<TenantId>("TenantId").IsTenantProperty());
        });
    }
}

/// <summary>
/// Table mappings against a real PostgreSQL with the restricted app role: an unsupported mapping
/// fails before a table exists, and in a supported model every table is protected.
/// </summary>
public class MappingIsolationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public MappingIsolationTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    /// <summary>Creates an empty database and returns owner and app connection strings.</summary>
    private async Task<(string Owner, string App)> CreateDatabaseAsync()
    {
        var name = "mapping_" + Guid.NewGuid().ToString("N")[..12];
        await using (var admin = new NpgsqlConnection(_fixture.OwnerConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }

        return (new NpgsqlConnectionStringBuilder(_fixture.OwnerConnectionString) { Database = name }
                .ConnectionString,
            new NpgsqlConnectionStringBuilder(_fixture.AppConnectionString) { Database = name }
                .ConnectionString);
    }

    private static DbContextOptions<TContext> Options<TContext>(
        string connectionString, ITenantProvider? tenants)
        where TContext : DbContext
    {
        // Without the query filter: only the database protects the rows
        var builder = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connectionString)
            .UseAcmeTenancy(t => t.ApplyQueryFilter(false));

        if (tenants is not null)
            builder.AddInterceptors(
                new TenantConnectionInterceptor(tenants),
                new TenantSaveChangesInterceptor(tenants));

        return builder.Options;
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Tpc_model_fails_before_a_single_table_is_created()
    {
        var (owner, _) = await CreateDatabaseAsync();
        await using var db = new TpcContext(Options<TpcContext>(owner, tenants: null));

        // Before the fix the table CardPayment was created without RLS, and the app role read
        // a foreign row without a tenant. Now the model is rejected while it is built.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.Database.EnsureCreatedAsync());
        Assert.Contains("TPC", ex.Message);

        await using var connection = new NpgsqlConnection(owner);
        await connection.OpenAsync();
        Assert.Equal(0, await ScalarAsync(connection,
            "SELECT count(*) FROM pg_tables WHERE schemaname = 'public'"));
    }

    [Fact]
    public async Task Every_table_of_the_supported_mappings_is_protected()
    {
        var (owner, app) = await CreateDatabaseAsync();
        await using (var db = new SupportedMappingsContext(
                         Options<SupportedMappingsContext>(owner, tenants: null)))
        {
            await db.Database.EnsureCreatedAsync();
            var commands = db.GetService<IMigrationsSqlGenerator>().Generate(
                [new GrantTablePrivilegesOperation { Role = PostgresFixture.AppRole }]);
            await db.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(
                commands, db.GetService<IRelationalConnection>(),
                new MigrationExecutionState(), commitTransaction: true);
        }

        // Every table of this model holds tenant data, so every table must have RLS
        await using (var connection = new NpgsqlConnection(owner))
        {
            await connection.OpenAsync();
            Assert.Equal(4, await ScalarAsync(connection,
                "SELECT count(*) FROM pg_tables WHERE schemaname = 'public'"));
            Assert.Equal(0, await ScalarAsync(connection,
                "SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND NOT rowsecurity"));
        }

        // Tenants A and B each write one row into every table, through the app role
        var tenants = new MutableTenantProvider();
        foreach (var tenant in new[] { TenantId.New(), TenantId.New() })
        {
            tenants.CurrentTenantId = tenant;
            await using var db = new SupportedMappingsContext(
                Options<SupportedMappingsContext>(app, tenants));
            var label = new Label { Id = Guid.NewGuid(), Name = "urgent" };
            db.Payments.Add(new CardPayment { Id = Guid.NewGuid(), Card = "4111" });
            db.Payments.Add(new TransferPayment { Id = Guid.NewGuid(), Iban = "DE00" });
            db.Baskets.Add(new Basket
            {
                Id = Guid.NewGuid(),
                Delivery = new BasketAddress { City = "Berlin" },
                Lines = [new BasketLine { Sku = "A-1" }],
                Labels = [label],
            });
            await db.SaveChangesAsync();
        }

        // Only tenant B's rows remain visible to B, and none without a tenant
        foreach (var (tenant, expected) in new[] { (tenants.CurrentTenantId, 1L), (null, 0L) })
        {
            await using var db = new SupportedMappingsContext(Options<SupportedMappingsContext>(
                app, new MutableTenantProvider { CurrentTenantId = tenant }));
            await db.Database.OpenConnectionAsync();
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();

            Assert.Equal(2 * expected, await ScalarAsync(connection, "SELECT count(*) FROM \"Payments\""));
            Assert.Equal(expected, await ScalarAsync(connection, "SELECT count(*) FROM \"Baskets\""));
            Assert.Equal(expected, await ScalarAsync(connection, "SELECT count(*) FROM \"Labels\""));
            Assert.Equal(expected, await ScalarAsync(connection, "SELECT count(*) FROM \"BasketLabel\""));
        }
    }

    [Fact]
    public void Validator_accepts_the_supported_model()
    {
        using var db = new SupportedMappingsContext(
            Options<SupportedMappingsContext>("Host=unused", tenants: null));

        TenantTableMappingValidator.Validate(db.Model, _ => false);
    }
}
