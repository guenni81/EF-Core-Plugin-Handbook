using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>Parameterless and a property constructor.</summary>
public class Receipt
{
    public Receipt()
    {
    }

    public Receipt(int id, string number)
    {
        Id = id;
        Number = number;
    }

    public int Id { get; set; }
    public string Number { get; set; } = "";
}

/// <summary>Parameterless and a service constructor.</summary>
public class Reminder
{
    public Reminder()
    {
    }

    public Reminder(DbContext context) => Context = context;

    public int Id { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DbContext? Context { get; }
}

/// <summary>
/// Constructor binding in EF Core 10 (chapter "Materialization: from rows to objects"): not "the
/// parameterless constructor if there is one", but most service parameters, then fewest property
/// parameters.
/// </summary>
public class ConstructorBindingTests
{
    private static IReadOnlyList<Type> Parameters<T>(Action<ModelBuilder> model)
    {
        using var ctx = Contexts.Create(model: model);
        var binding = ctx.Model.FindEntityType(typeof(T))!.ConstructorBinding!;
        return binding.ParameterBindings.Select(p => p.ParameterType).ToList();
    }

    [Fact]
    public void Fewest_property_parameters_win_without_services()
        => Assert.Empty(Parameters<Receipt>(m => m.Entity<Receipt>()));

    [Fact]
    public void Constructor_with_a_service_wins_over_the_parameterless_one()
        => Assert.Equal([typeof(DbContext)], Parameters<Reminder>(m => m.Entity<Reminder>()));
}
