using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>The value generation convention for keys of type <see cref="TenantId"/>.</summary>
public class ValueGenerationTests
{
    [Fact]
    public void TenantId_key_is_generated_on_add()
    {
        using var ctx = Contexts.Create();
        var tenant = new Tenant { Name = "Contoso Ltd" };

        // The generator produces final (non-temporary) values, so the key is set by Add().
        ctx.Tenants.Add(tenant);

        Assert.False(tenant.Id.IsEmpty);
    }

    [Fact]
    public void Explicit_ValueGeneratedNever_wins_over_convention()
    {
        using var ctx = Contexts.Create(
            model: m => m.Entity<Tenant>().Property(t => t.Id).ValueGeneratedNever());
        var tenant = new Tenant { Id = TenantId.New() };
        var expected = tenant.Id;

        ctx.Tenants.Add(tenant);

        Assert.Equal(expected, tenant.Id);
    }
}
