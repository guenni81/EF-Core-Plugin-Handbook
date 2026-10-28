using Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests;

/// <summary>
/// One debug info entry per setting (chapter "The options extension"): EF compares these entries
/// to name the option that differs when it warns about many internal service providers.
/// </summary>
public class SqlServerDebugInfoTests
{
    private static Dictionary<string, string> DebugInfo(SqlServerTenancySettings settings)
    {
        var debugInfo = new Dictionary<string, string>();
        new SqlServerTenancyOptionsExtension().WithSettings(settings).Info.PopulateDebugInfo(debugInfo);
        return debugInfo;
    }

    [Fact]
    public void Every_setting_has_an_entry_of_its_own()
    {
        Assert.Equal(
            [
                "Acme:Tenancy:SqlServer:EnrichOpenTelemetry",
                "Acme:Tenancy:SqlServer:PolicySchema",
                "Acme:Tenancy:SqlServer:ProtectBulkOperations",
                "Acme:Tenancy:SqlServer:SessionKey",
                "Acme:Tenancy:SqlServer:VerifyMaterialized",
            ],
            DebugInfo(SqlServerTenancySettings.Default).Keys.Order());
    }

    [Fact]
    public void A_changed_setting_changes_exactly_its_own_entry()
    {
        var before = DebugInfo(SqlServerTenancySettings.Default);
        var after = DebugInfo(SqlServerTenancySettings.Default with { SessionKey = "Tenant" });

        var changed = before.Keys.Where(k => before[k] != after[k]);

        Assert.Equal(["Acme:Tenancy:SqlServer:SessionKey"], changed);
    }
}
