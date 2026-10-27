using System.Text.Json;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// System.Text.Json for <see cref="TenantId"/> without any registration: the attribute on the
/// type selects <see cref="TenantIdJsonConverter"/> (chapter "Complex types and JSON").
/// </summary>
public class TenantIdJsonConverterTests
{
    private sealed record OrderDto(int Id, TenantId Tenant, TenantId? Owner);

    [Fact]
    public void New_and_empty_ids_are_plain_guid_strings()
    {
        var id = TenantId.New();

        Assert.Equal($"\"{id.Value:D}\"", JsonSerializer.Serialize(id));
        Assert.Equal("\"00000000-0000-0000-0000-000000000000\"", JsonSerializer.Serialize(TenantId.Empty));
        Assert.Equal(id, JsonSerializer.Deserialize<TenantId>($"\"{id.Value:D}\""));
        Assert.Equal(TenantId.Empty, JsonSerializer.Deserialize<TenantId>("\"00000000-0000-0000-0000-000000000000\""));
    }

    [Theory]
    [InlineData("\"not-a-guid\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    [InlineData("{\"Value\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("null")]
    public void Invalid_values_throw_a_json_exception(string json)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TenantId>(json));

    [Fact]
    public void Dto_round_trip_including_nullable_and_dictionary_keys()
    {
        var dto = new OrderDto(7, TenantId.New(), null);
        var json = JsonSerializer.Serialize(dto);

        Assert.Contains($"\"Tenant\":\"{dto.Tenant.Value:D}\"", json);
        Assert.Contains("\"Owner\":null", json);
        Assert.Equal(dto, JsonSerializer.Deserialize<OrderDto>(json));

        var byTenant = new Dictionary<TenantId, int> { [dto.Tenant] = 3 };
        var map = JsonSerializer.Serialize(byTenant);
        Assert.Equal($"{{\"{dto.Tenant.Value:D}\":3}}", map);
        Assert.Equal(3, JsonSerializer.Deserialize<Dictionary<TenantId, int>>(map)![dto.Tenant]);
    }
}
