using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Runtime;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests.MappingIsolation;

/// <summary>Tenant-owned root of a TPH hierarchy.</summary>
public abstract class Payment : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
}

public class CardPayment : Payment
{
    public string Card { get; set; } = "";
}

public class TransferPayment : Payment
{
    public string Iban { get; set; } = "";
}

/// <summary>Owner of an owned reference in its table; shares the table with <see cref="BasketInfo"/>.</summary>
public class Basket : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public BasketAddress Delivery { get; set; } = new();
    public BasketInfo? Info { get; set; }
}

public class BasketAddress
{
    public string City { get; set; } = "";
}

/// <summary>Table splitting: no tenant property of its own, lives in the table of <see cref="Basket"/>.</summary>
public class BasketInfo
{
    public Guid Id { get; set; }
    public string Comment { get; set; } = "";
}

/// <summary>Owner of an owned collection stored as JSON.</summary>
public class Shipment : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public List<ShipmentLine> Lines { get; set; } = [];
}

public class ShipmentLine
{
    public string Sku { get; set; } = "";
}

/// <summary>Both sides of a many-to-many relationship with an explicit join entity.</summary>
public class Project : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public List<Label> Labels { get; set; } = [];
}

public class Label : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public List<Project> Projects { get; set; } = [];
}

/// <summary>The supported mappings of the usage contract in one model.</summary>
public sealed class SupportedMappingsContext : DbContext
{
    public SupportedMappingsContext(DbContextOptions<SupportedMappingsContext> options)
        : base(options)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Basket> Baskets => Set<Basket>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Label> Labels => Set<Label>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CardPayment>();
        modelBuilder.Entity<TransferPayment>();
        modelBuilder.Entity<Basket>(b =>
        {
            b.ToTable("Baskets");
            b.OwnsOne(x => x.Delivery);
            b.HasOne(x => x.Info).WithOne().HasForeignKey<BasketInfo>(i => i.Id);
        });
        modelBuilder.Entity<BasketInfo>().ToTable("Baskets");
        modelBuilder.Entity<Shipment>().OwnsMany(s => s.Lines, l => l.ToJson());
        modelBuilder.Entity<Project>().HasMany(p => p.Labels).WithMany(l => l.Projects)
            .UsingEntity("ProjectLabel", j => j.Property<TenantId>("TenantId").IsTenantProperty());
    }
}

/// <summary>
/// The supported table mappings against a real SQL Server, connected as the app login: every
/// table gets a security policy, and every tenant sees only its rows (chapter "Without SQL: what
/// carries over to Cosmos DB", usage contract).
/// </summary>
public class SqlServerMappingIsolationTests : IClassFixture<SqlServerFixture>
{
    private static readonly string[] Tables = ["Payments", "Baskets", "Shipments", "Projects", "Labels", "ProjectLabel"];

    private readonly SqlServerFixture _fixture;

    public SqlServerMappingIsolationTests(SqlServerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private static SupportedMappingsContext Create(string connectionString, ITenantProvider? tenants)
    {
        var builder = new DbContextOptionsBuilder<SupportedMappingsContext>()
            .UseSqlServer(connectionString, sql => sql.UseAcmeTenancy())
            .EnableServiceProviderCaching(false);
        if (tenants is not null)
            builder.AddInterceptors(new SqlServerTenantSessionInterceptor(tenants), new TenantSaveChangesInterceptor(tenants));
        return new SupportedMappingsContext(builder.Options);
    }

    private static async Task<int> CountAsync(SupportedMappingsContext db, string table)
    {
        await using var command = (SqlCommand)db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT count(*) FROM [{table}]";      // raw SQL: only the policy filters
        return (int)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Every_table_of_the_supported_mappings_is_protected()
    {
        var (owner, app) = await SqlServerMigrationTests.CreateDatabaseAsync(_fixture);
        await using (var db = Create(owner, tenants: null))
            await db.Database.EnsureCreatedAsync();

        // One security policy per table, and no table without one
        await using (var connection = new SqlConnection(owner))
        {
            await connection.OpenAsync();
            Assert.Equal(Tables.Length, (int)(await SqlServerMigrationTests.Execute(connection,
                "SELECT count(*) FROM sys.tables"))!);
            Assert.Equal(Tables.Length, (int)(await SqlServerMigrationTests.Execute(connection,
                "SELECT count(DISTINCT target_object_id) FROM sys.security_predicates"))!);
        }

        // Tenants A and B each write one row into every table, as the app login
        var tenants = new MutableTenantProvider();
        foreach (var tenant in new[] { TenantId.New(), TenantId.New() })
        {
            tenants.CurrentTenantId = tenant;
            await using var db = Create(app, tenants);
            db.Payments.AddRange(
                new CardPayment { Id = Guid.NewGuid(), Card = "4111" },
                new TransferPayment { Id = Guid.NewGuid(), Iban = "DE00" });
            db.Baskets.Add(new Basket
            {
                Id = Guid.NewGuid(),
                Delivery = new BasketAddress { City = "Berlin" },
                Info = new BasketInfo { Comment = "fragile" },
            });
            db.Shipments.Add(new Shipment { Id = Guid.NewGuid(), Lines = [new ShipmentLine { Sku = "A-1" }] });
            db.Projects.Add(new Project { Id = Guid.NewGuid(), Labels = [new Label { Id = Guid.NewGuid() }] });
            await db.SaveChangesAsync();
        }

        // B sees its rows only, also through the split type, the owned data and the join entity;
        // without a tenant nothing is visible
        foreach (var (tenant, expected) in new[] { (tenants.CurrentTenantId, 1), (null, 0) })
        {
            await using var db = Create(app, new MutableTenantProvider { CurrentTenantId = tenant });
            await db.Database.OpenConnectionAsync();
            Assert.Equal(2 * expected, await CountAsync(db, "Payments"));
            foreach (var table in Tables.Skip(1))
                Assert.Equal(expected, await CountAsync(db, table));

            Assert.Equal(expected, await db.Set<BasketInfo>().CountAsync());
            Assert.Equal(expected, await db.Payments.OfType<TransferPayment>().CountAsync());
            var baskets = await db.Baskets.Include(b => b.Info).ToListAsync();
            Assert.All(baskets, b => Assert.Equal(("Berlin", "fragile"), (b.Delivery.City, b.Info!.Comment)));
            Assert.Equal(expected, (await db.Shipments.ToListAsync()).Sum(s => s.Lines.Count));
            Assert.Equal(expected, (await db.Projects.Include(p => p.Labels).ToListAsync()).Sum(p => p.Labels.Count));
        }
    }

    [Fact]
    public async Task Another_tenant_cannot_update_a_split_row()
    {
        var (owner, app) = await SqlServerMigrationTests.CreateDatabaseAsync(_fixture);
        await using (var db = Create(owner, tenants: null))
            await db.Database.EnsureCreatedAsync();

        // The split type has no tenant property; its row is the basket's row and carries its tenant
        var a = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        var id = Guid.NewGuid();
        await using (var db = Create(app, a))
        {
            db.Baskets.Add(new Basket { Id = id, Info = new BasketInfo { Comment = "a" } });
            await db.SaveChangesAsync();
        }

        // Tenant B cannot reach it: the FILTER predicate hides it from the UPDATE
        await using var other = Create(app, new MutableTenantProvider { CurrentTenantId = TenantId.New() });
        Assert.Equal(0, await other.Set<BasketInfo>().Where(i => i.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Comment, "b")));
        await using var check = Create(app, a);
        Assert.Equal("a", (await check.Set<BasketInfo>().SingleAsync(i => i.Id == id)).Comment);
    }
}
