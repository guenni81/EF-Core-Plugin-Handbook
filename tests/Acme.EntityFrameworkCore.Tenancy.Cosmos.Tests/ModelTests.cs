using Acme.EntityFrameworkCore.Tenancy.Cosmos.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.Cosmos.Tests;

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
}
