using Acme.EntityFrameworkCore.Tenancy;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;

/// <summary>Tenant-owned entity for the benchmark.</summary>
public class Order : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Context with the plugin; no connection is ever opened.</summary>
public sealed class BenchmarkContext : DbContext
{
    public BenchmarkContext(DbContextOptions<BenchmarkContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
}

/// <summary>Always the same tenant: the benchmark measures the plugin, not the provider.</summary>
public sealed class FixedTenantProvider : ITenantProvider
{
    public TenantId? CurrentTenantId { get; } = TenantId.New();
}

/// <summary>
/// Cost of the SaveChanges logic per save: what the plugin adds on top of the change tracking
/// EF does anyway. Runs without a database; the connection interceptor's round trip is a
/// database cost and has to be measured against a real server.
/// </summary>
[MemoryDiagnoser]
public class SaveChangesInterceptorBenchmark
{
    private BenchmarkContext _db = null!;
    private TenantSaveChangesInterceptor _interceptor = null!;

    /// <summary>Number of added entities per save.</summary>
    [Params(1, 100)]
    public int Entities { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _interceptor = new TenantSaveChangesInterceptor(new FixedTenantProvider());
        _db = new BenchmarkContext(new DbContextOptionsBuilder<BenchmarkContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseAcmeTenancy()
            .Options);
        _ = _db.Model;                  // build the model outside the measurement
    }

    [GlobalCleanup]
    public void Cleanup() => _db.Dispose();

    /// <summary>Baseline: track the entities and enumerate them, as SaveChanges would.</summary>
    [Benchmark(Baseline = true)]
    public int TrackOnly()
    {
        AddOrders();
        var count = _db.ChangeTracker.Entries().Count();
        _db.ChangeTracker.Clear();
        return count;
    }

    /// <summary>The same plus the plugin's SaveChanges logic (sets and checks the tenant).</summary>
    [Benchmark]
    public int TrackAndApplyTenant()
    {
        AddOrders();
        _interceptor.ApplyTenant(_db);
        var count = _db.ChangeTracker.Entries().Count();
        _db.ChangeTracker.Clear();
        return count;
    }

    private void AddOrders()
    {
        for (var i = 0; i < Entities; i++)
            _db.Orders.Add(new Order { Total = i });
    }
}
