using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// <see cref="TenantId"/> inside a JSON column: EF reads and writes JSON values through the
/// type mapping's <c>JsonValueReaderWriter</c>, so the plugin's mapping must supply its own.
/// </summary>
public class JsonTests
{
    [Fact]
    public void TenantId_in_json_column_uses_plugin_reader_writer()
    {
        using var ctx = Contexts.Create(model: m =>
            m.Entity<Shipment>().OwnsOne(s => s.Details, d => d.ToJson()));

        var property = ctx.DesignModel()
            .FindEntityType(typeof(ShipmentDetails))!
            .FindProperty(nameof(ShipmentDetails.ApprovedBy))!;

        Assert.Same(TenantIdJsonReaderWriter.Instance, property.GetTypeMapping().JsonValueReaderWriter);
    }
}
