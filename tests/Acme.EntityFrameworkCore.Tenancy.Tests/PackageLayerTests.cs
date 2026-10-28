using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// The core package works with any EF Core provider, the relational layer is a package of its
/// own (chapter "What belongs in the core"). A non-relational provider such as Cosmos DB must be
/// able to reference the core without pulling in Microsoft.EntityFrameworkCore.Relational.
/// </summary>
public class PackageLayerTests
{
    [Fact]
    public void Core_does_not_reference_the_relational_assembly()
    {
        var references = typeof(TenantId).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Relational", references);
    }

    [Fact]
    public void Relational_building_blocks_live_in_the_relational_package()
    {
        Assert.Equal("Acme.EntityFrameworkCore.Tenancy.Relational",
            typeof(TenantSessionInterceptor).Assembly.GetName().Name);
    }

    [Fact]
    public void Tenancy_plugin_alone_keeps_the_sql_generator_of_npgsql()
    {
        // The custom SQL node lives in the functions sample plugin: the tenancy plugin replaces
        // no exclusive query service and composes with any plugin that does
        var options = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseAcmeTenancy()
            .EnableServiceProviderCaching(false)
            .Options;
        using var ctx = new TestContext(options, null);

        Assert.IsType<NpgsqlQuerySqlGeneratorFactory>(ctx.GetService<IQuerySqlGeneratorFactory>());
    }

    [Fact]
    public void EnrichOpenTelemetry_registers_the_interceptor_only_when_enabled()
    {
        static int Count(bool enrich)
        {
            using var ctx = Contexts.Create(t => t.EnrichOpenTelemetry(enrich));
            return ctx.GetService<IEnumerable<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>>()
                .OfType<TenantActivityEnrichmentInterceptor>().Count();
        }

        Assert.Equal((0, 1), (Count(false), Count(true)));
    }
}
