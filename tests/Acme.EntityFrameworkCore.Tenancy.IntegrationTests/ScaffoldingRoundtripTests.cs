using System.Reflection;
using System.Runtime.Loader;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.IntegrationTests;

/// <summary>
/// Full roundtrip as with "dotnet ef dbcontext scaffold": policies in a real database,
/// reverse engineering, compiling the generated code, rebuilding its model and creating the
/// schema from it again (chapter "Scaffolding (reverse engineering)").
/// </summary>
public class ScaffoldingRoundtripTests : IClassFixture<PostgresFixture>
{
    private const string Schema = $"""
        CREATE TABLE scaffold_probe (id integer PRIMARY KEY);
        ALTER TABLE scaffold_probe ENABLE ROW LEVEL SECURITY;
        CREATE POLICY allow_all ON scaffold_probe;
        CREATE TABLE select_probe (id integer PRIMARY KEY);
        ALTER TABLE select_probe ENABLE ROW LEVEL SECURITY;
        CREATE POLICY reader ON select_probe FOR SELECT TO {PostgresFixture.AppRole};
        CREATE POLICY no_limit ON select_probe AS RESTRICTIVE;
        CREATE POLICY first_row ON select_probe FOR SELECT USING (id = 1);
        CREATE TABLE expression_probe (id integer PRIMARY KEY, owner text NOT NULL);
        ALTER TABLE expression_probe ENABLE ROW LEVEL SECURITY;
        ALTER TABLE expression_probe FORCE ROW LEVEL SECURITY;
        CREATE POLICY own ON expression_probe FOR UPDATE USING (owner = current_user) WITH CHECK (owner = current_user);
        """;

    private const string Policies = """
        SELECT string_agg(format('%s.%s %s %s %s %s %s', tablename, policyname, permissive, cmd,
                                 roles::text, coalesce(qual, '-'), coalesce(with_check, '-')), E'\n'
                          ORDER BY tablename, policyname)
        FROM pg_policies WHERE schemaname = 'public'
        """;

    private readonly PostgresFixture _fixture;

    public ScaffoldingRoundtripTests(PostgresFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _fixture = fixture;
    }

    private static ServiceProvider DesignServices()
    {
        var services = new ServiceCollection().AddEntityFrameworkDesignTimeServices();
        new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
        new TenancyDesignTimeServices().ConfigureDesignTimeServices(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Policies_without_expressions_survive_scaffold_compile_and_migrate()
    {
        var (owner, _) = await TestDatabase.CreateAsync(_fixture);
        await TestDatabase.ScalarAsync(owner, Schema);
        var expected = (string)(await TestDatabase.ScalarAsync(owner, Policies))!;

        // 1. Scaffold: before the fix BuildPolicyChain threw ArgumentOutOfRangeException here
        using var services = DesignServices();
        var scaffolded = services.GetRequiredService<IReverseEngineerScaffolder>().ScaffoldModel(
            owner,
            new DatabaseModelFactoryOptions(),
            new ModelReverseEngineerOptions(),
            new ModelCodeGenerationOptions
            {
                RootNamespace = "Scaffolded",
                ModelNamespace = "Scaffolded",
                ContextNamespace = "Scaffolded",
                ContextName = "ProbeContext",
                Language = "C#",
                UseNullableReferenceTypes = true,
                SuppressOnConfiguring = true,       // the test passes its own connection
            });
        var code = string.Concat(scaffolded.ContextFile.Code.Where(c => !char.IsWhiteSpace(c)));
        Assert.Contains("HasPolicy(\"allow_all\",p=>p.For(PolicyCommand.All))", code);
        Assert.Contains("HasPolicy(\"no_limit\",p=>p.Restrictive())", code);
        Assert.Contains("p.For(PolicyCommand.Select).To(new[]{\"shop_app\"}))", code);
        Assert.Contains(".WithCheck(\"(owner=CURRENT_USER)\")", code);

        // 2. The generated code compiles
        var contextType = Compile([scaffolded.ContextFile.Code, .. scaffolded.AdditionalFiles.Select(f => f.Code)])
            .GetType("Scaffolded.ProbeContext", throwOnError: true)!;

        // 3. Its model carries the policies exactly as the database reported them
        var databaseModel = services.GetRequiredService<IDatabaseModelFactory>()
            .Create(owner, new DatabaseModelFactoryOptions());
        var (fresh, freshApp) = await TestDatabase.CreateAsync(_fixture);
        await using var context = CreateContext(contextType, fresh);
        var model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        foreach (var table in databaseModel.Tables)
        {
            var rebuilt = model.FindTable(table.Name, null)!;
            Assert.Equal(table.GetRlsPolicies().OrderBy(p => p.Key), rebuilt.GetRlsPolicies().OrderBy(p => p.Key));
            Assert.Equal(table.IsRlsForced(), rebuilt.IsRlsForced());
        }

        // 4. No phantom migration: the model against itself, rebuilt in another context
        await using (var again = CreateContext(contextType, fresh))
            Assert.Empty(context.GetService<IMigrationsModelDiffer>().GetDifferences(
                model, again.GetService<IDesignTimeModel>().Model.GetRelationalModel()));

        // 5. A schema created from the scaffolded model has the same policies
        Assert.True(await context.Database.EnsureCreatedAsync());
        Assert.Equal(expected, await TestDatabase.ScalarAsync(fresh, Policies));

        // 6. Semantics as the app role: a permissive policy without expressions grants nothing,
        //    even FOR SELECT TO the role; a restrictive one restricts nothing
        await TestDatabase.ScalarAsync(fresh, $"""
            INSERT INTO scaffold_probe VALUES (1), (2);
            INSERT INTO select_probe VALUES (1), (2);
            GRANT SELECT, INSERT ON scaffold_probe, select_probe TO {PostgresFixture.AppRole};
            """);
        Assert.Equal(0L, await TestDatabase.ScalarAsync(freshApp, "SELECT count(*) FROM scaffold_probe"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => TestDatabase.ScalarAsync(freshApp, "INSERT INTO scaffold_probe VALUES (3)"));
        Assert.Equal(1L, await TestDatabase.ScalarAsync(freshApp, "SELECT count(*) FROM select_probe"));
    }

    private static DbContext CreateContext(Type contextType, string connectionString)
    {
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(
            typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        builder.UseNpgsql(connectionString).UseAcmeTenancy().EnableServiceProviderCaching(false);
        return (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
    }

    private static Assembly Compile(IEnumerable<string> sources)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Concat(((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)));
        var compilation = CSharpCompilation.Create(
            "Scaffolded_" + Guid.NewGuid().ToString("N"),
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }
}
