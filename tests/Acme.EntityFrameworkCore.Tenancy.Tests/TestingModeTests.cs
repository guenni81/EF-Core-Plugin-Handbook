using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// Test mode for applications (chapter "Test strategy"): business logic tests on SQLite or
/// InMemory get the application-side behavior of the plugin, without row level security.
/// </summary>
public class TestingModeTests
{
    private sealed class MutableTenantProvider : ITenantProvider
    {
        public TenantId? CurrentTenantId { get; set; }
    }

    /// <summary>An application context as a consumer writes it, with the parameter filter.</summary>
    private sealed class AppContext : DbContext
    {
        public AppContext(DbContextOptions<AppContext> options)
            : base(options)
        {
        }

        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.HasTenantQueryFilters(this);
    }

    private static IServiceProvider Services(MutableTenantProvider tenants)
        => new ServiceCollection().AddSingleton<ITenantProvider>(tenants).BuildServiceProvider();

    private static AppContext Sqlite(SqliteConnection connection, MutableTenantProvider tenants)
        => new(new DbContextOptionsBuilder<AppContext>()
            .UseSqlite(connection)
            .UseAcmeTenancyForTesting(Services(tenants))
            .Options);

    [Fact]
    public void The_postgresql_package_still_refuses_sqlite()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        var options = new DbContextOptionsBuilder<AppContext>()
            .UseSqlite(connection)
            .UseAcmeTenancy()
            .Options;

        // The context is disposed inside: Dispose would run into the same validation again
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var ctx = new AppContext(options);
            _ = ctx.Model;
        });
        Assert.Contains("UseNpgsql()", ex.Message);
    }

    [Fact]
    public async Task Saving_sets_the_tenant_and_queries_see_only_the_current_tenant()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();                 // the in-memory database lives with it
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var ctx = Sqlite(connection, tenants);
        await ctx.Database.EnsureCreatedAsync();
        var tenantA = tenants.CurrentTenantId.Value;

        ctx.Orders.Add(new Order { Total = 1 });      // TenantId left empty on purpose
        await ctx.SaveChangesAsync();
        tenants.CurrentTenantId = TenantId.New();
        ctx.Orders.Add(new Order { Total = 2 });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // The SaveChanges interceptor of the core filled the tenant
        var all = await ctx.Orders.IgnoreQueryFilters().OrderBy(o => o.Total).ToListAsync();
        Assert.Equal(tenantA, all[0].TenantId);

        // The parameter filter follows the current tenant from query to query
        Assert.Equal(2m, Assert.Single(await ctx.Orders.ToListAsync()).Total);
        tenants.CurrentTenantId = tenantA;
        Assert.Equal(1m, Assert.Single(await ctx.Orders.ToListAsync()).Total);
    }

    [Fact]
    public async Task Works_with_the_inmemory_provider_too()
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var ctx = new AppContext(new DbContextOptionsBuilder<AppContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseAcmeTenancyForTesting(Services(tenants))
            .Options);

        ctx.Orders.Add(new Order { Total = 1 });
        await ctx.SaveChangesAsync();

        Assert.Equal("TenantId", ctx.Model.FindEntityType(typeof(Order))!.GetTenantPropertyName());
        Assert.Equal(1, await ctx.Orders.CountAsync());
        tenants.CurrentTenantId = TenantId.New();
        Assert.Equal(0, await ctx.Orders.CountAsync());
    }

    [Fact]
    public void Test_mode_refuses_a_provider_with_real_isolation()
    {
        var options = new DbContextOptionsBuilder<AppContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseAcmeTenancyForTesting(Services(new MutableTenantProvider()))
            .Options;

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var ctx = new AppContext(options);
            _ = ctx.Model;
        });
        Assert.Contains("UseAcmeTenancy()", ex.Message);
    }
}
