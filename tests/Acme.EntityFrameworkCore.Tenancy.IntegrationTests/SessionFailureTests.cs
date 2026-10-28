using System.Data;
using System.Data.Common;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Runtime;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>How the next session command of <see cref="FaultySessionInterceptor"/> fails.</summary>
public enum SessionFault
{
    None,
    Fail,       // the server rejects the command
    Timeout,    // the command outlives its CommandTimeout
    Cancel,     // the caller cancels while the command runs
}

/// <summary>
/// The plugin's PostgreSQL interceptor with a controlled fault: the next session command fails,
/// times out or is cancelled, without random waits. Every fault applies once.
/// </summary>
public sealed class FaultySessionInterceptor : TenantConnectionInterceptor
{
    public FaultySessionInterceptor(ITenantProvider tenants)
        : base(tenants)
    {
    }

    public SessionFault Next { get; set; }

    /// <summary>Cancelled by a <see cref="SessionFault.Cancel"/> fault.</summary>
    public CancellationTokenSource Cancellation { get; } = new();

    protected override DbCommand CreateSessionCommand(DbConnection connection, DbContext? context, TenantId? tenant)
    {
        var command = base.CreateSessionCommand(connection, context, tenant);
        var fault = Next;
        Next = SessionFault.None;
        switch (fault)
        {
            case SessionFault.Fail:
                command.CommandText = "SELECT 1 / 0, " + command.CommandText["SELECT ".Length..];
                break;
            case SessionFault.Timeout:
                command.CommandText = "SELECT pg_sleep(30), " + command.CommandText["SELECT ".Length..];
                command.CommandTimeout = 1;
                break;
            case SessionFault.Cancel:
                Cancellation.Cancel();   // the token EF passed on is this one (see the test)
                break;
        }

        return command;
    }
}

/// <summary>
/// Failures of the session command that sets the tenant (chapter "Interceptors"): afterwards
/// the context must neither run without a tenant nor with the tenant of an earlier use.
/// </summary>
public class SessionFailureTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public SessionFailureTests(PostgresFixture fixture)
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

    /// <summary>
    /// A pool of its own with one physical connection, so that every context gets the same
    /// server session; optionally without the reset when a connection is returned.
    /// </summary>
    private string SinglePool(bool noResetOnClose) => new NpgsqlConnectionStringBuilder(_fixture.AppConnectionString)
    {
        ApplicationName = "session_" + Guid.NewGuid().ToString("N")[..8],
        MaxPoolSize = 1,
        NoResetOnClose = noResetOnClose,
    }.ConnectionString;

    private static ShopContext Create(string connectionString, ITenantProvider tenants, FaultySessionInterceptor interceptor)
        => new(new DbContextOptionsBuilder<ShopContext>()
            .UseNpgsql(connectionString)
            .UseAcmeTenancy(t => t.ApplyQueryFilter(false))            // only RLS protects
            .EnableServiceProviderCaching(false)
            .AddInterceptors(interceptor, new TenantSaveChangesInterceptor(tenants))
            .Options);

    [Theory]
    [InlineData(SessionFault.Fail, false)]
    [InlineData(SessionFault.Fail, true)]
    [InlineData(SessionFault.Timeout, false)]
    [InlineData(SessionFault.Timeout, true)]
    [InlineData(SessionFault.Cancel, false)]
    [InlineData(SessionFault.Cancel, true)]
    public async Task After_a_failed_session_command_the_next_operation_sets_the_tenant_again(
        SessionFault fault, bool noResetOnClose)
    {
        var a = await CreateTenantWithOrdersAsync(2);
        var b = await CreateTenantWithOrdersAsync(1);
        var pool = SinglePool(noResetOnClose);

        // An earlier use of the physical connection by tenant A
        var tenantsA = new MutableTenantProvider { CurrentTenantId = a };
        await using (var earlier = Create(pool, tenantsA, new FaultySessionInterceptor(tenantsA)))
            Assert.Equal(2, await earlier.Orders.CountAsync());

        // Tenant B: the session command fails once
        var tenantsB = new MutableTenantProvider { CurrentTenantId = b };
        var interceptor = new FaultySessionInterceptor(tenantsB) { Next = fault };
        await using var db = Create(pool, tenantsB, interceptor);
        var ex = await Record.ExceptionAsync(() => db.Orders.ToListAsync(interceptor.Cancellation.Token));
        Assert.True(fault switch
        {
            SessionFault.Fail => ex is PostgresException { SqlState: PostgresErrorCodes.DivisionByZero },
            // EF reports the timeout as a transient failure around Npgsql's exception
            SessionFault.Timeout => ex is InvalidOperationException
                { InnerException: NpgsqlException { InnerException: TimeoutException } },
            _ => ex is OperationCanceledException,
        }, ex?.ToString());

        // The failure leaves no open connection behind that EF would use without the interceptor
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);

        // Next use of the same context: the interceptor runs again and sets tenant B
        var orders = await db.Orders.ToListAsync();
        Assert.Single(orders);
        Assert.Equal(b, orders[0].TenantId);
    }

    [Fact]
    public async Task A_failed_session_command_in_an_explicit_open_stays_with_the_caller()
    {
        var b = await CreateTenantWithOrdersAsync(1);
        var tenants = new MutableTenantProvider { CurrentTenantId = b };
        var interceptor = new FaultySessionInterceptor(tenants) { Next = SessionFault.Fail };
        await using var db = Create(_fixture.AppConnectionString, tenants, interceptor);

        // OpenConnection runs the interceptor as well; the failure closes the connection again
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.OpenConnectionAsync());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);

        await db.Database.OpenConnectionAsync();
        Assert.Single(await db.Orders.ToListAsync());
        Assert.Single(await db.Orders.ToListAsync());                  // stays open, same tenant
        await db.Database.CloseConnectionAsync();
    }

    [Fact]
    public async Task A_failed_ApplyTenantSession_keeps_the_previous_tenant_of_the_open_connection()
    {
        var a = await CreateTenantWithOrdersAsync(2);
        var b = await CreateTenantWithOrdersAsync(1);
        var tenants = new MutableTenantProvider { CurrentTenantId = a };
        var interceptor = new FaultySessionInterceptor(tenants);
        await using var db = Create(_fixture.AppConnectionString, tenants, interceptor);
        await db.Database.OpenConnectionAsync();

        // The caller owns the open connection: the helper does not close it, the session keeps
        // tenant A. The exception must therefore not be swallowed.
        tenants.CurrentTenantId = b;
        interceptor.Next = SessionFault.Fail;
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ApplyTenantSessionAsync());
        Assert.Equal(ConnectionState.Open, db.Database.GetDbConnection().State);
        Assert.All(await db.Orders.ToListAsync(), o => Assert.Equal(a, o.TenantId));

        Assert.True(await db.Database.ApplyTenantSessionAsync());       // retried: tenant B
        Assert.Equal(b, (await db.Orders.SingleAsync()).TenantId);
    }

    [Fact]
    public void Synchronous_open_closes_the_connection_after_a_failed_session_command()
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        var interceptor = new FaultySessionInterceptor(tenants) { Next = SessionFault.Fail };
        using var db = Create(_fixture.AppConnectionString, tenants, interceptor);

        Assert.Throws<PostgresException>(() => db.Orders.ToList());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
        Assert.Empty(db.Orders.ToList());                              // a fresh tenant: no rows
    }
}

/// <summary>The transaction pooling interceptor with a controlled fault of its next command.</summary>
public sealed class FaultyTransactionSessionInterceptor : PostgreSqlTransactionSessionInterceptor
{
    public FaultyTransactionSessionInterceptor(ITenantProvider tenants)
        : base(tenants)
    {
    }

    public SessionFault Next { get; set; }

    public CancellationTokenSource Cancellation { get; } = new();

    protected override DbCommand CreateSessionCommand(DbConnection connection, DbTransaction transaction, DbContext? context)
    {
        var command = base.CreateSessionCommand(connection, transaction, context);
        var fault = Next;
        Next = SessionFault.None;
        if (fault == SessionFault.Fail)
            command.CommandText = "SELECT 1 / 0, " + command.CommandText["SELECT ".Length..];
        else if (fault == SessionFault.Cancel)
            Cancellation.Cancel();
        return command;
    }
}

/// <summary>Failures of the per-transaction command with UseTransactionPooling().</summary>
public class TransactionSessionFailureTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public TransactionSessionFailureTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    [Theory]
    [InlineData(SessionFault.Fail)]
    [InlineData(SessionFault.Cancel)]
    public async Task A_failed_tenant_command_leaves_no_transaction_and_the_next_one_works(SessionFault fault)
    {
        var tenant = TenantId.New();
        var tenants = new MutableTenantProvider { CurrentTenantId = tenant };
        var interceptor = new FaultyTransactionSessionInterceptor(tenants) { Next = fault };
        await using var db = new ShopContext(new DbContextOptionsBuilder<ShopContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .UseAcmeTenancy(t => t.UseTransactionPooling().ApplyQueryFilter(false))
            .EnableServiceProviderCaching(false)
            .AddInterceptors(interceptor, new TenantSaveChangesInterceptor(tenants))
            .Options);

        var ex = await Record.ExceptionAsync(() => db.Database.BeginTransactionAsync(interceptor.Cancellation.Token));
        Assert.True(fault == SessionFault.Fail ? ex is PostgresException : ex is OperationCanceledException);
        Assert.Null(db.Database.CurrentTransaction);

        // The next transaction gets the tenant: SaveChanges begins one and writes the row
        db.Orders.Add(new Order { Total = 5 });
        await db.SaveChangesAsync();
        await using (var transaction = await db.Database.BeginTransactionAsync())
            Assert.Single(await db.Orders.ToListAsync());

        await using var check = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant });
        Assert.Single(await check.Orders.ToListAsync());
    }
}
