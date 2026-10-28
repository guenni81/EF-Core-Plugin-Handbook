using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>Counts the commands that reach the database.</summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    public int Count { get; private set; }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Count++;
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Count++;
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// ProtectBulkOperations() with ExecuteUpdate after a projection, against PostgreSQL: allowed
/// setters update the rows of the own tenant, setters of the tenant column fail before any
/// command is sent (chapter "Bulk operations and the limits of interceptors").
/// </summary>
public class BulkUpdateProjectionTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public BulkUpdateProjectionTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private async Task<(TenantId Tenant, int Id)> CreateOrderAsync(decimal total)
    {
        var tenant = TenantId.New();
        await using var db = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant });
        var order = new Order { Total = total };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (tenant, order.Id);
    }

    private ShopContext Protected(TenantId tenant, CommandCounter counter, bool protect = true)
        => _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant },
            t => t.ProtectBulkOperations(protect), o => o.AddInterceptors(counter));

    private async Task<(TenantId Tenant, decimal Total)> StoredAsync(int id)
    {
        await using var connection = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT \"TenantId\", \"Total\" FROM \"Orders\" WHERE \"Id\" = {id}", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (new TenantId(reader.GetGuid(0)), reader.GetDecimal(1));
    }

    [Fact]
    public async Task Projected_setter_of_another_column_updates_only_the_own_tenant()
    {
        var (tenant, id) = await CreateOrderAsync(10);
        var (other, otherId) = await CreateOrderAsync(10);
        var counter = new CommandCounter();
        await using var db = Protected(tenant, counter);

        var updated = await db.Orders.Select(o => new { Entity = o })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Entity.Total, 20m));

        Assert.Equal(1, updated);                                     // RLS: only the own row
        Assert.Equal((tenant, 20m), await StoredAsync(id));
        Assert.Equal((other, 10m), await StoredAsync(otherId));
    }

    [Fact]
    public async Task Projected_setters_of_the_tenant_column_fail_before_any_command()
    {
        var (tenant, id) = await CreateOrderAsync(10);
        var counter = new CommandCounter();
        await using var db = Protected(tenant, counter);

        await Assert.ThrowsAsync<TenancyException>(() => db.Orders.Select(o => new { Entity = o })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Entity.TenantId, TenantId.New())));
        Assert.Throws<TenancyException>(() => db.Orders.Select(o => new { Entity = o })
            .ExecuteUpdate(s => s
                .SetProperty(x => x.Entity.Total, 30m)
                .SetProperty(x => x.Entity.TenantId, TenantId.New())));
        await Assert.ThrowsAsync<TenancyException>(() => db.Orders.Select(o => new { Entity = o, o.TenantId })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TenantId, TenantId.New())));

        Assert.Equal(0, counter.Count);
        Assert.Equal((tenant, 10m), await StoredAsync(id));
    }

    [Fact]
    public async Task Without_the_option_the_database_rejects_the_projected_tenant_setters()
    {
        var (tenant, id) = await CreateOrderAsync(10);
        var counter = new CommandCounter();
        await using var db = Protected(tenant, counter, protect: false);

        // EF translates both forms; WITH CHECK of the policy stops them in the database
        var entity = await Assert.ThrowsAsync<PostgresException>(() => db.Orders.Select(o => new { Entity = o })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Entity.TenantId, TenantId.New())));
        var scalar = await Assert.ThrowsAsync<PostgresException>(() => db.Orders.Select(o => new { Entity = o, o.TenantId })
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TenantId, TenantId.New())));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, entity.SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, scalar.SqlState);
        Assert.Equal(2, counter.Count);
        Assert.Equal((tenant, 10m), await StoredAsync(id));
    }
}
