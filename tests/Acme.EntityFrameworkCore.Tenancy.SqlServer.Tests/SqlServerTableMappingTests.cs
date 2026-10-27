using Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests;

/// <summary>Tenant-owned root of the mapping tests' hierarchy.</summary>
public abstract class Payment : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
}

/// <summary>Concrete type of the <see cref="Payment"/> hierarchy.</summary>
public class CardPayment : Payment
{
    public string Card { get; set; } = "";
}

/// <summary>Tenant-owned owner of an owned collection.</summary>
public class Basket : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public List<BasketLine> Lines { get; set; } = [];
}

/// <summary>Owned collection type.</summary>
public class BasketLine
{
    public string Sku { get; set; } = "";
}

/// <summary>Tenant-owned side of a many-to-many relationship.</summary>
public class Project : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public List<Label> Labels { get; set; } = [];
}

/// <summary>Other side of the many-to-many relationship.</summary>
public class Label : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public List<Project> Projects { get; set; } = [];
}

/// <summary>Context whose model a test configures.</summary>
public sealed class MappingContext : DbContext
{
    private readonly Action<ModelBuilder> _model;

    public MappingContext(DbContextOptions<MappingContext> options, Action<ModelBuilder> model)
        : base(options)
    {
        _model = model;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) => _model(modelBuilder);

    public static MappingContext Create(Action<ModelBuilder> model, string? connectionString = null)
        => new(new DbContextOptionsBuilder<MappingContext>()
            .UseSqlServer(connectionString ?? "Server=localhost;Database=unused",
                sql => sql.UseAcmeTenancy())
            .EnableServiceProviderCaching(false)
            .Options, model);
}

/// <summary>
/// The table mapping contract for SQL Server: the same validator as for PostgreSQL, so the same
/// mappings are supported and rejected.
/// </summary>
public class SqlServerTableMappingTests
{
    private static string Rejected(Action<ModelBuilder> model)
    {
        using var ctx = MappingContext.Create(model);
        return Assert.Throws<InvalidOperationException>(() => ctx.Model).Message;
    }

    private static ITable Table(DbContext ctx, string name)
        => ctx.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            .Single(t => t.Name == name);

    [Fact]
    public void Tph_hierarchy_gets_one_security_policy_column()
    {
        using var ctx = MappingContext.Create(m =>
        {
            m.Entity<Payment>();
            m.Entity<CardPayment>();
        });

        Assert.Equal("TenantId", Table(ctx, "Payment")[SqlServerRlsAnnotationNames.TenantColumn]);
    }

    [Fact]
    public void Tpc_hierarchy_is_rejected()
        => Assert.Contains("TPC", Rejected(m =>
        {
            m.Entity<Payment>().UseTpcMappingStrategy();
            m.Entity<CardPayment>();
        }));

    [Fact]
    public void Tpt_hierarchy_is_rejected()
        => Assert.Contains("TPT", Rejected(m =>
        {
            m.Entity<Payment>().UseTptMappingStrategy();
            m.Entity<CardPayment>();
        }));

    [Fact]
    public void Owned_collection_in_its_own_table_is_rejected()
        => Assert.Contains("its own table", Rejected(m => m.Entity<Basket>().OwnsMany(b => b.Lines)));

    [Fact]
    public void Owned_collection_as_json_is_protected_with_the_owner()
    {
        using var ctx = MappingContext.Create(m => m.Entity<Basket>().OwnsMany(b => b.Lines, l => l.ToJson()));

        Assert.Equal("TenantId", Table(ctx, "Basket")[SqlServerRlsAnnotationNames.TenantColumn]);
    }

    [Fact]
    public void Implicit_join_table_is_rejected()
        => Assert.Contains("join table", Rejected(m =>
            m.Entity<Project>().HasMany(p => p.Labels).WithMany(l => l.Projects)));
}
