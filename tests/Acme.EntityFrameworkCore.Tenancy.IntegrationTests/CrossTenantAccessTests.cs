using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// Legitimate access across tenants (reporting, maintenance) via a separate database role,
/// not via a switch the application role could flip itself (chapter "Row Level Security in
/// production").
/// </summary>
public class CrossTenantAccessTests : IClassFixture<PostgresFixture>
{
    private const string ReportingRole = "shop_reporting";
    private const string ReportingPassword = "shop_reporting_pw";

    private readonly PostgresFixture _fixture;

    public CrossTenantAccessTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private async Task CreateTenantWithOrdersAsync(int count)
    {
        var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using var db = _fixture.CreateAppContext(tenants);

        for (var i = 0; i < count; i++)
            db.Orders.Add(new Order { Total = i + 1 });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Read-only role that bypasses RLS. Created by an administrator (BYPASSRLS requires a
    /// superuser), deliberately not by a migration of the application.
    /// </summary>
    private async Task<string> CreateReportingRoleAsync()
    {
        await using var owner = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await owner.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            DO $$ BEGIN
                IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{ReportingRole}') THEN
                    CREATE ROLE {ReportingRole} LOGIN PASSWORD '{ReportingPassword}'
                        NOSUPERUSER BYPASSRLS;
                END IF;
            END $$;
            GRANT USAGE ON SCHEMA public TO {ReportingRole};
            GRANT SELECT ON ALL TABLES IN SCHEMA public TO {ReportingRole};
            """, owner);
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(_fixture.OwnerConnectionString)
        {
            Username = ReportingRole,
            Password = ReportingPassword,
        }.ConnectionString;
    }

    [Fact]
    public async Task Reporting_role_reads_all_tenants()
    {
        await CreateTenantWithOrdersAsync(2);
        await CreateTenantWithOrdersAsync(3);
        var connectionString = await CreateReportingRoleAsync();

        // Separate context configuration: no tenant, no interceptors, read-only by grant
        var options = new DbContextOptionsBuilder<ShopContext>()
            .UseNpgsql(connectionString)
            .UseAcmeTenancy()
            .Options;
        await using var reporting = new ShopContext(options);

        var tenants = await reporting.Orders.Select(o => o.TenantId).Distinct().CountAsync();

        Assert.True(tenants >= 2);
    }

    [Fact]
    public async Task Reporting_role_cannot_write()
    {
        var connectionString = await CreateReportingRoleAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO \"Orders\" (\"TenantId\", \"Total\") VALUES (gen_random_uuid(), 1)",
            connection);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task App_role_cannot_switch_off_rls_itself()
    {
        await CreateTenantWithOrdersAsync(1);
        await using var connection = new NpgsqlConnection(_fixture.AppConnectionString);
        await connection.OpenAsync();

        // Neither turning off row security for the session nor a self-made "bypass" setting
        // helps: the app role has no BYPASSRLS, so the policies still apply.
        await using var command = new NpgsqlCommand(
            "SET row_security = off; SELECT count(*) FROM \"Orders\";", connection);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }
}
