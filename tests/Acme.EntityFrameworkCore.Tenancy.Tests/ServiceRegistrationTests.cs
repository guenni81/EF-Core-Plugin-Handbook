using Acme.EntityFrameworkCore.Functions.PostgreSQL.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// Upgrade guard: the plugins choose the lifetime themselves wherever they call Replace. If a new EF
/// version changes the lifetime of such a service, this test fails instead of the application
/// (chapter "Versioning and compatibility", upgrade playbook).
/// </summary>
public class ServiceRegistrationTests
{
    /// <summary>EF's own service table: lifetime and cardinality of every EF service.</summary>
    internal static IReadOnlyDictionary<Type, ServiceCharacteristics> EfServiceTable()
    {
        var table = new Dictionary<Type, ServiceCharacteristics>();
        foreach (var (type, characteristics) in EntityFrameworkServicesBuilder.CoreServices
                     .Concat(EntityFrameworkRelationalServicesBuilder.RelationalServices))
            table[type] = characteristics;
        return table;
    }

    [Fact]
    public void Every_registered_ef_service_uses_the_lifetime_and_cardinality_of_efs_table()
    {
        var services = new ServiceCollection();
        new TenancyOptionsExtension()
            .WithSettings(TenancySettings.Default with { VerifyMaterializedTenants = true })
            .ApplyServices(services);
        new FunctionsOptionsExtension().ApplyServices(services);
        var table = EfServiceTable();

        var checkedServices = 0;
        foreach (var group in services.GroupBy(d => d.ServiceType))
        {
            if (!table.TryGetValue(group.Key, out var expected))
                continue;                          // a service of the plugin itself

            checkedServices++;
            Assert.All(group, d => Assert.True(d.Lifetime == expected.Lifetime,
                $"{group.Key.Name}: registered as {d.Lifetime}, EF expects {expected.Lifetime}"));
            if (!expected.MultipleRegistrations)
                Assert.True(group.Count() == 1, $"{group.Key.Name} is a single service");
        }

        // Plugin interfaces and replaced single services: guards against an empty comparison
        Assert.True(checkedServices >= 10, $"Only {checkedServices} EF services compared");
    }
}
