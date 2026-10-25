using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>Contexts whose model depends on a schema chosen at run time.</summary>
public interface ISchemaScoped
{
    /// <summary>The database schema of this context instance.</summary>
    string Schema { get; }
}

/// <summary>Model cache key that includes the schema: one cached model per schema.</summary>
public sealed class SchemaModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime)
        => context is ISchemaScoped scoped
            ? (context.GetType(), scoped.Schema, designTime)
            : (object)(context.GetType(), designTime);
}

/// <summary>Context for "schema per tenant": the schema is part of the model.</summary>
public sealed class SchemaContext : DbContext, ISchemaScoped
{
    /// <summary>Creates the context for one schema.</summary>
    public SchemaContext(DbContextOptions<SchemaContext> options, string schema)
        : base(options)
    {
        Schema = schema;
    }

    /// <inheritdoc />
    public string Schema { get; }

    /// <summary>Products, not tenant-owned: separation comes from the schema.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.HasDefaultSchema(Schema);
}

/// <summary>
/// The alternative to RLS from chapter "Models per runtime context", checked: one model per
/// schema.
/// </summary>
public class SchemaPerTenantTests
{
    // One options instance, shared like in an application: caching of the internal provider on
    private static readonly DbContextOptions<SchemaContext> Options =
        new DbContextOptionsBuilder<SchemaContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .ReplaceService<IModelCacheKeyFactory, SchemaModelCacheKeyFactory>()
            .Options;

    [Fact]
    public void Each_schema_gets_its_own_model()
    {
        using var a = new SchemaContext(Options, "tenant_a");
        using var b = new SchemaContext(Options, "tenant_b");

        Assert.NotSame(a.Model, b.Model);
        Assert.Equal("tenant_a", a.Model.FindEntityType(typeof(Product))!.GetSchema());
        Assert.Equal("tenant_b", b.Model.FindEntityType(typeof(Product))!.GetSchema());
    }

    [Fact]
    public void Same_schema_shares_the_cached_model()
    {
        using var first = new SchemaContext(Options, "tenant_c");
        using var second = new SchemaContext(Options, "tenant_c");

        Assert.Same(first.Model, second.Model);
    }
}
