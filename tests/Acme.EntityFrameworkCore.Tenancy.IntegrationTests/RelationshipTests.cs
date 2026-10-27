using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests.Relationships;

/// <summary>Tenant-owned customer.</summary>
public class Customer : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public string Email { get; set; } = "";
    public List<Order> Orders { get; set; } = [];
}

/// <summary>Tenant-owned order of a customer.</summary>
public class Order : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

/// <summary>Tenant-owned item of an order; refers to shared master data.</summary>
public class OrderItem : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
}

/// <summary>Master data shared by all tenants: not tenant-owned, no RLS.</summary>
public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Relationships as EF creates them by default: foreign keys on the ID alone.</summary>
public sealed class NaiveContext : DbContext
{
    public NaiveContext(DbContextOptions<NaiveContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> Items => Set<OrderItem>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Customer>().HasIndex(c => c.Email).IsUnique();   // global
}

/// <summary>Tenant-safe relationships: the tenant is part of every reference and unique key.</summary>
public sealed class TenantSafeContext : DbContext
{
    public TenantSafeContext(DbContextOptions<TenantSafeContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> Items => Set<OrderItem>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(b =>
        {
            b.HasAlternateKey(c => new { c.TenantId, c.Id });
            b.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();   // unique per tenant
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.HasAlternateKey(o => new { o.TenantId, o.Id });
            b.HasOne(o => o.Customer).WithMany(c => c.Orders)
                .HasForeignKey(o => new { o.TenantId, o.CustomerId })
                .HasPrincipalKey(c => new { c.TenantId, c.Id })
                .OnDelete(DeleteBehavior.Restrict);                   // customers are not cascaded
        });

        modelBuilder.Entity<OrderItem>()
            .HasOne(i => i.Order).WithMany(o => o.Items)
            .HasForeignKey(i => new { i.TenantId, i.OrderId })
            .HasPrincipalKey(o => new { o.TenantId, o.Id })
            .OnDelete(DeleteBehavior.Cascade);                         // items belong to the order
    }
}

/// <summary>
/// Row visibility versus referential consistency against PostgreSQL. Foreign key, unique and
/// primary key checks bypass row security (PostgreSQL documentation, "Row Security Policies").
/// </summary>
public class RelationshipTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public RelationshipTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private static readonly Func<DbContextOptions<NaiveContext>, NaiveContext> Naive = o => new(o);
    private static readonly Func<DbContextOptions<TenantSafeContext>, TenantSafeContext> Safe = o => new(o);

    private sealed record Setup(string App, Guid Product, TenantId A, TenantId B);

    private async Task<Setup> CreateAsync<TContext>(Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        var (owner, app) = await TestDatabase.CreateAsync(_fixture);
        var product = Guid.NewGuid();
        await using (var db = create(TestDatabase.Options<TContext>(owner)))
        {
            await TestDatabase.CreateSchemaAsync(db);
            db.Add(new Product { Id = product, Name = "Shared" });   // master data, no tenant
            await db.SaveChangesAsync();
        }

        return new Setup(app, product, TenantId.New(), TenantId.New());
    }

    /// <summary>A customer with one order, created by the given tenant through the app role.</summary>
    private static async Task<Guid> CreateOrderAsync<TContext>(
        Func<DbContextOptions<TContext>, TContext> create, string app, TenantId tenant)
        where TContext : DbContext
    {
        await using var db = create(TestDatabase.Options<TContext>(
            app, new MutableTenantProvider { CurrentTenantId = tenant }));
        var order = new Order
        {
            Id = Guid.NewGuid(),
            Customer = new Customer { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@example.com" },
        };
        db.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>Tenant adds an item that refers to the given order ID.</summary>
    private static async Task AddItemAsync<TContext>(
        Func<DbContextOptions<TContext>, TContext> create, Setup setup, TenantId tenant, Guid orderId)
        where TContext : DbContext
    {
        await using var db = create(TestDatabase.Options<TContext>(
            setup.App, new MutableTenantProvider { CurrentTenantId = tenant }));
        db.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = orderId, ProductId = setup.Product });
        await db.SaveChangesAsync();
    }

    private static string SqlState(DbUpdateException ex) => ((PostgresException)ex.InnerException!).SqlState;

    [Fact]
    public async Task Naive_foreign_key_accepts_a_reference_to_a_foreign_row_and_reveals_its_existence()
    {
        var setup = await CreateAsync(Naive);
        var foreignOrder = await CreateOrderAsync(Naive, setup.App, setup.B);

        // The FK check bypasses RLS: tenant A may point at B's order, which A cannot even see
        await AddItemAsync(Naive, setup, setup.A, foreignOrder);

        // A non-existent ID fails, an existing foreign one did not: existence leaks
        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => AddItemAsync(Naive, setup, setup.A, Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, SqlState(ex));

        await using var db = new NaiveContext(TestDatabase.Options<NaiveContext>(
            setup.App, new MutableTenantProvider { CurrentTenantId = setup.A }));
        Assert.Equal(1, await db.Items.CountAsync());              // A's own row is visible ...
        // ... but its parent is not: Include of a required navigation is an INNER JOIN, and the
        // item vanishes from the result
        Assert.Empty(await db.Items.Include(i => i.Order).ToListAsync());
    }

    [Fact]
    public async Task Naive_cascade_deletes_rows_of_another_tenant()
    {
        var setup = await CreateAsync(Naive);
        var orderOfA = await CreateOrderAsync(Naive, setup.App, setup.A);
        await AddItemAsync(Naive, setup, setup.B, orderOfA);   // B's item

        await using (var db = new NaiveContext(TestDatabase.Options<NaiveContext>(
                         setup.App, new MutableTenantProvider { CurrentTenantId = setup.A })))
        {
            db.Orders.Remove(await db.Orders.SingleAsync());
            await db.SaveChangesAsync();
        }

        // The cascade runs as a referential action and bypasses RLS: B lost its item
        await using var b = new NaiveContext(TestDatabase.Options<NaiveContext>(
            setup.App, new MutableTenantProvider { CurrentTenantId = setup.B }));
        Assert.Empty(await b.Items.ToListAsync());
    }

    [Fact]
    public async Task Composite_foreign_key_rejects_references_across_tenants_like_missing_rows()
    {
        var setup = await CreateAsync(Safe);
        var foreignOrder = await CreateOrderAsync(Safe, setup.App, setup.B);
        var ownOrder = await CreateOrderAsync(Safe, setup.App, setup.A);

        // (A, B's order ID) does not exist: same error as for an unknown ID, nothing leaks
        var foreign = await Assert.ThrowsAsync<DbUpdateException>(
            () => AddItemAsync(Safe, setup, setup.A, foreignOrder));
        var unknown = await Assert.ThrowsAsync<DbUpdateException>(
            () => AddItemAsync(Safe, setup, setup.A, Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, SqlState(foreign));
        Assert.Equal(SqlState(unknown), SqlState(foreign));

        // Within the tenant everything works, including shared master data
        await AddItemAsync(Safe, setup, setup.A, ownOrder);
        await using var db = new TenantSafeContext(TestDatabase.Options<TenantSafeContext>(
            setup.App, new MutableTenantProvider { CurrentTenantId = setup.A }));
        var item = await db.Items.Include(i => i.Order).Include(i => i.Product).SingleAsync();
        Assert.Equal(ownOrder, item.Order!.Id);
        Assert.Equal("Shared", item.Product!.Name);
    }

    [Fact]
    public async Task Global_unique_index_reveals_values_of_other_tenants_per_tenant_index_does_not()
    {
        static async Task AddCustomerAsync<TContext>(
            Func<DbContextOptions<TContext>, TContext> create, string app, TenantId tenant)
            where TContext : DbContext
        {
            await using var db = create(TestDatabase.Options<TContext>(
                app, new MutableTenantProvider { CurrentTenantId = tenant }));
            db.Add(new Customer { Id = Guid.NewGuid(), Email = "office@example.com" });
            await db.SaveChangesAsync();
        }

        var naive = await CreateAsync(Naive);
        await AddCustomerAsync(Naive, naive.App, naive.B);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => AddCustomerAsync(Naive, naive.App, naive.A));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, SqlState(ex));   // A learns B's customer

        var safe = await CreateAsync(Safe);
        await AddCustomerAsync(Safe, safe.App, safe.B);
        await AddCustomerAsync(Safe, safe.App, safe.A);
    }
}
