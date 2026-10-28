using System.Reflection;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// The model snapshot: RLS annotations are written as Fluent API calls, and a compiled
/// snapshot rebuilds exactly the same model.
/// </summary>
public class SnapshotTests
{
    private static TestContext CreateContext()
        => Contexts.Create(model: m => m.Entity<Customer>().HasPolicy("support_read", p => p
            .For(PolicyCommand.Select)
            .Restrictive()
            .To("shop_support", "PUBLIC")
            .Using("true")));

    /// <summary>Generates the snapshot code like "dotnet ef migrations add".</summary>
    private static string GenerateSnapshot(DbContext context)
    {
        var services = new ServiceCollection()
            .AddEntityFrameworkDesignTimeServices()
            .AddDbContextDesignTimeServices(context);

        // NpgsqlDesignTimeServices lives in an Npgsql *.Internal namespace (EF1001). The plugin
        // replaces the provider's IAnnotationCodeGenerator, which turns annotations into Fluent API.
        new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
        new TenancyDesignTimeServices().ConfigureDesignTimeServices(services);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMigrationsCodeGeneratorSelector>()
            .Select(language: null)
            .GenerateSnapshot("Generated", context.GetType(), "TestSnapshot", context.DesignModel());
    }

    // ------------------------------------------------ String tests: show WHAT differs
    [Fact]
    public void Snapshot_uses_fluent_api_instead_of_raw_annotations()
    {
        using var ctx = CreateContext();
        var code = GenerateSnapshot(ctx);

        // EF Core 10 writes extension methods of the snapshot as static calls
        // (Extensions.Method(b, ...)) to avoid ambiguities between providers.
        Assert.Contains("RlsEntityTypeBuilderExtensions.HasRowLevelSecurity(b, true)", code);
        Assert.Contains("RlsEntityTypeBuilderExtensions.HasPolicy(b, \"tenant_isolation\", p => p", code);
        Assert.Contains("RlsEntityTypeBuilderExtensions.HasPolicy(b, \"support_read\", p => p", code);
        Assert.Contains("PolicyCommand.Select)", code);
        Assert.Contains(".Restrictive()", code);
        Assert.DoesNotContain("Acme:Rls:Policy:", code);
        Assert.DoesNotContain("Acme:Rls:Enabled\", true", code);
    }

    [Fact]
    public void Opt_out_marker_stays_a_raw_annotation()
    {
        using var ctx = CreateContext();

        Assert.Contains("HasAnnotation(\"Acme:Rls:Enabled\", false)", GenerateSnapshot(ctx));
    }

    // ------------------------------------------------ Roundtrip: catches ANY difference
    [Fact]
    public void Snapshot_roundtrip_produces_no_differences()
    {
        using var ctx = CreateContext();
        var snapshot = Compile(GenerateSnapshot(ctx));

        // As in EF's migrator: finalize and initialize the snapshot model. Without this the
        // relational model is missing and the differ would report spurious differences.
        var model = snapshot.Model;
        if (model is IMutableModel mutable)
            model = mutable.FinalizeModel();

        model = ctx.GetService<IModelRuntimeInitializer>()
            .Initialize(model, designTime: true, validationLogger: null);

        var differences = ctx.GetService<IMigrationsModelDiffer>().GetDifferences(
            model.GetRelationalModel(), ctx.DesignModel().GetRelationalModel());

        Assert.Empty(differences);
    }

    [Fact]
    public void Explicit_force_false_survives_the_snapshot_roundtrip()
    {
        using var ctx = Contexts.Create(model: m => m.Entity<Order>().HasRowLevelSecurity(force: false));
        var code = GenerateSnapshot(ctx);
        Assert.Contains("RlsEntityTypeBuilderExtensions.HasRowLevelSecurity(b, false)", code);

        var model = Compile(code).Model;
        if (model is IMutableModel mutable)
            model = mutable.FinalizeModel();
        model = ctx.GetService<IModelRuntimeInitializer>()
            .Initialize(model, designTime: true, validationLogger: null);

        Assert.False(model.FindEntityType(typeof(Order).FullName!)!.IsRlsForced());
        Assert.Empty(ctx.GetService<IMigrationsModelDiffer>().GetDifferences(
            model.GetRelationalModel(), ctx.DesignModel().GetRelationalModel()));
    }

    private static ModelSnapshot Compile(string code)
    {
        // Load the required assemblies: AppDomain.GetAssemblies() below only returns
        // assemblies that have already been loaded.
        _ = typeof(RlsEntityTypeBuilderExtensions).Assembly;
        _ = typeof(RlsAnnotationNames).Assembly;
        _ = typeof(NpgsqlModelBuilderExtensions).Assembly;

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location));

        var compilation = CSharpCompilation.Create(
            "GeneratedSnapshot",
            [CSharpSyntaxTree.ParseText(code)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, "Snapshot does not compile:\n"
            + string.Join("\n", result.Diagnostics) + "\n" + code);

        var type = Assembly.Load(stream.ToArray()).GetTypes()
            .Single(t => typeof(ModelSnapshot).IsAssignableFrom(t));
        return (ModelSnapshot)Activator.CreateInstance(type)!;
    }
}
