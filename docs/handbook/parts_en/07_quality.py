# ============================================================ PART VII
S += part(
    "Quality and delivery",
    "Testing, versioning, packaging, and the checklist from A to Z.",
    ["Chapter 23: Test strategy",
     "Chapter 24: Versioning and compatibility",
     "Chapter 25: Packaging and documentation",
     "Chapter 26: The checklist from A to Z"])

# ------------------------------------------------------------ 23
S += h1("Test strategy")
S.append(p(
    "Plugins can be tested to a surprising extent without a database. Model building, type "
    "mappings, SQL translation, migration SQL and snapshots only need a connection string, "
    "no connection. The sample's test strategy has four levels:"))
S += data_table(
    ["Level", "Checks", "Database"],
    [
        ["Model tests", "Annotations, conventions, validation, options, caching", "no"],
        ["SQL tests", "Translations via " + c("ToQueryString") + ", migration SQL via "
         "differ and generator", "no"],
        ["Snapshot tests", "Code generator as a string and as a round trip (Roslyn)", "no"],
        ["Integration tests", "Real behavior: RLS, roles, interceptors (Testcontainers)",
         "yes (Docker)"],
    ], [1.2, 2.6, 0.8])

S += h2("Test helpers")
S.append(p(
    "Tests build many contexts with different " + c("OnModelCreating") + ". Because EF "
    "caches the model per internal provider, every test must get its own provider; that is "
    "what " + c("EnableServiceProviderCaching(false)") + " is for:"))
S += code_block('''
public static TestContext Create(
    Action<TenancyOptionsBuilder>? tenancy = null,
    Action<ModelBuilder>? model = null,
    bool cacheServiceProvider = false,
    Action<DbContextOptionsBuilder>? configure = null)
{
    var builder = new DbContextOptionsBuilder<TestContext>()
        .UseNpgsql("Host=localhost;Database=unused")       // no connection needed
        .UseAcmeTenancy(tenancy)
        .EnableServiceProviderCaching(cacheServiceProvider);
    configure?.Invoke(builder);
    return new TestContext(builder.Options, model);
}

public static IReadOnlyList<string> MigrationSql(DbContext? source, DbContext target)
{
    var operations = target.GetService<IMigrationsModelDiffer>().GetDifferences(
        source?.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
        target.GetService<IDesignTimeModel>().Model.GetRelationalModel());

    return target.GetService<IMigrationsSqlGenerator>()
        .Generate(operations, target.GetService<IDesignTimeModel>().Model)
        .Select(c => c.CommandText.Trim()).ToList();
}
''', label="tests/.../TestModel.cs (shortened)")
S += callout("important", [
    "Always use the <b>design-time model</b> for migration tests (" +
    c("IDesignTimeModel") + "). " + c("context.Model") + " is the lean runtime model; "
    "annotations that the annotation provider only passes on at design time are missing "
    "from it.",
], title="Use the design-time model")
S += callout("note", [
    "Tests against SQLite or the InMemory provider are tempting because they need no "
    "Docker. For a plugin they prove little: InMemory is not relational, so type mappings, "
    "translators and migrations never run; SQLite has a different SQL dialect, other type "
    "mappings and no Row Level Security. A provider-specific plugin is tested with its own "
    "provider: without a database (connection string only) for everything up to the SQL "
    "text, and against the real database for behavior.",
], title="Why not SQLite or InMemory?")

S += h2("What is tested")
S += data_table(
    ["Test", "Ensures"],
    [
        ["Caching with different options", "Two contexts with different settings get "
         "different models (hash mistake from chapter 4)."],
        ["Invalid setting names", "Validation including an injection attempt."],
        ["Type mapping without configuration", c("TenantId") + " becomes " + c("uuid") +
         "; literal format; a different column type is not claimed."],
        [c("CurrentTenant()") + " via " + c("ToQueryString"), "Translation, and that nothing "
         "is evaluated client-side (chapter 13)."],
        ["Explicit beats convention", "An own policy with the same name is kept; the Fluent "
         "API overrides the attribute."],
        ["Warning as error", "The opt-out warning becomes an error via " +
         c("ConfigureWarnings") + "."],
        ["Validation", "RLS on a derived type and invalid policies throw while building "
         "the model."],
        ["No phantom migration", "An unchanged model produces not a single operation."],
        ["Migration SQL", "Initial, change (DROP + CREATE), opt-out, custom operations."],
        ["SaveChanges logic", "Setting, foreign tenant, reassignment, no tenant."],
        ["Snapshot", "Fluent API instead of annotations; round trip without differences."],
        ["RLS in PostgreSQL", "Isolation, no rows without a tenant, WITH CHECK applies."],
        ["Edge cases (chapters 27–32)", "ExecuteUpdate and raw SQL against RLS, value "
         "generation, JSON columns, compiled model, idempotent script."],
        ["Composability", "Decorator around the migrations generator (chapter 5), "
         "EFCore.NamingConventions in both orders (chapter 8)."],
        ["Annotation format", "Serialized policies stay identical; tolerant reading "
         "(chapter 24)."],
        ["Context lifetimes", "DbContext factory, background jobs, open connections "
         "(chapters 15, 35)."],
        ["Cross-tenant access", "Reporting role reads all tenants but cannot write; the app "
         "role cannot switch RLS off (chapter 33)."],
    ], [1.5, 2.5])

S += h2("The snapshot round trip")
S.append(p(
    "The most valuable test for design-time code: generate the snapshot, compile it with "
    "Roslyn, build the model from it and diff it against the original. Any discrepancy "
    "between code generator and Fluent API would otherwise produce a phantom migration "
    "for every user:"))
S += code_block('''
[Fact]
public void Snapshot_roundtrip_produces_no_differences()
{
    using var ctx = CreateContext();
    var snapshot = Compile(GenerateSnapshot(ctx));

    // As in EF's migrator: finalize and initialize
    var model = snapshot.Model;
    if (model is IMutableModel mutable) model = mutable.FinalizeModel();
    model = ctx.GetService<IModelRuntimeInitializer>()
        .Initialize(model, designTime: true, validationLogger: null);

    var differences = ctx.GetService<IMigrationsModelDiffer>().GetDifferences(
        model.GetRelationalModel(), ctx.DesignModel().GetRelationalModel());

    Assert.Empty(differences);
}
''', label="tests/.../SnapshotTests.cs (excerpt)")

S += h2("Integration tests with Testcontainers")
S.append(p(
    "The real security behavior can only be verified against PostgreSQL. The fixture "
    "starts a container, creates the schema with " + c("EnsureCreated") + " (including "
    "RLS, chapter 17), executes the custom migration operations for real and connects the "
    "tests with a restricted role:"))
S += code_block('''
await owner.Database.EnsureCreatedAsync();          // RLS, policies, index

var commands = owner.GetService<IMigrationsSqlGenerator>().Generate(
[
    new CreateApplicationRoleOperation { Name = AppRole, CanLogin = true },
    new GrantTablePrivilegesOperation { Role = AppRole },
]);
await owner.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(
    commands, owner.GetService<IRelationalConnection>(),
    new MigrationExecutionState(), commitTransaction: true);
''', label="tests/...IntegrationTests/PostgresFixture.cs (excerpt)")
S += code_block('''
[Fact]
public async Task Database_rejects_rows_of_other_tenant()
{
    // Bypass the interceptor: direct SQL. The policy's WITH CHECK must apply.
    var tenants = new MutableTenantProvider { CurrentTenantId = TenantId.New() };
    await using var db = _fixture.CreateAppContext(tenants);

    var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
        $"INSERT INTO \\"Orders\\" (\\"TenantId\\", \\"Total\\") VALUES ({Guid.NewGuid()}, 1)"));

    Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
}
''', label="tests/...IntegrationTests/RowLevelSecurityTests.cs (excerpt)")
S += callout("tip", [
    "For providers and very deep plugins, EF Core provides the <b>specification tests</b> (" +
    c("Microsoft.EntityFrameworkCore.Relational.Specification.Tests") + "). This is the "
    "same test suite EF and the providers themselves are tested against. The "
    "NetTopologySuite plugin in the EF repository shows how a plugin uses it.",
])

# ------------------------------------------------------------ 24
S += h1("Versioning and compatibility")
S += h2("Align versions with EF Core")
S.append(p(
    "The plugin's major version follows EF Core's: version 10.x for EF Core 10. Users "
    "immediately see which combination fits, and the plugin can adopt breaking changes "
    "with every EF major version. Dependencies are limited to this major version:"))
S += code_block('''
<PackageVersion Include="Microsoft.EntityFrameworkCore" Version="[10.0.0,11.0.0)" />
<PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="[10.0.0,11.0.0)" />
''', lang="xml", label="Directory.Packages.props (excerpt)")

S += h2("Dealing with internal APIs")
S.append(p(
    "Plugins rarely get by without internal APIs: the sample derives from " +
    c("NpgsqlMigrationsSqlGenerator") + ", " + c("NpgsqlAnnotationProvider") + " and " +
    c("NpgsqlAnnotationCodeGenerator") + " and uses " + c("NpgsqlOptionsExtension") +
    ". Such types can change even in minor versions. EF reports their use with warning "
    "<b>EF1001</b>."))
S += data_table(
    ["Measure", "Effect"],
    [
        ["Look for a public alternative", "Every internal API avoided is one risk less."],
        ["Bundle internal access", "A few classes encapsulate it; a break affects one place."],
        ["Set " + c("<NoWarn>EF1001</NoWarn>") + " deliberately", "Project-wide and "
         "documented, not scattered per line."],
        ["Limit the version range", "Prevents users from getting untested combinations."],
        ["CI against previews", "A build against the next EF preview shows breaks months "
         "earlier."],
        ["Round trip and SQL tests", "Detect behavior changes the compiler does not see."],
    ], [1.6, 2.4])
S += callout("note", [
    "Supporting several EF major versions at once is possible through multi-targeting, but "
    "doubles test effort and conditional compilation. For most plugins, one release branch "
    "per EF major version is simpler.",
], title="Multi-targeting")

S += h2("Evolving annotation formats")
S.append(p(
    "Everything a plugin writes into annotations ends up in its users' model snapshots, "
    "often long before the next upgrade. If a new plugin version serializes the same "
    "configuration differently (an additional JSON property, a different order, another "
    "enum spelling), the differ compares two different strings. Every user then gets a "
    "migration that drops and recreates every policy without any actual change. The "
    "serialized format is therefore part of the public API, just like the methods."))
S += data_table(
    ["Rule", "How"],
    [
        ["Pin the format with a test", "A golden-value test compares the serialized form "
         "character by character."],
        ["Read tolerantly", "Missing properties become defaults, unknown ones are ignored "
         "(the default behavior of System.Text.Json)."],
        ["Only add, and do not write defaults", "New optional properties with " +
         c("[JsonIgnore(Condition = WhenWritingDefault)]") + " keep existing annotations "
         "identical."],
        ["Real format changes only with a major version", "Document them, ideally together "
         "with the EF major upgrade, when users expect migrations anyway."],
    ], [1.7, 2.6])
S += code_block('''
[Fact]
public void Serialized_policy_format_is_stable()
{
    var policy = new RlsPolicy("support_read", PolicyCommand.Select, Permissive: false,
        Roles: ["shop_support"], Using: "true", WithCheck: null);

    // Golden value: changing it is a breaking change for existing snapshots
    Assert.Equal(
        """{"Name":"support_read","Command":"Select","Permissive":false"""
        + ""","Roles":["shop_support"],"Using":"true","WithCheck":null}""",
        policy.Serialize());
}
''', label="tests/.../AnnotationFormatTests.cs (excerpt)")
S.append(p(
    "Two further tests check tolerant reading in both directions. The snapshot round trip "
    "from chapter 23 complements them: the round trip shows that the current code "
    "generator and Fluent API agree, the golden value shows that the next version still "
    "agrees with the snapshots users already have."))

# ------------------------------------------------------------ 25
S += h1("Packaging and documentation")
S.append(p(
    "Shared settings live in " + c("Directory.Build.props") + ", package versions centrally "
    "in " + c("Directory.Packages.props") + " (Central Package Management):"))
S += code_block('''
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  <NoWarn>$(NoWarn);EF1001</NoWarn>
  <Deterministic>true</Deterministic>
</PropertyGroup>
<PropertyGroup>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <VersionPrefix>10.0.0</VersionPrefix>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>      <!-- SourceLink -->
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
</PropertyGroup>
''', lang="xml", label="Directory.Build.props (shortened)")
S += data_table(
    ["Element", "Why"],
    [
        [c("GenerateDocumentationFile"), "XML docs in the package: users see explanations in "
         "the IDE."],
        [c("IsAotCompatible"), "Analyzers report AOT and trimming problems at build time."],
        ["SourceLink, " + c(".snupkg"), "Users can debug into the plugin's code. Built into "
         "the SDK since .NET 8."],
        ["README in the package", "Shown on nuget.org; minimal example and compatibility."],
        ["CHANGELOG", "Mark breaking changes clearly, especially on EF major upgrades."],
        [c("InternalsVisibleTo"), "Tests reach internal logic without public API."],
        ["Package names", "Core without provider, provider packages with a suffix (" +
         c(".PostgreSQL") + ", " + c(".SqlServer") + ")."],
    ], [1.4, 2.6])
S.append(p(
    "Besides the API docs, the documentation above all needs the things users do not see "
    "in the code: which single services are replaced (composability), which event IDs "
    "exist, which limitations apply (pooling) and which security assumptions hold "
    "(database roles)."))

# ------------------------------------------------------------ 26
S += h1("The checklist from A to Z")
S.append(p("For every new plugin, in the order of development:"))
check = [
    ("Architecture", [
        "Decide the level: helpers, model helpers, real plugin or provider?",
        "Separate core and provider packages; types for entity assemblies go into the core.",
        "For every extension point, look for a plugin interface first, replace only then.",
    ]),
    ("Options", [
        "Extension immutable with copy constructor and virtual Clone.",
        "All model-relevant options in the hash, nothing that varies per context.",
        "Validate for preconditions (provider, combinations).",
        "Maintain LogFragment and PopulateDebugInfo.",
        "Use method in the namespace Microsoft.EntityFrameworkCore, generic and non-generic.",
    ]),
    ("Services", [
        "Registration through the services builder (lifetimes from EF's table).",
        "Decorate single services where the public interface suffices (chapter 5).",
        "Otherwise Replace, and offer the logic publicly and virtually.",
        "ISingletonOptions for singleton services, registered twice.",
        "App dependencies via AddInterceptors, document pooling behavior.",
    ]),
    ("Model", [
        "Annotations with a prefix, simple values only, no stored defaults.",
        "Implement the Fluent API non-generically, delegate generically.",
        "Convention builder overloads for ConfigurationSource.",
        "Conventions at ModelFinalizing when they depend on the finished model.",
        "Attributes in the core package, attribute conventions in the provider package.",
        "Validation as IModelFinalizedConvention: throw on errors, warn on questionable cases.",
    ]),
    ("Type mappings", [
        "Protected parameter constructor and Clone override.",
        "Literal generation for model and provider type.",
        "Public static Default property, shared converter instance.",
        "JsonValueReaderWriter with Instance and ConstructorExpression.",
    ]),
    ("Queries", [
        "EF.Functions methods in the core, translation in the provider package.",
        "Translators: null for foreign methods, correct nullability, set type mappings.",
        "IEvaluatableExpressionFilterPlugin for methods without data reference.",
        "Never insert run-time values at translation time (query cache, AOT).",
    ]),
    ("Run time", [
        "Implement interceptors synchronously and asynchronously.",
        "Singleton interceptors stateless and thread-safe.",
        "Own event IDs in an own range, EventDefinitions cached.",
        "Security-relevant cases as warnings, escalatable via ConfigureWarnings.",
        "Where hooks cannot run, offer an explicit API (e.g. ApplyTenantSession).",
    ]),
    ("Migrations & design time", [
        "Declarative via annotation provider (design time only) and SQL generator.",
        "Escape identifiers, literals via type mappings, validate self-generated SQL.",
        "Custom operations idempotent, extensions in the migrations namespace.",
        "DesignTimeServicesReference with forProvider; code generator removes annotations.",
        "Check compiled models: Default, Instance, runtime annotations.",
    ]),
    ("Quality", [
        "Model, SQL, snapshot round trip and integration tests.",
        "Caching test with different options; test for no phantom migration.",
        "Version range per EF major, CI against previews, EF1001 deliberately.",
        "XML docs, SourceLink, symbols, README, CHANGELOG, limitations documented.",
        "Pin serialized annotation formats with golden-value tests.",
        "Test together with popular plugins (naming conventions, decorators).",
    ]),
    ("Advanced (part VIII)", [
        "Document paths that bypass interceptors (ExecuteUpdate, raw SQL).",
        "Configure value generators with HasValueGeneratorFactory(Type) (compiled models).",
        "Check conventions for complex types and JSON columns.",
        "Document and test the order of custom migration operations.",
        "Consider idempotent scripts and suppressTransaction.",
        "Document operational assumptions (roles, pooling, transaction pooling).",
        "Document context lifetimes: factories, background jobs, open connections.",
        "Cross-tenant access only through separate roles, never through app switches.",
        "Measure hot paths (interceptors), do not guess.",
        "CI with preview build and public API analyzer.",
    ]),
    ("Deep extensions (part IX)", [
        "Custom SQL nodes: VisitChildren, Update, Quote, Print, Equals/GetHashCode.",
        "Handle custom nodes in the SQL generator AND in the nullability processor.",
        "Aggregates: handle predicates, DISTINCT and empty sets deliberately.",
        "Choose runtime annotations deliberately, filter design-time annotations.",
        "Scaffolding: read the plugin's schema objects from the database.",
        "Provider-neutral building blocks into the core, differences into provider packages.",
    ]),
]
for group, items in check:
    S += h2(group)
    for item in items:
        S.append(Paragraph("☐&nbsp;&nbsp;" + item, BODY_TIGHT))
    S.append(Spacer(1, 4))
