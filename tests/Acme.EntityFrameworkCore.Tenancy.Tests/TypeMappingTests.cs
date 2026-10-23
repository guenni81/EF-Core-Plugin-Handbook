using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>The type mapping source plugin and the <c>uuid</c> mapping of <see cref="TenantId"/>.</summary>
public class TypeMappingTests
{
    [Fact]
    public void TenantId_maps_to_uuid_without_configuration()
    {
        using var ctx = Contexts.Create();
        var mapping = ctx.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(TenantId));

        Assert.IsType<TenantIdTypeMapping>(mapping);
        Assert.Equal("uuid", mapping!.StoreType);
    }

    [Fact]
    public void Property_uses_the_plugin_mapping()
    {
        using var ctx = Contexts.Create();
        var property = ctx.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.TenantId))!;

        Assert.Equal("uuid", property.GetColumnType());
    }

    [Fact]
    public void Literal_is_a_uuid_cast()
    {
        var id = new TenantId(Guid.Parse("0196f2a4-7a1c-7d3e-9b2a-5c8d1e0f4a6b"));

        Assert.Equal(
            "'0196f2a4-7a1c-7d3e-9b2a-5c8d1e0f4a6b'::uuid",
            TenantIdTypeMapping.Default.GenerateSqlLiteral(id));
    }

    [Fact]
    public void Explicit_other_column_type_is_not_claimed_by_the_plugin()
    {
        // An explicit HasColumnType("text") is not claimed by the plugin, so users can configure
        // their own value converter instead of silently getting a uuid column.
        var plugin = new TenancyTypeMappingSourcePlugin();
        var info = new RelationalTypeMappingInfo(
            type: typeof(TenantId), storeTypeName: "text", storeTypeNameBase: "text");

        Assert.Null(plugin.FindMapping(info));
    }
}
