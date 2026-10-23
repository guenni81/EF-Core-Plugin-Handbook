using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace Acme.EntityFrameworkCore.Tenancy;

/// <summary>
/// Reads/writes <see cref="TenantId"/> in JSON columns and primitive collections
/// (e.g. <c>List&lt;TenantId&gt;</c>). Without a reader/writer these scenarios do not work.
/// </summary>
public sealed class TenantIdJsonReaderWriter : JsonValueReaderWriter<TenantId>
{
    private static readonly PropertyInfo InstanceProperty =
        typeof(TenantIdJsonReaderWriter).GetProperty(nameof(Instance))!;

    /// <summary>Public static instance: required for compiled models.</summary>
    public static TenantIdJsonReaderWriter Instance { get; } = new();

    private TenantIdJsonReaderWriter()
    {
    }

    /// <summary>Reads a JSON string token as a Guid.</summary>
    public override TenantId FromJsonTyped(ref Utf8JsonReaderManager manager, object? existingObject = null)
        => new(manager.CurrentReader.GetGuid());

    /// <summary>Writes the ID as a JSON string, the same format EF uses for Guid.</summary>
    public override void ToJsonTyped(Utf8JsonWriter writer, TenantId value)
        => writer.WriteStringValue(value.Value);

    /// <summary>How the instance is created in generated code (compiled model).</summary>
    public override Expression ConstructorExpression
        => Expression.Property(null, InstanceProperty);
}
