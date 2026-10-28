using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// Raw ADO.NET (and therefore Dapper) on the context's connection (chapter "Interceptors"): row
/// level security applies to every command, but the session value is only set when EF opens
/// the connection.
/// </summary>
public class RawAdoNetTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public RawAdoNetTests(PostgresFixture fixture)
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

    // What Dapper does: a plain command on the connection, optionally in a transaction
    private static async Task<long> CountOrdersAsync(DbConnection connection, DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM \"Orders\"";
        command.Transaction = transaction;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Connection_opened_by_ef_carries_the_tenant()
    {
        var tenant = await CreateTenantWithOrdersAsync(2);
        await CreateTenantWithOrdersAsync(1);
        await using var db = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant });

        await db.Database.OpenConnectionAsync();      // the interceptor sets the session

        Assert.Equal(2, await CountOrdersAsync(db.Database.GetDbConnection()));
    }

    [Fact]
    public async Task Connection_opened_by_ado_net_sees_nothing_until_the_session_is_applied()
    {
        var tenant = await CreateTenantWithOrdersAsync(2);
        await using var db = _fixture.CreateAppContext(
            new MutableTenantProvider { CurrentTenantId = tenant });
        await db.Orders.CountAsync();                 // EF opened and closed the connection again

        // Opened directly, as Dapper does with a closed connection: EF is not involved
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        Assert.Equal(0, await CountOrdersAsync(connection));      // fail closed

        await db.Database.ApplyTenantSessionAsync();
        Assert.Equal(2, await CountOrdersAsync(connection));
    }

    [Fact]
    public async Task With_transaction_pooling_raw_commands_belong_in_efs_transaction()
    {
        var tenant = await CreateTenantWithOrdersAsync(2);
        // Configured like an application with PgBouncer (see TransactionPoolingTests)
        var services = new ServiceCollection()
            .AddSingleton<ITenantProvider>(new MutableTenantProvider { CurrentTenantId = tenant })
            .BuildServiceProvider();
        await using var db = new ShopContext(new DbContextOptionsBuilder<ShopContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .UseAcmeTenancy(services, t => t.UseTransactionPooling())
            .Options);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var connection = db.Database.GetDbConnection();

        Assert.Equal(2, await CountOrdersAsync(connection, transaction.GetDbTransaction()));
    }
}
