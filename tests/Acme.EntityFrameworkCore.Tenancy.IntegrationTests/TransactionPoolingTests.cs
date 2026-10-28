using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// UseTransactionPooling() against PostgreSQL (chapter "Row Level Security in production"): the
/// tenant is set per transaction with set_config(..., true) and is gone after commit or rollback,
/// as PgBouncer in transaction mode requires.
/// </summary>
public class TransactionPoolingTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public TransactionPoolingTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    /// <summary>Configured like an application: AddAcmeTenancy's provider, UseAcmeTenancy(sp).</summary>
    private ShopContext CreatePooledContext(MutableTenantProvider tenants)
    {
        var services = new ServiceCollection().AddSingleton<ITenantProvider>(tenants).BuildServiceProvider();
        return new ShopContext(new DbContextOptionsBuilder<ShopContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .UseAcmeTenancy(services, t => t.UseTransactionPooling())
            .Options);
    }

    private async Task<TenantId> CreateTenantWithOrderAsync()
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var db = _fixture.CreateAppContext(tenants);
        db.Orders.Add(new Order { Total = 1 });
        await db.SaveChangesAsync();
        return tenants.CurrentTenantId.Value;
    }

    private static async Task<string?> CurrentSettingAsync(DbContext db)
    {
        var command = (NpgsqlCommand)db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT current_setting('app.tenant_id', true)";
        command.Transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        return await command.ExecuteScalarAsync() as string;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tenant_is_set_in_the_transaction_and_gone_after_it(bool commit)
    {
        var tenant = await CreateTenantWithOrderAsync();
        await CreateTenantWithOrderAsync();

        await using var db = CreatePooledContext(new MutableTenantProvider { CurrentTenantId = tenant });
        var interceptors = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!;
        Assert.Contains(interceptors, i => i is PostgreSqlTransactionSessionInterceptor);
        Assert.DoesNotContain(interceptors, i => i is TenantConnectionInterceptor);

        await db.Database.OpenConnectionAsync();                  // the same connection throughout
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal(tenant.ToString(), await CurrentSettingAsync(db));
            var orders = await db.Orders.ToListAsync();
            Assert.Single(orders);
            Assert.Equal(tenant, orders[0].TenantId);

            if (commit)
                await transaction.CommitAsync();
            else
                await transaction.RollbackAsync();
        }

        // is_local = true: the value ends with the transaction
        Assert.True(string.IsNullOrEmpty(await CurrentSettingAsync(db)));
        Assert.Empty(await db.Orders.ToListAsync());               // outside a transaction: fail closed
    }

    [Fact]
    public async Task SaveChanges_sets_the_tenant_in_its_own_transaction()
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using (var db = CreatePooledContext(tenants))
        {
            db.Orders.Add(new Order { Total = 1 });
            db.Orders.Add(new Order { Total = 2 });
            await db.SaveChangesAsync();
        }

        await using var check = _fixture.CreateAppContext(tenants);
        Assert.Equal(2, await check.Orders.CountAsync());
    }

    [Fact]
    public async Task Single_insert_gets_a_transaction_and_with_it_the_tenant()
    {
        // EF omits the automatic transaction for a single command; without it the insert would
        // run without a tenant and fail on the policy
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using (var db = CreatePooledContext(tenants))
        {
            db.Orders.Add(new Order { Total = 1 });
            await db.SaveChangesAsync();
            db.Orders.Add(new Order { Total = 2 });
            db.SaveChanges();
        }

        await using var check = _fixture.CreateAppContext(tenants);
        Assert.Equal(2, await check.Orders.CountAsync());
    }

    [Fact]
    public async Task Connection_interceptor_refuses_the_session_value_with_transaction_pooling()
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var db = _fixture.CreateAppContext(tenants, t => t.UseTransactionPooling());

        var ex = await Assert.ThrowsAsync<TenancyException>(() => db.Orders.ToListAsync());
        Assert.Contains("UseTransactionPooling()", ex.Message);
    }

    [Fact]
    public async Task ApplyTenantSession_explains_that_the_tenant_is_set_per_transaction()
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var db = CreatePooledContext(tenants);

        // No session interceptor with transaction pooling: there is no session to update
        var ex = await Assert.ThrowsAsync<TenancyException>(() => db.Database.ApplyTenantSessionAsync());
        Assert.Contains("per transaction", ex.Message);
        Assert.Contains("UseTransactionPooling()", ex.Message);
    }
}
