using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Scaffolding;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>Edge cases from part VIII of the handbook, against a real PostgreSQL.</summary>
public class AdvancedTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public AdvancedTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private ShopContext ContextFor(TenantId? tenant)
        => _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = tenant });

    [Fact]
    public async Task ExecuteUpdate_bypasses_interceptor_but_not_rls()
    {
        var tenant = TenantId.New();
        await using (var db = ContextFor(tenant))
        {
            db.Orders.Add(new Order { Total = 1 });
            await db.SaveChangesAsync();
        }

        await using var attacker = ContextFor(tenant);

        // No SaveChanges -> the SaveChanges interceptor does NOT run.
        // The database (the policy's WITH CHECK) still prevents moving the row to another tenant.
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            attacker.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.TenantId, TenantId.New())));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task Raw_sql_is_filtered_by_rls()
    {
        var tenant = TenantId.New();
        await using (var db = ContextFor(tenant))
        {
            db.Orders.AddRange(new Order { Total = 1 }, new Order { Total = 2 });
            await db.SaveChangesAsync();
        }

        await using var reader = ContextFor(tenant);
        // No WHERE clause: RLS also applies to raw SQL, which EF does not inspect at all.
        var count = await reader.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM \"Orders\"")
            .SingleAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task TenantId_key_is_generated_and_persisted()
    {
        // Tenant is not tenant-owned, so no active tenant is needed.
        await using var db = ContextFor(null);
        var tenant = new Tenant { Name = "Contoso Ltd" };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        // A fresh context, so FindAsync has to query the database instead of the change tracker.
        await using var check = ContextFor(null);
        Assert.False(tenant.Id.IsEmpty);
        Assert.NotNull(await check.Tenants.FindAsync(tenant.Id));
    }

    [Fact]
    public async Task TenantId_roundtrips_through_json_column()
    {
        var tenant = TenantId.New();
        var approver = TenantId.New();
        await using (var db = ContextFor(tenant))
        {
            db.Shipments.Add(new Shipment { Details = new() { Carrier = "DHL", ApprovedBy = approver } });
            await db.SaveChangesAsync();
        }

        await using var reader = ContextFor(tenant);
        var shipment = await reader.Shipments.SingleAsync();

        Assert.Equal(approver, shipment.Details.ApprovedBy);
    }

    [Fact]
    public async Task Idempotent_script_runs_twice()
    {
        var options = new DbContextOptionsBuilder<ScriptContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .UseAcmeTenancy()
            .Options;
        await using var db = new ScriptContext(options);

        var script = db.GetService<IMigrator>()
            .GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        // 1st run: checks that the nested DO $$ inside DO $EF$ is valid.
        // 2nd run: skipped via the migrations history table. Independently of that, the
        //    operations themselves are idempotent (IF NOT EXISTS) in case the history is missing.
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
    }

    [Fact]
    public async Task Custom_sql_node_and_aggregate_run_against_postgres()
    {
        var tenant = TenantId.New();
        await using (var db = ContextFor(tenant))
        {
            db.Orders.AddRange(new Order { Total = 1 }, new Order { Total = 2 });
            await db.SaveChangesAsync();
        }

        await using var reader = ContextFor(tenant);

        var same = await reader.Orders
            .CountAsync(o => EF.Functions.IsNotDistinctFrom(o.TenantId, EF.Functions.CurrentTenant()));
        var allPositive = await reader.Orders
            .GroupBy(o => o.TenantId)
            .Select(g => EF.Functions.Every(g.Select(o => o.Total > 0)))
            .SingleAsync();

        Assert.Equal(2, same);
        Assert.True(allPositive);
    }

    [Fact]
    public void Scaffolding_reads_rls_and_policies()
    {
        // The context only supplies the logger; the factory opens its own connection.
        using var ctx = ContextFor(null);
        var factory = new TenancyDatabaseModelFactory(
            ctx.GetService<IDiagnosticsLogger<DbLoggerCategory.Scaffolding>>());

        var model = factory.Create(_fixture.OwnerConnectionString, new DatabaseModelFactoryOptions());
        var orders = model.Tables.Single(t => t.Name == "Orders");

        Assert.Equal(true, orders[RlsAnnotationNames.Enabled]);
        Assert.Equal(true, orders[RlsAnnotationNames.Forced]);
        var policy = RlsPolicy.Deserialize((string)orders[RlsAnnotationNames.PolicyPrefix + "tenant_isolation"]!);
        Assert.Contains("current_setting", policy.Using);
    }
}
