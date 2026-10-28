using Acme.EntityFrameworkCore.Tenancy;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Runtime;
using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

/// <summary>
/// The connection interceptor's session round trip against PostgreSQL in Docker
/// (chapter "Plugin performance"): a unit of work with several equal queries, either with
/// EF opening and closing the connection per query (one set_config per query) or with the
/// connection held open for the unit of work (one set_config in total).
/// </summary>
/// <remarks>
/// Every variant reads as a restricted role under RLS, with the same tenant, query and rows.
/// The baselines get the tenant without a session command: their role has it as a default
/// (ALTER ROLE ... SET), which every new session and every pool reset restores. The
/// difference between a variant and its baseline is therefore the session command alone.
/// Contexts are created and queries compiled in the setup, outside the measurement. Valid only
/// with one tenant per context: the open connection keeps the tenant it was opened with.
/// </remarks>
[MemoryDiagnoser]
public class SessionRoundtripBenchmark
{
    private const int StoredRows = 100;
    private const string Password = "bench_pw";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly FixedTenantProvider _tenants = new();
    private BenchmarkContext _withInterceptor = null!;
    private BenchmarkContext _withRoleDefault = null!;

    /// <summary>Equal queries per unit of work.</summary>
    [Params(1, 5, 20)]
    public int Operations { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        await _container.StartAsync();
        var owner = _container.GetConnectionString();

        await using (var db = Create(owner, interceptor: true))
        {
            await db.Database.EnsureCreatedAsync();
            // The owner is the container's superuser and bypasses RLS: it writes the rows
            var tenant = _tenants.CurrentTenantId!.Value.Value;
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO \"Orders\" (\"TenantId\", \"Total\") SELECT {tenant}, g FROM generate_series(1, {StoredRows}) g");
            // DDL takes no parameters; only constants and a Guid of the benchmark itself
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE ROLE bench_app LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{Password}';
                CREATE ROLE bench_fixed LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{Password}';
                ALTER ROLE bench_fixed SET app.tenant_id = '{_tenants.CurrentTenantId}';
                GRANT SELECT ON "Orders" TO bench_app, bench_fixed;
                """);
#pragma warning restore EF1002
        }

        _withInterceptor = Create(Login(owner, "bench_app"), interceptor: true);
        _withRoleDefault = Create(Login(owner, "bench_fixed"), interceptor: false);

        // Compile the query and fill both pools outside the measurement; both see all rows
        if (await QueryAsync(_withInterceptor) != 10 || await QueryAsync(_withRoleDefault) != 10)
            throw new InvalidOperationException("The variants do not read the same rows.");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _withInterceptor.DisposeAsync();
        await _withRoleDefault.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Baseline: EF opens and closes per query, the tenant comes from the role.</summary>
    [Benchmark(Baseline = true)]
    public Task<int> PerQueryOpen_RoleDefault() => PerQueryAsync(_withRoleDefault);

    /// <summary>EF opens and closes per query; the interceptor sets the tenant every time.</summary>
    [Benchmark]
    public Task<int> PerQueryOpen_Interceptor() => PerQueryAsync(_withInterceptor);

    /// <summary>Open for the unit of work, the tenant from the role: no session command at all.</summary>
    [Benchmark]
    public Task<int> UnitOfWorkOpen_RoleDefault() => UnitOfWorkAsync(_withRoleDefault);

    /// <summary>Open for the unit of work; the interceptor sets the tenant once.</summary>
    [Benchmark]
    public Task<int> UnitOfWorkOpen_Interceptor() => UnitOfWorkAsync(_withInterceptor);

    private async Task<int> PerQueryAsync(BenchmarkContext db)
    {
        var rows = 0;
        for (var i = 0; i < Operations; i++)
            rows += await QueryAsync(db);
        return rows;
    }

    private async Task<int> UnitOfWorkAsync(BenchmarkContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            return await PerQueryAsync(db);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // The same small query everywhere: ten rows of the tenant, no tracking
    private static async Task<int> QueryAsync(BenchmarkContext db)
        => (await db.Orders.AsNoTracking().OrderBy(o => o.Id).Take(10).ToListAsync()).Count;

    private static string Login(string owner, string role)
        => new NpgsqlConnectionStringBuilder(owner) { Username = role, Password = Password }.ConnectionString;

    // Without the query filter only RLS decides, in both variants alike
    private BenchmarkContext Create(string connectionString, bool interceptor)
    {
        var builder = new DbContextOptionsBuilder<BenchmarkContext>()
            .UseNpgsql(connectionString)
            .UseAcmeTenancy(t => t.ApplyQueryFilter(false));
        if (interceptor)
            builder.AddInterceptors(new TenantConnectionInterceptor(_tenants));
        return new BenchmarkContext(builder.Options);
    }
}
