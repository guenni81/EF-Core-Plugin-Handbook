using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>Root type of a TPH hierarchy for the validation tests.</summary>
public class Document : ITenantOwned
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }
}

/// <summary>Derived type: RLS configuration is only allowed on the root type.</summary>
public class Invoice : Document;

/// <summary>
/// Model validation: policies on derived types and invalid USING/WITH CHECK combinations
/// are rejected while the model is built.
/// </summary>
public class ValidationTests
{
    [Fact]
    public void Rls_on_derived_type_is_rejected()
    {
        using var ctx = Contexts.Create(model: m =>
        {
            m.Entity<Document>();
            m.Entity<Invoice>().HasPolicy("invoices_only", p => p.Using("true"));
        });

        var ex = Assert.Throws<InvalidOperationException>(() => ctx.Model);
        Assert.Contains("root type", ex.Message);
    }

    [Theory]
    [InlineData(PolicyCommand.Insert, true, false)]   // INSERT does not allow USING
    [InlineData(PolicyCommand.Select, false, true)]   // SELECT does not allow WITH CHECK
    [InlineData(PolicyCommand.Delete, false, true)]   // DELETE does not allow WITH CHECK
    public void Invalid_policy_is_rejected_while_building_the_model(
        PolicyCommand command, bool withUsing, bool withCheck)
    {
        using var ctx = Contexts.Create(model: m => m.Entity<Order>().HasPolicy("bad", p =>
        {
            p.For(command);
            if (withUsing) p.Using("true");
            if (withCheck) p.WithCheck("true");
        }));

        Assert.Throws<InvalidOperationException>(() => ctx.Model);
    }

    [Theory]
    [InlineData(PolicyCommand.All)]
    [InlineData(PolicyCommand.Select)]
    [InlineData(PolicyCommand.Insert)]
    [InlineData(PolicyCommand.Update)]
    [InlineData(PolicyCommand.Delete)]
    public void Policy_without_expressions_is_valid_as_in_PostgreSQL(PolicyCommand command)
    {
        // CREATE POLICY p ON t; is valid: both clauses are optional
        using var ctx = Contexts.Create(model: m => m.Entity<Order>().HasPolicy("bare", p => p.For(command)));

        var policy = RlsPolicy.Deserialize((string)ctx.DesignModel().FindEntityType(typeof(Order))!
            [RlsAnnotationNames.PolicyPrefix + "bare"]!);
        Assert.Equal(command, policy.Command);
        Assert.Null(policy.Using);
        Assert.Null(policy.WithCheck);
        Assert.Contains(Contexts.MigrationSql(null, ctx),
            s => s == $"CREATE POLICY bare ON \"Orders\" AS PERMISSIVE FOR {command.ToString().ToUpperInvariant()};");
    }
}
