using Acme.EntityFrameworkCore.Tenancy.Interceptors;

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
}
