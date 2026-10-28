using Acme.EntityFrameworkCore.Tenancy.Cosmos.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Tests;

/// <summary>Implements the interface explicitly: not a mapped property.</summary>
public class Contract : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId Mandant { get; set; }

    TenantId ITenantOwned.TenantId
    {
        get => Mandant;
        set => Mandant = value;
    }
}

/// <summary>Context whose model a test configures.</summary>
public sealed class ConfiguredContext : DbContext
{
    private readonly Action<ModelBuilder> _model;

    public ConfiguredContext(Action<ModelBuilder> model)
        : base(new DbContextOptionsBuilder<ConfiguredContext>()
            .UseCosmos(Contexts.OfflineConnectionString, "shop", cosmos => cosmos.UseAcmeTenancy(
                new ServiceCollection().AddSingleton<ITenantProvider>(new MutableTenantProvider())
                    .BuildServiceProvider()))
            .EnableServiceProviderCaching(false)
            .Options)
    {
        _model = model;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultContainer("Shop");
        _model(modelBuilder);
    }
}

/// <summary>The model the package builds, without a database.</summary>
public class ModelTests
{
    [Fact]
    public void Tenant_property_is_the_partition_key_stored_as_a_string()
    {
        using var ctx = Contexts.Offline();
        var order = ctx.Model.FindEntityType(typeof(Order))!;

        Assert.Equal(["TenantId"], order.GetPartitionKeyPropertyNames());
        Assert.Equal(typeof(string),
            order.FindProperty(nameof(Order.TenantId))!.GetTypeMapping().Converter?.ProviderClrType);
        // The runtime annotation the core interceptors read
        Assert.Equal("TenantId", order.GetTenantPropertyName());
    }

    [Fact]
    public void Data_without_tenant_keeps_its_own_partition_key_and_no_filter()
    {
        using var ctx = Contexts.Offline();
        var country = ctx.Model.FindEntityType(typeof(Country))!;

        Assert.Equal(["Code"], country.GetPartitionKeyPropertyNames());
        Assert.Empty(country.GetDeclaredQueryFilters());
    }

    [Fact]
    public void Tenant_owned_types_get_the_named_query_filter()
    {
        using var ctx = Contexts.Offline();
        var order = ctx.Model.FindEntityType(typeof(Order))!;

        Assert.NotNull(order.FindDeclaredQueryFilter(TenancyModelBuilderExtensions.QueryFilterKey));
    }

    [Fact]
    public void Validate_requires_the_cosmos_provider()
    {
        var options = new DbContextOptionsBuilder().Options;

        Assert.Throws<InvalidOperationException>(() => new CosmosTenancyOptionsExtension().Validate(options));
    }

    [Fact]
    public void Ignored_tenant_property_of_an_ITenantOwned_type_is_rejected()
    {
        using var ctx = new ConfiguredContext(m => m.Entity<Order>().Ignore(o => o.TenantId));

        Assert.Contains("is not mapped", Assert.Throws<InvalidOperationException>(() => ctx.Model).Message);
    }

    [Fact]
    public void Explicit_interface_implementation_needs_a_marked_property()
    {
        using (var unmarked = new ConfiguredContext(m => m.Entity<Contract>()))
            Assert.Contains("IsTenantProperty()",
                Assert.Throws<InvalidOperationException>(() => unmarked.Model).Message);

        using var marked = new ConfiguredContext(
            m => m.Entity<Contract>().Property(c => c.Mandant).IsTenantProperty());
        var contract = marked.Model.FindEntityType(typeof(Contract))!;
        Assert.Equal("Mandant", contract.GetTenantPropertyName());
        Assert.Equal(["Mandant"], contract.GetPartitionKeyPropertyNames());
    }

    [Fact]
    public void Opted_out_tenant_owned_type_gets_neither_partition_key_nor_tenant_annotation()
    {
        using var ctx = new ConfiguredContext(m => m.Entity<OptedOutOrder>());
        var order = ctx.Model.FindEntityType(typeof(OptedOutOrder))!;

        Assert.Null(order.GetTenantPropertyName());
        Assert.NotEqual(["TenantId"], order.GetPartitionKeyPropertyNames());
    }
}

/// <summary>Tenant-owned, but deliberately excluded.</summary>
[NoTenantIsolation]
public class OptedOutOrder : ITenantOwned
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
}
