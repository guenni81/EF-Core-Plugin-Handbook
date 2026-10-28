using Acme.EntityFrameworkCore.Tenancy.Metadata;
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

    public static MappingContext Create(
        Action<ModelBuilder> model,
        string? connectionString = null,
        string sessionKey = "TenantId",
        string policySchema = "acme",
        ITenantProvider? tenants = null)
    {
        var builder = new DbContextOptionsBuilder<MappingContext>()
            .UseSqlServer(connectionString ?? "Server=localhost;Database=unused",
                sql => sql.UseAcmeTenancy(sessionKey, policySchema))
            .EnableServiceProviderCaching(false);

        if (tenants is not null)
            builder.AddInterceptors(
                new Runtime.SqlServerTenantSessionInterceptor(tenants),
                new Interceptors.TenantSaveChangesInterceptor(tenants));

        return new MappingContext(builder.Options, model);
    }
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

/// <summary>Entity type without tenant reference.</summary>
public class Country
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// Temporal tables: the security policy protects the current table only, the history table
/// stays readable for every tenant via FOR SYSTEM_TIME (chapter "Multiple providers").
/// </summary>
public class SqlServerTemporalTableTests
{
    private static ITable Table(DbContext ctx, string name)
        => ctx.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            .Single(t => t.Name == name);

    [Fact]
    public void Temporal_tenant_owned_table_is_rejected()
    {
        using var ctx = MappingContext.Create(m => m.Entity<Order>().ToTable(t => t.IsTemporal()));

        var message = Assert.Throws<InvalidOperationException>(() => ctx.Model).Message;

        Assert.Contains("temporal table", message);
        Assert.Contains("history table", message);
    }

    [Fact]
    public void Temporal_tph_hierarchy_is_rejected()
    {
        using var ctx = MappingContext.Create(m =>
        {
            m.Entity<Payment>().ToTable(t => t.IsTemporal());
            m.Entity<CardPayment>();
        });

        Assert.Contains("temporal table", Assert.Throws<InvalidOperationException>(() => ctx.Model).Message);
    }

    [Fact]
    public void Temporal_table_without_tenant_reference_stays_supported()
    {
        using var ctx = MappingContext.Create(m =>
        {
            m.Entity<Country>().ToTable(t => t.IsTemporal());
            m.Entity<Order>();
        });

        Assert.True(ctx.Model.FindEntityType(typeof(Country))!.IsTemporal());
        Assert.Null(Table(ctx, "Country")[SqlServerRlsAnnotationNames.TenantColumn]);
        Assert.Equal("TenantId", Table(ctx, "Order")[SqlServerRlsAnnotationNames.TenantColumn]);
    }

    [Fact]
    public void Temporal_table_of_an_opted_out_entity_type_stays_supported()
    {
        using var ctx = MappingContext.Create(m => m.Entity<AuditEntry>().ToTable(t => t.IsTemporal()));

        Assert.True(ctx.Model.FindEntityType(typeof(AuditEntry))!.IsTemporal());
        Assert.Null(Table(ctx, "AuditEntry")[SqlServerRlsAnnotationNames.TenantColumn]);
        Assert.DoesNotContain(Contexts.CreateSql(ctx), s => s.Contains("SECURITY POLICY"));
    }
}

/// <summary>Implements the interface explicitly; the mapped property has another name.</summary>
public class Contract : ITenantOwned
{
    public int Id { get; set; }
    public TenantId Mandant { get; set; }

    TenantId ITenantOwned.TenantId
    {
        get => Mandant;
        set => Mandant = value;
    }
}

/// <summary>
/// An ITenantOwned type without a mapped tenant property must fail while the model is built,
/// as with PostgreSQL; otherwise it would lose its policy and the SaveChanges check silently.
/// </summary>
public class SqlServerTenantPropertyValidationTests
{
    private static ITable Table(DbContext ctx, string name)
        => ctx.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            .Single(t => t.Name == name);

    [Fact]
    public void Ignored_tenant_property_is_rejected()
    {
        using var ctx = MappingContext.Create(m => m.Entity<Order>().Ignore(o => o.TenantId));

        var message = Assert.Throws<InvalidOperationException>(() => ctx.Model).Message;

        Assert.Contains("'Order' implements ITenantOwned", message);
        Assert.Contains("is not mapped", message);
    }

    [Fact]
    public void Explicit_interface_implementation_without_marking_is_rejected()
    {
        using var ctx = MappingContext.Create(m => m.Entity<Contract>());

        Assert.Contains("IsTenantProperty()",
            Assert.Throws<InvalidOperationException>(() => ctx.Model).Message);
    }

    [Fact]
    public void Explicitly_marked_property_with_another_name_is_protected()
    {
        using var ctx = MappingContext.Create(
            m => m.Entity<Contract>().Property(c => c.Mandant).IsTenantProperty());

        Assert.Equal("Mandant", ctx.Model.FindEntityType(typeof(Contract))!.GetTenantPropertyName());
        Assert.Equal("Mandant", Table(ctx, "Contract")[SqlServerRlsAnnotationNames.TenantColumn]);
        Assert.Contains(Contexts.CreateSql(ctx), s => s.Contains("CREATE SECURITY POLICY"));
    }

    [Fact]
    public void Opted_out_entity_type_stays_unprotected_without_error()
    {
        using var ctx = MappingContext.Create(m => m.Entity<AuditEntry>());

        Assert.Null(ctx.Model.FindEntityType(typeof(AuditEntry))!.GetTenantPropertyName());
        Assert.Null(Table(ctx, "AuditEntry")[SqlServerRlsAnnotationNames.TenantColumn]);
    }

    [Fact]
    public void Entity_type_without_tenant_reference_is_unaffected()
    {
        using var ctx = MappingContext.Create(m => m.Entity<Country>());

        Assert.Null(ctx.Model.FindEntityType(typeof(Country))!.GetTenantPropertyName());
    }
}
