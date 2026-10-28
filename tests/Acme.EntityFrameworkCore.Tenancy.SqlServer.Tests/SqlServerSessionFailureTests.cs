using System.Data;
using System.Data.Common;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Runtime;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests;

/// <summary>The SQL Server interceptor whose next session command fails once.</summary>
public sealed class FailingSessionInterceptor : SqlServerTenantSessionInterceptor
{
    public FailingSessionInterceptor(ITenantProvider tenants)
        : base(tenants)
    {
    }

    public bool FailNext { get; set; }

    protected override DbCommand CreateSessionCommand(DbConnection connection, DbContext? context, TenantId? tenant)
    {
        var command = base.CreateSessionCommand(connection, context, tenant);
        if (FailNext)
        {
            FailNext = false;
            command.CommandText = "SELECT 1 / 0; " + command.CommandText;
        }

        return command;
    }
}

/// <summary>A failed sp_set_session_context closes the connection again (shared base class).</summary>
public class SqlServerSessionFailureTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public SqlServerSessionFailureTests(SqlServerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    [Fact]
    public async Task After_a_failed_session_command_the_next_operation_sets_the_tenant_again()
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        var interceptor = new FailingSessionInterceptor(tenants);
        await using var db = new ShopContext(new DbContextOptionsBuilder<ShopContext>()
            .UseSqlServer(_fixture.AppConnectionString, sql => sql.UseAcmeTenancy())
            .EnableServiceProviderCaching(false)
            .AddInterceptors(interceptor, new TenantSaveChangesInterceptor(tenants))
            .Options);
        db.Orders.Add(new Order { Total = 3 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        interceptor.FailNext = true;
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Orders.ToListAsync());
        Assert.Equal(8134, ex.Number);                                // divide by zero
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);

        var orders = await db.Orders.ToListAsync();
        Assert.Single(orders);
        Assert.Equal(tenants.CurrentTenantId, orders[0].TenantId);
    }
}
