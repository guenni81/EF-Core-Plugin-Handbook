using Acme.EntityFrameworkCore.Tenancy;
using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

/// <summary>The contexts compared: EF Core alone, with the plugin, with its query filter.</summary>
public enum QuerySetup
{
    EFCore,
    Tenancy,
    TenancyWithQueryFilter,
}

/// <summary>The same entity for EF Core without the plugin, which maps TenantId otherwise.</summary>
public sealed class PlainBenchmarkContext : DbContext
{
    public PlainBenchmarkContext(DbContextOptions<PlainBenchmarkContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Order>().Property(o => o.TenantId)
            .HasConversion(t => t.Value, g => new TenantId(g));
}

/// <summary>
/// What the plugin adds to query compilation (translator plugins, evaluatable expression filter,
/// the replaced SQL generator and nullability processor, optionally the query filter) and
/// whether the query cache hides it afterwards. ToQueryString runs the whole pipeline up to
/// the SQL without a connection; no database is needed.
/// </summary>
[MemoryDiagnoser]
public class QueryCompilationBenchmark
{
    private DbContext _db = null!;
    private MemoryCache _queryCache = null!;
    private decimal _minimum;

    [Params(QuerySetup.EFCore, QuerySetup.Tenancy, QuerySetup.TenancyWithQueryFilter)]
    public QuerySetup Setup { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        const string connectionString = "Host=localhost;Database=unused";  // never opened
        _db = Setup == QuerySetup.EFCore
            ? new PlainBenchmarkContext(new DbContextOptionsBuilder<PlainBenchmarkContext>()
                .UseNpgsql(connectionString)
                .Options)
            : new BenchmarkContext(new DbContextOptionsBuilder<BenchmarkContext>()
                .UseNpgsql(connectionString)
                .UseAcmeTenancy(t => t.ApplyQueryFilter(Setup == QuerySetup.TenancyWithQueryFilter))
                .Options);
        _ = _db.Model;                                              // model outside the measurement
        _queryCache = (MemoryCache)_db.GetService<IMemoryCache>();  // EF's compiled query cache
    }

    [GlobalCleanup]
    public void Cleanup() => _db.Dispose();

    /// <summary>
    /// First execution of the query shape: the cache is emptied before every call, so EF
    /// preprocesses, translates and generates SQL every time.
    /// </summary>
    [Benchmark]
    public string Compile()
    {
        _queryCache.Clear();
        return Query();
    }

    /// <summary>The same shape with a new parameter value: a cache hit.</summary>
    [Benchmark]
    public string CacheHit()
    {
        _minimum++;
        return Query();
    }

    private string Query()
        => _db.Set<Order>().Where(o => o.Total > _minimum).OrderBy(o => o.Id).Take(10).ToQueryString();
}
