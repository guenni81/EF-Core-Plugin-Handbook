using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// Property-level Fluent API and runtime annotation (chapter "Property-level Fluent API and
/// runtime annotations").
/// </summary>
public class TenantPropertyTests
{
    /// <summary>Implements the interface explicitly: not a mapped property.</summary>
    private sealed class ExplicitContract : ITenantOwned
    {
        public int Id { get; set; }
        public TenantId Mandant { get; set; }

        TenantId ITenantOwned.TenantId
        {
            get => Mandant;
            set => Mandant = value;
        }
    }

    private sealed class FixedTenant : ITenantProvider
    {
        public FixedTenant(TenantId? id) => CurrentTenantId = id;

        public TenantId? CurrentTenantId { get; }
    }

    private static TestContext CreateWithContract()
        => Contexts.Create(model: m => m.Entity<Contract>().Property(c => c.Mandant).IsTenantProperty());

    [Fact]
    public void Marked_property_enables_isolation_without_interface()
    {
        using var ctx = CreateWithContract();
        var contract = ctx.DesignModel().FindEntityType(typeof(Contract))!;

        Assert.True(contract.IsRlsEnabled());
        Assert.Contains("\"Mandant\" = ", RlsPolicy.Deserialize(contract.GetRlsPolicies()["tenant_isolation"]).Using);
    }

    [Fact]
    public void Runtime_model_carries_tenant_property_name()
    {
        using var ctx = CreateWithContract();

        // Runtime model (context.Model), not the design-time model: the interceptor only
        // sees the runtime model, so the annotation must be present there.
        Assert.Equal("Mandant", ctx.Model.FindEntityType(typeof(Contract))!.GetTenantPropertyName());
    }

    [Fact]
    public void Interceptor_uses_marked_property()
    {
        var tenant = TenantId.New();
        using var ctx = CreateWithContract();
        var contract = new Contract();
        ctx.Add(contract);

        new TenantSaveChangesInterceptor(new FixedTenant(tenant)).ApplyTenant(ctx);

        Assert.Equal(tenant, contract.Mandant);
    }

    [Fact]
    public void Opted_out_entities_are_not_touched_by_interceptor()
    {
        using var ctx = Contexts.Create();
        var audit = new AuditEntry();
        ctx.AuditEntries.Add(audit);

        // No tenant required, the value stays empty: opting out means no isolation
        new TenantSaveChangesInterceptor(new FixedTenant(null)).ApplyTenant(ctx);

        Assert.True(audit.TenantId.IsEmpty);
    }

    [Fact]
    public void Ignored_tenant_property_of_an_ITenantOwned_type_is_rejected()
    {
        using var ctx = Contexts.Create(model: m => m.Entity<Order>().Ignore(o => o.TenantId));

        Assert.Contains("is not mapped", Assert.Throws<InvalidOperationException>(() => ctx.Model).Message);
    }

    [Fact]
    public void Explicit_interface_implementation_needs_a_marked_property()
    {
        using (var unmarked = Contexts.Create(model: m => m.Entity<ExplicitContract>()))
            Assert.Contains("IsTenantProperty()",
                Assert.Throws<InvalidOperationException>(() => unmarked.Model).Message);

        using var marked = Contexts.Create(
            model: m => m.Entity<ExplicitContract>().Property(c => c.Mandant).IsTenantProperty());
        Assert.Equal("Mandant", marked.Model.FindEntityType(typeof(ExplicitContract))!.GetTenantPropertyName());
    }
}
