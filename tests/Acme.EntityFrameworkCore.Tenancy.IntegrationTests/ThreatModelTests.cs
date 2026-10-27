using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// The limit of the threat model (chapter "Row Level Security in production"): whoever runs
/// arbitrary SQL as the application role can set the session setting the policy trusts.
/// </summary>
public class ThreatModelTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public ThreatModelTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    [Fact]
    public async Task Arbitrary_sql_as_the_app_role_can_switch_to_another_tenant()
    {
        var victim = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
        await using (var db = _fixture.CreateAppContext(victim))
        {
            db.Orders.Add(new Order { Total = 1 });
            await db.SaveChangesAsync();
        }

        await using var attacker = _fixture.CreateAppContext(new MutableTenantProvider { CurrentTenantId = TenantId.New() });
        await attacker.Database.OpenConnectionAsync();
        Assert.Empty(await attacker.Orders.ToListAsync());

        // What an injected statement can do: the app role may call set_config like the plugin
        var victimId = victim.CurrentTenantId.Value.ToString();
        await attacker.Database.SqlQuery<string>(
                $"SELECT set_config('app.tenant_id', {victimId}, false) AS \"Value\"")
            .ToListAsync();

        var stolen = await attacker.Orders.ToListAsync();
        Assert.NotEmpty(stolen);
        Assert.All(stolen, o => Assert.Equal(victim.CurrentTenantId, o.TenantId));
    }
}
