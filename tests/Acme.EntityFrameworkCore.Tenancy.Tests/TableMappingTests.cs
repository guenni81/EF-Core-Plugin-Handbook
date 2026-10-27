using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>Tenant-owned root of the mapping tests' hierarchies.</summary>
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

/// <summary>Second concrete type of the <see cref="Payment"/> hierarchy.</summary>
public class TransferPayment : Payment
{
    public string Iban { get; set; } = "";
}

/// <summary>Root without a tenant; only its derived type is tenant-owned.</summary>
public class Note
{
    public int Id { get; set; }
}

/// <summary>Tenant-owned derived type of a root that is not.</summary>
public class TenantNote : Note, ITenantOwned
{
    public TenantId TenantId { get; set; }
}

/// <summary>Tenant-owned owner of owned types.</summary>
public class Basket : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public Address Delivery { get; set; } = new();
    public List<BasketLine> Lines { get; set; } = [];
}

/// <summary>Owned type, single.</summary>
public class Address
{
    public string City { get; set; } = "";
}

/// <summary>Owned type, collection.</summary>
public class BasketLine
{
    public string Sku { get; set; } = "";
}

/// <summary>Shares the table of <see cref="Basket"/> (table splitting).</summary>
public class BasketInfo
{
    public int Id { get; set; }
    public string Comment { get; set; } = "";
}

/// <summary>Tenant-owned side of the many-to-many tests.</summary>
public class Project : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public List<Label> Labels { get; set; } = [];
}

/// <summary>Other tenant-owned side of the many-to-many tests.</summary>
public class Label : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
    public List<Project> Projects { get; set; } = [];
}

/// <summary>
/// Supported and rejected table mappings (TenantTableMappingValidator): tenant isolation protects
/// tables, so every table with tenant data needs the tenant column.
/// </summary>
public class TableMappingTests
{
    private static InvalidOperationException Rejected(Action<ModelBuilder> model)
    {
        using var ctx = Contexts.Create(model: model);
        return Assert.Throws<InvalidOperationException>(() => ctx.Model);
    }

    private static ITable Table(TestContext ctx, string name)
        => ctx.DesignModel().GetRelationalModel().Tables.Single(t => t.Name == name);

    // ------------------------------------------------ Inheritance

    [Fact]
    public void Tph_hierarchy_protects_its_single_table()
    {
        using var ctx = Contexts.Create(model: m =>
        {
            m.Entity<Payment>();        // base types are only part of the model when included
            m.Entity<CardPayment>();
            m.Entity<TransferPayment>();
        });

        var table = Table(ctx, "Payment");
        Assert.Equal(true, table[RlsAnnotationNames.Enabled]);
        Assert.Equal(3, table.EntityTypeMappings.Count());
        Assert.Single(ctx.DesignModel().GetRelationalModel().Tables, t => t.Name.Contains("Payment"));
    }

    [Fact]
    public void Tpc_hierarchy_is_rejected()
    {
        var ex = Rejected(m =>
        {
            m.Entity<Payment>().UseTpcMappingStrategy();
            m.Entity<CardPayment>();
            m.Entity<TransferPayment>();
        });

        Assert.Contains("more than one table", ex.Message);
        Assert.Contains("TPC", ex.Message);
    }

    [Fact]
    public void Tpt_hierarchy_is_rejected()
    {
        var ex = Rejected(m =>
        {
            m.Entity<Payment>().UseTptMappingStrategy();
            m.Entity<CardPayment>();
        });

        Assert.Contains("TPT", ex.Message);
    }

    [Fact]
    public void Tenant_owned_derived_type_of_a_shared_root_is_rejected()
    {
        var ex = Rejected(m =>
        {
            m.Entity<Note>();
            m.Entity<TenantNote>();
        });

        Assert.Contains("root type 'Note'", ex.Message);
    }

    [Fact]
    public void Deliberately_excluded_hierarchy_may_use_tpc()
    {
        using var ctx = Contexts.Create(model: m =>
        {
            m.Entity<Payment>().UseTpcMappingStrategy().HasNoRowLevelSecurity();
            m.Entity<CardPayment>();
        });

        Assert.Null(Table(ctx, "CardPayment")[RlsAnnotationNames.Enabled]);
    }

    // ------------------------------------------------ Owned types

    [Fact]
    public void Owned_types_in_the_owners_table_or_json_are_protected_with_it()
    {
        using var ctx = Contexts.Create(model: m => m.Entity<Basket>(b =>
        {
            b.OwnsOne(x => x.Delivery);
            b.OwnsMany(x => x.Lines, l => l.ToJson());
        }));

        var basket = Table(ctx, "Basket");
        Assert.Single(basket.EntityTypeMappings, m => !((IEntityType)m.TypeBase).IsOwned());
        Assert.Equal(true, basket[RlsAnnotationNames.Enabled]);
        Assert.Contains(basket.Columns, c => c.Name == "Delivery_City");
        Assert.Contains(basket.Columns, c => c.Name == "Lines");
    }

    [Fact]
    public void Owned_collection_in_its_own_table_is_rejected()
    {
        var ex = Rejected(m => m.Entity<Basket>(b =>
        {
            b.OwnsOne(x => x.Delivery);
            b.OwnsMany(x => x.Lines);
        }));

        Assert.Contains("BasketLine", ex.Message);
        Assert.Contains("its own table", ex.Message);
    }

    [Fact]
    public void Owned_reference_moved_to_its_own_table_is_rejected()
    {
        var ex = Rejected(m => m.Entity<Basket>(b =>
        {
            b.OwnsOne(x => x.Delivery, d => d.ToTable("Deliveries"));
            b.OwnsMany(x => x.Lines, l => l.ToJson());
        }));

        Assert.Contains("Deliveries", ex.Message);
    }

    // ------------------------------------------------ Splitting

    [Fact]
    public void Entity_splitting_is_rejected()
    {
        var ex = Rejected(m => m.Entity<Customer>()
            .SplitToTable("CustomerNames", t => t.Property(c => c.Name)));

        Assert.Contains("CustomerNames", ex.Message);
    }

    [Fact]
    public void Table_splitting_protects_the_shared_table_whichever_type_comes_first()
    {
        using var ctx = Contexts.Create(model: m =>
        {
            m.Entity<Basket>(b =>
            {
                b.ToTable("Baskets");
                b.OwnsOne(x => x.Delivery);
                b.Ignore(x => x.Lines);     // EF: no table splitting with JSON columns
                b.HasOne<BasketInfo>().WithOne().HasForeignKey<BasketInfo>(i => i.Id);
            });
            m.Entity<BasketInfo>().ToTable("Baskets");
        });

        var table = Table(ctx, "Baskets");
        Assert.Equal(2, table.EntityTypeMappings.Count(m => !((IEntityType)m.TypeBase).IsOwned()));
        Assert.Equal(true, table[RlsAnnotationNames.Enabled]);
    }

    // ------------------------------------------------ Many-to-many

    [Fact]
    public void Implicit_join_table_between_tenant_owned_types_is_rejected()
    {
        var ex = Rejected(m => m.Entity<Project>().HasMany(p => p.Labels).WithMany(l => l.Projects));

        Assert.Contains("join table", ex.Message);
    }

    [Fact]
    public void Join_entity_with_a_tenant_property_is_protected()
    {
        using var ctx = Contexts.Create(model: m => m.Entity<Project>()
            .HasMany(p => p.Labels).WithMany(l => l.Projects)
            .UsingEntity("ProjectLabel", j =>
            {
                j.Property<TenantId>("TenantId").IsTenantProperty();
            }));

        Assert.Equal(true, Table(ctx, "ProjectLabel")[RlsAnnotationNames.Enabled]);
    }

    [Fact]
    public void Join_entity_excluded_explicitly_is_accepted()
    {
        using var ctx = Contexts.Create(model: m => m.Entity<Project>()
            .HasMany(p => p.Labels).WithMany(l => l.Projects)
            .UsingEntity("ProjectLabel", j => j.HasNoRowLevelSecurity()));

        Assert.Null(Table(ctx, "ProjectLabel")[RlsAnnotationNames.Enabled]);
    }

    [Fact]
    public void Validator_can_be_called_on_a_finished_model()
    {
        using var ctx = Contexts.Create();

        TenantTableMappingValidator.Validate(ctx.Model, _ => false);
    }
}
