using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// Composability with a widely used convention plugin: EFCore.NamingConventions renames
/// tables and columns to snake_case. Because the isolation convention reads the column name
/// only at ModelFinalizing, the policy uses the final name in either activation order
/// (chapter "Conventions").
/// </summary>
public class NamingConventionTests
{
    private static TestContext Create(bool namingFirst)
    {
        var builder = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .EnableServiceProviderCaching(false);

        if (namingFirst)
            builder.UseSnakeCaseNamingConvention().UseAcmeTenancy();
        else
            builder.UseAcmeTenancy().UseSnakeCaseNamingConvention();

        return new TestContext(builder.Options, null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Policy_uses_the_renamed_column(bool namingFirst)
    {
        using var ctx = Create(namingFirst);
        var order = ctx.DesignModel().FindEntityType(typeof(Order))!;

        var policy = RlsPolicy.Deserialize(order.GetRlsPolicies()["tenant_isolation"]);

        Assert.StartsWith("tenant_id = ", policy.Using);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Migration_targets_the_renamed_table(bool namingFirst)
    {
        using var ctx = Create(namingFirst);

        var sql = Contexts.MigrationSql(null, ctx);

        Assert.Contains("ALTER TABLE orders ENABLE ROW LEVEL SECURITY;", sql);
        Assert.Contains(sql, s => s.StartsWith("CREATE POLICY tenant_isolation ON orders"));
        Assert.Contains(sql, s => s.StartsWith("CREATE INDEX ix_orders_tenant_id"));
    }
}
