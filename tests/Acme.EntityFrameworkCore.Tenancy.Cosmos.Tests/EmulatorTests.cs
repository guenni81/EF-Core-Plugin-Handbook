using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Tests;

/// <summary>
/// Against the Cosmos DB emulator. Cosmos only supports asynchronous I/O, so every test is async.
/// </summary>
public class EmulatorTests : IClassFixture<CosmosFixture>
{
    private readonly CosmosFixture _fixture;

    public EmulatorTests(CosmosFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private async Task<TenantId> CreateTenantWithOrdersAsync(int count)
    {
        var tenant = TenantId.New();
        await using var ctx = _fixture.CreateContext(new MutableTenantProvider { CurrentTenantId = tenant });
        for (var i = 0; i < count; i++)
            ctx.Orders.Add(new Order { Id = Guid.NewGuid(), Total = i + 1 });
        await ctx.SaveChangesAsync();
        return tenant;
    }

    [Fact]
    public async Task SaveChanges_sets_the_tenant_and_the_filter_isolates_tenants()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        var b = await CreateTenantWithOrdersAsync(1);

        await using var readA = _fixture.CreateContext(new MutableTenantProvider { CurrentTenantId = a });
        var orders = await readA.Orders.ToListAsync();

        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.Equal(a, o.TenantId));

        await using var readB = _fixture.CreateContext(new MutableTenantProvider { CurrentTenantId = b });
        Assert.Equal(1, await readB.Orders.CountAsync());
    }

    [Fact]
    public async Task One_context_follows_the_tenant_from_query_to_query()
    {
        // The tenant is a query parameter read from the context for every execution, not a
        // value frozen into the cached query
        var a = await CreateTenantWithOrdersAsync(2);
        var b = await CreateTenantWithOrdersAsync(3);
        var tenants = new MutableTenantProvider { CurrentTenantId = a };
        await using var ctx = _fixture.CreateContext(tenants);

        var countA = await ctx.Orders.CountAsync();
        tenants.CurrentTenantId = b;
        var countB = await ctx.Orders.CountAsync();

        Assert.Equal((2, 3), (countA, countB));
    }

    [Fact]
    public async Task Without_a_tenant_the_filter_matches_nothing()
    {
        await CreateTenantWithOrdersAsync(1);
        await using var ctx = _fixture.CreateContext(new MutableTenantProvider());

        Assert.Empty(await ctx.Orders.ToListAsync());
    }

    [Fact]
    public async Task Queries_with_the_filter_go_to_a_single_partition()
    {
        var tenant = await CreateTenantWithOrdersAsync(2);
        var partitions = new List<string?>();
        await using var ctx = _fixture.CreateContext(
            new MutableTenantProvider { CurrentTenantId = tenant },
            configure: o => o.LogTo(
                (id, _) => id == CosmosEventId.ExecutingSqlQuery,
                e => partitions.Add(((CosmosQueryEventData)e).PartitionKeyValue.ToString())));

        await ctx.Orders.Where(o => o.Total > 0).ToListAsync();

        // EF lifts the filter's equality on the partition key into the request
        var partition = Assert.Single(partitions);
        Assert.Contains(tenant.Value.ToString("D"), partition);
    }

    [Fact]
    public async Task IgnoreQueryFilters_crosses_tenants_and_the_materialization_check_stops_it()
    {
        var a = await CreateTenantWithOrdersAsync(1);
        await CreateTenantWithOrdersAsync(1);

        await using var unchecked_ = _fixture.CreateContext(new MutableTenantProvider { CurrentTenantId = a });
        Assert.True(await unchecked_.Orders.IgnoreQueryFilters().CountAsync() >= 2);

        await using var verified = _fixture.CreateContext(
            new MutableTenantProvider { CurrentTenantId = a }, verifyMaterializedTenants: true);
        await Assert.ThrowsAsync<TenancyException>(() => verified.Orders.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task FromSql_keeps_the_filter_but_runs_across_partitions()
    {
        var tenant = await CreateTenantWithOrdersAsync(2);
        await CreateTenantWithOrdersAsync(1);
        var partitions = new List<string?>();
        await using var ctx = _fixture.CreateContext(
            new MutableTenantProvider { CurrentTenantId = tenant },
            configure: o => o.LogTo(
                (id, _) => id == CosmosEventId.ExecutingSqlQuery,
                e => partitions.Add(((CosmosQueryEventData)e).PartitionKeyValue.ToString())));

        // EF composes the filter around the raw query, so the result is still correct ...
        var orders = await ctx.Orders.FromSql($"SELECT * FROM c").ToListAsync();
        Assert.Equal(2, orders.Count);

        // ... but the partition key is not lifted: the query fans out to every partition. A query
        // through the Cosmos SDK itself would not even see the filter.
        Assert.DoesNotContain(tenant.Value.ToString("D"), Assert.Single(partitions));
    }
}
