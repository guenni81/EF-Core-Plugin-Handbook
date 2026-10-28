using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Tests.Mappings;

/// <summary>Tenant-owned root of a hierarchy; all types share the root's container.</summary>
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

/// <summary>Owner of embedded owned types.</summary>
public class Basket : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public Address Delivery { get; set; } = new();
    public List<BasketLine> Lines { get; set; } = [];
}

/// <summary>Owned reference with a TenantId value that is not a tenant property.</summary>
public class Address
{
    public string City { get; set; } = "";
    public TenantId ApprovedBy { get; set; }
}

/// <summary>Owned collection whose type even implements ITenantOwned.</summary>
public class BasketLine : ITenantOwned
{
    public string Sku { get; set; } = "";
    public TenantId TenantId { get; set; }
}

public sealed class MappingContext : DbContext
{
    public MappingContext(DbContextOptions<MappingContext> options)
        : base(options)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Basket> Baskets => Set<Basket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultContainer("Mappings");
        modelBuilder.Entity<CardPayment>();
        modelBuilder.Entity<TransferPayment>();
        modelBuilder.Entity<Basket>(b =>
        {
            b.OwnsOne(x => x.Delivery);
            b.OwnsMany(x => x.Lines);
        });
        modelBuilder.HasTenantQueryFilters(this);
    }

    /// <summary>Creates a context; the application services supply the tenant provider.</summary>
    public static MappingContext Create(string connectionString, IServiceProvider services,
        Action<CosmosDbContextOptionsBuilder>? cosmos = null)
        => new(new DbContextOptionsBuilder<MappingContext>()
            .UseCosmos(connectionString, "shop", c =>
            {
                cosmos?.Invoke(c);
                c.UseAcmeTenancy(services);
            })
            .Options);

    /// <summary>Application services with the given tenant provider.</summary>
    public static IServiceProvider Services(ITenantProvider tenants)
        => new ServiceCollection().AddSingleton(tenants).BuildServiceProvider();
}

/// <summary>Hierarchies and owned types in the model the Cosmos package builds (no database).</summary>
public class MappingModelTests
{
    private static readonly IServiceProvider Services = MappingContext.Services(new MutableTenantProvider());

    private static MappingContext Offline() => MappingContext.Create(Contexts.OfflineConnectionString, Services);

    [Fact]
    public void Hierarchy_shares_the_root_partition_key_filter_and_tenant_annotation()
    {
        using var ctx = Offline();
        var root = ctx.Model.FindEntityType(typeof(Payment))!;

        Assert.Equal(["TenantId"], root.GetPartitionKeyPropertyNames());
        Assert.NotNull(root.FindDeclaredQueryFilter(TenancyModelBuilderExtensions.QueryFilterKey));
        foreach (var type in new[] { typeof(CardPayment), typeof(TransferPayment) })
        {
            var derived = ctx.Model.FindEntityType(type)!;
            Assert.Equal(["TenantId"], derived.GetPartitionKeyPropertyNames());   // from the root
            Assert.Equal("TenantId", derived.GetTenantPropertyName());           // what the interceptors read
            Assert.Empty(derived.GetDeclaredQueryFilters());                      // the root's filter applies
            Assert.Equal("Mappings", derived.GetContainer());
        }
    }

    [Fact]
    public void Owned_types_are_embedded_without_partition_key_filter_or_tenant_annotation()
    {
        using var ctx = Offline();
        var basket = ctx.Model.FindEntityType(typeof(Basket))!;
        Assert.Equal(["TenantId"], basket.GetPartitionKeyPropertyNames());
        Assert.Equal("TenantId", basket.GetTenantPropertyName());

        foreach (var owned in basket.GetNavigations().Select(n => n.TargetEntityType))
        {
            Assert.True(owned.IsOwned());
            Assert.Null(owned.GetContainer());                                  // in the owner's document
            Assert.Empty(owned.GetPartitionKeyPropertyNames());
            Assert.Empty(owned.GetDeclaredQueryFilters());
            // Not a tenant property of its own, even for an ITenantOwned owned type: the
            // document is isolated through its owner, the interceptors leave the value alone
            Assert.Null(owned.GetTenantPropertyName());
        }
    }
}

/// <summary>Hierarchies and owned types against the emulator (preview, separate CI job).</summary>
[Trait("Category", "Emulator")]
public class MappingEmulatorTests : IClassFixture<CosmosFixture>
{
    private readonly CosmosFixture _fixture;

    public MappingEmulatorTests(CosmosFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    // One application container, as in an application: the tenant changes, the services do not
    private readonly MutableTenantProvider _tenants = new();

    private MappingContext Create(TenantId? tenant)
    {
        _tenants.CurrentTenantId = tenant;
        return MappingContext.Create(_fixture.ConnectionString, _services ??= MappingContext.Services(_tenants), c => c
            .ConnectionMode(ConnectionMode.Gateway)
            .HttpClientFactory(() => _fixture.HttpClient)
            .LimitToEndpoint());
    }

    private IServiceProvider? _services;

    [Fact]
    public async Task Derived_types_and_owned_types_are_stored_with_the_tenant_and_isolated()
    {
        await using (var setup = Create(null))
            await setup.Database.EnsureCreatedAsync();

        TenantId a = TenantId.New(), b = TenantId.New();
        foreach (var tenant in new[] { a, b })
        {
            await using var db = Create(tenant);
            db.Payments.AddRange(
                new CardPayment { Id = Guid.NewGuid(), Card = "4111" },
                new TransferPayment { Id = Guid.NewGuid(), Iban = "DE00" });
            db.Baskets.Add(new Basket
            {
                Id = Guid.NewGuid(),
                Delivery = new Address { City = "Berlin" },
                Lines = [new BasketLine { Sku = "A-1" }],
            });
            await db.SaveChangesAsync();
        }

        await using (var read = Create(a))
        {
            var payments = await read.Payments.ToListAsync();
            Assert.Equal(2, payments.Count);
            Assert.All(payments, p => Assert.Equal(a, p.TenantId));             // set by the interceptor
            Assert.Single(await read.Payments.OfType<TransferPayment>().ToListAsync());
            var basket = Assert.Single(await read.Baskets.ToListAsync());
            Assert.Equal(("Berlin", "A-1"), (basket.Delivery.City, basket.Lines.Single().Sku));
            Assert.Equal(TenantId.Empty, basket.Lines.Single().TenantId);       // owned: left alone
        }

        await using var none = Create(null);
        Assert.Empty(await none.Payments.ToListAsync());
    }
}
