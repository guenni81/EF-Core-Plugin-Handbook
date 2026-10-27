using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// The optional materialization check against a real database: it detects rows of another
/// tenant where row level security does not take effect, and it has limits
/// (chapter "Materialization: from rows to objects").
/// </summary>
public class MaterializationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public MaterializationTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private async Task<TenantId> CreateTenantWithOrdersAsync(int count)
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var db = _fixture.CreateAppContext(tenants);

        for (var i = 0; i < count; i++)
            db.Orders.Add(new Order { Total = i + 1 });

        await db.SaveChangesAsync();
        return tenants.CurrentTenantId!.Value;
    }

    /// <summary>An open connection whose session still carries <paramref name="tenant"/>.</summary>
    private async Task<NpgsqlConnection> OpenConnectionWithSessionAsync(TenantId tenant)
    {
        var connection = new NpgsqlConnection(_fixture.AppConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant, false)", connection);
        command.Parameters.AddWithValue("tenant", tenant.ToString());
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    [Fact]
    public async Task Own_rows_pass_with_tracking_without_tracking_and_raw_sql()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        await CreateTenantWithOrdersAsync(1);

        await using var db = _fixture.CreateAppContext(
            new MutableTenantProvider { CurrentTenantId = a }, t => t.VerifyMaterializedTenants());

        Assert.Equal(2, (await db.Orders.ToListAsync()).Count);
        Assert.Equal(2, (await db.Orders.AsNoTracking().ToListAsync()).Count);
        Assert.Equal(2, (await db.Orders.FromSql($"SELECT * FROM \"Orders\"").ToListAsync()).Count);
    }

    [Fact]
    public async Task Rows_of_another_tenant_are_rejected_instead_of_returned()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        var b = await CreateTenantWithOrdersAsync(1);

        // The situation from ConnectionLifetimeTests: EF does not open the connection, the
        // session interceptor does not run, and the database returns the rows of tenant B
        await using var connection = await OpenConnectionWithSessionAsync(b);
        await using var db = _fixture.CreateAppContext(
            connection, new MutableTenantProvider { CurrentTenantId = a },
            t => t.VerifyMaterializedTenants());

        await Assert.ThrowsAsync<TenancyException>(() => db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Projections_of_columns_are_not_materialized_and_not_checked()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        var b = await CreateTenantWithOrdersAsync(1);

        await using var connection = await OpenConnectionWithSessionAsync(b);
        await using var db = _fixture.CreateAppContext(
            connection, new MutableTenantProvider { CurrentTenantId = a },
            t => t.VerifyMaterializedTenants());

        // No entity, no materializer, no check: the limit of this extension point
        var totals = await db.Orders.Select(o => o.Total).ToListAsync();

        Assert.Equal([1m], totals);
    }

    [Fact]
    public async Task Singleton_that_remembers_the_tenant_misses_the_leak_and_rejects_own_rows()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        var b = await CreateTenantWithOrdersAsync(1);

        // One shared instance, as it must be for a singleton interceptor
        var tenantsOf = new ConditionalWeakTable<DbContext, MutableTenantProvider>();
        var remembering = new RememberingTenantInterceptor(
            context => tenantsOf.TryGetValue(context, out var t) ? t.CurrentTenantId : null);

        ShopContext Create(MutableTenantProvider tenants, NpgsqlConnection? connection = null)
        {
            var db = connection is null
                ? _fixture.CreateAppContext(tenants, configure: o => o.AddInterceptors(remembering))
                : _fixture.CreateAppContext(connection, tenants,
                    configure: o => o.AddInterceptors(remembering));
            tenantsOf.Add(db, tenants);
            return db;
        }

        // First request: tenant A. The interceptor remembers A.
        await using (var first = Create(new MutableTenantProvider { CurrentTenantId = a }))
            Assert.Equal(2, (await first.Orders.ToListAsync()).Count);

        // Second request: tenant B reads its own rows and gets an error for tenant A's data
        await using (var second = Create(new MutableTenantProvider { CurrentTenantId = b }))
            await Assert.ThrowsAsync<TenancyException>(() => second.Orders.ToListAsync());

        // Worse: a context for B on a connection that still carries A's session returns A's
        // rows, and the remembered tenant A lets them through
        await using var connection = await OpenConnectionWithSessionAsync(a);
        await using var leaking = Create(new MutableTenantProvider { CurrentTenantId = b }, connection);
        var leaked = await leaking.Orders.ToListAsync();

        Assert.Equal(2, leaked.Count);
        Assert.All(leaked, o => Assert.Equal(a, o.TenantId));
    }

    /// <summary>
    /// WRONG: keeps request data in a singleton. EF uses one instance for all contexts and
    /// threads, so the tenant of the first context it sees stays for all later ones.
    /// </summary>
    private sealed class RememberingTenantInterceptor : IMaterializationInterceptor
    {
        private readonly Func<DbContext, TenantId?> _tenantOf;
        private TenantId? _tenant;                       // per-request state in a singleton

        public RememberingTenantInterceptor(Func<DbContext, TenantId?> tenantOf)
        {
            _tenantOf = tenantOf;
        }

        public InterceptionResult<object> CreatingInstance(
            MaterializationInterceptionData materializationData,
        InterceptionResult<object> result)
        {
            // "Optimization": look up the tenant only once
            _tenant ??= _tenantOf(materializationData.Context);

            if (materializationData.GetPropertyValue<TenantId>(nameof(Order.TenantId)) != _tenant)
                throw new TenancyException("Row of another tenant.");

            return result;
        }
    }
}
