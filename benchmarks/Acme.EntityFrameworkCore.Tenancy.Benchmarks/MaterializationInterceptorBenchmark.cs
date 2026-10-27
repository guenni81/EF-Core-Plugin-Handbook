using Acme.EntityFrameworkCore.Tenancy;
using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

/// <summary>A materialization interceptor that does nothing: what EF's interception costs.</summary>
public sealed class EmptyMaterializationInterceptor : IMaterializationInterceptor
{
    public InterceptionResult<object> CreatingInstance(
        MaterializationInterceptionData materializationData, InterceptionResult<object> result)
        => result;
}

/// <summary>
/// Cost of VerifyMaterializedTenants() when reading: the same query with and without the
/// TenantMaterializationInterceptor, against PostgreSQL in Docker (Testcontainers). A third
/// variant with an empty interceptor separates EF's interception path from the plugin's
/// check. The connection stays open, so the session round trip of the connection interceptor
/// is not measured, only reading and materializing the rows.
/// </summary>
[MemoryDiagnoser]
public class MaterializationInterceptorBenchmark
{
    private const int StoredRows = 1000;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private BenchmarkContext _plain = null!;
    private BenchmarkContext _verifying = null!;
    private BenchmarkContext _intercepted = null!;

    /// <summary>Rows per query.</summary>
    [Params(10, 100, 1000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        await _container.StartAsync();

        // All rows belong to the tenant of the provider, so the verification never throws.
        // The container's superuser bypasses RLS; the benchmark measures the client side.
        var tenants = new FixedTenantProvider();
        var services = new ServiceCollection().AddSingleton<ITenantProvider>(tenants).BuildServiceProvider();
        _plain = Create(services, verify: false);
        _verifying = Create(services, verify: true);
        _intercepted = Create(services, verify: false, new EmptyMaterializationInterceptor());

        await _plain.Database.EnsureCreatedAsync();
        for (var i = 0; i < StoredRows; i++)
            _plain.Orders.Add(new Order { Total = i });
        await _plain.SaveChangesAsync();
        _plain.ChangeTracker.Clear();

        await _plain.Database.OpenConnectionAsync();
        await _verifying.Database.OpenConnectionAsync();
        await _intercepted.Database.OpenConnectionAsync();
        _ = await CountAsync(_plain);               // compile the query outside the measurement
        _ = await CountAsync(_verifying);
        _ = await CountAsync(_intercepted);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _plain.DisposeAsync();
        await _verifying.DisposeAsync();
        await _intercepted.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Baseline: the plugin without VerifyMaterializedTenants().</summary>
    [Benchmark(Baseline = true)]
    public Task<int> Read() => CountAsync(_plain);

    /// <summary>The same query through an interceptor that does nothing.</summary>
    [Benchmark]
    public Task<int> ReadIntercepted() => CountAsync(_intercepted);

    /// <summary>The same query; the interceptor checks the tenant of every row.</summary>
    [Benchmark]
    public Task<int> ReadAndVerify() => CountAsync(_verifying);

    // Materializes the rows; only the count leaves the method (BenchmarkDotNet's generated
    // code cannot name the type Order, which clashes with its namespace BenchmarkDotNet.Order)
    private async Task<int> CountAsync(BenchmarkContext db)
        => (await db.Orders.AsNoTracking().OrderBy(o => o.Id).Take(Rows).ToListAsync()).Count;

    private BenchmarkContext Create(IServiceProvider services, bool verify,
        params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<BenchmarkContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseAcmeTenancy(services, t => t.VerifyMaterializedTenants(verify))
            .AddInterceptors(interceptors)
            .Options);
}
