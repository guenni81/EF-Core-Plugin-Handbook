# ============================================================ Title & preface
S += title_block(
    "EF Core plugin development · reference and practice",
    "The EF Core Plugin Handbook",
    "From the options extension to the NuGet package: everything you need for professional "
    "and even very complex extensions of Entity Framework Core. Explained throughout with a "
    "complete multi-tenancy plugin for PostgreSQL and SQL Server.",
    [
        ("Versions", "EF Core 10, Npgsql.EntityFrameworkCore.PostgreSQL 10, .NET 10"),
        ("Sample", "Complete solution at "
                   "<b>github.com/guenni81/EF-Core-Plugin-Handbook</b>: "
                   "core package, two provider packages, two sample apps, unit and "
                   "integration tests"),
        ("Verified", "The sample code is built against EF Core 10 and Npgsql 10; unit and "
                     "integration tests run against real PostgreSQL and SQL Server."),
        ("Structure", "9 parts, 42 chapters, 4 appendices"),
        ("License", "MIT License, © 2026 guenni81. Free to use, copy, modify and share, "
                    "including commercially; see the last page."),
    ])

S.append(Paragraph("About this book", H2))
S.append(p(
    "Many EF Core extensions start out as a handful of extension methods and then grow "
    "without a plan. If you understand how EF Core is built internally, however, you can "
    "write extensions that feel like part of EF Core itself: they are configured through "
    "the options, run inside the internal service provider, translate custom functions to "
    "SQL, generate migrations, write readable snapshots, and combine cleanly with other "
    "plugins."))
S.append(p(
    "This book explains each of these building blocks in two layers. First comes the "
    "<b>principle</b>: how does the mechanism work in EF Core, which variants exist, and "
    "what do you need to watch out for? Then comes the <b>implementation</b> in the sample "
    "plugin, with the code that is also in the repository. The chapters build on each "
    "other, but can also be read individually as a reference. The code in the repository "
    "and the snippets in this edition use English comments and messages. Each snippet's "
    "label names the file."))
S += data_table(
    ["Box", "Meaning"],
    [
        ["<font color='#d99a00'><b>Important</b></font>",
         "Pitfalls that lead to incorrect behavior or security holes."],
        ["<font color='#2e8b57'><b>Tip</b></font>",
         "Proven practice that saves work or improves quality."],
        ["<font color='#1d4e89'><b>Note</b></font>",
         "Background, context and references to other chapters."],
    ], [1, 3])
S += callout("note", [
    "Some APIs that plugins need live in namespaces ending in " + c(".Internal") +
    ". EF Core calls them “pubternal”: publicly visible, but without compatibility "
    "guarantees. The book marks these places, and chapter 24 explains how to deal with them.",
], title="Public and internal APIs")

S.append(PageBreak())
S.append(Paragraph("Contents", H2))
S.append(toc())

# ============================================================ PART I
S += part(
    "Foundations",
    "What a plugin is, how EF Core is built internally, and which scenario accompanies us "
    "through the book.",
    ["Chapter 1: What is an EF Core plugin?",
     "Chapter 2: EF Core's architecture from a plugin's point of view",
     "Chapter 3: The sample scenario"])

# ------------------------------------------------------------ 1
S += h1("What is an EF Core plugin?")
S.append(p(
    "The term “plugin” is used for very different things. For this book, it helps to "
    "distinguish four levels that differ in how deeply they reach into EF Core:"))
S += data_table(
    ["Level", "Description", "Examples"],
    [
        ["1 · Helpers", "Extension methods on " + c("DbContext") + " or " +
         c("IQueryable") + " that only call public API.",
         "Paging helpers, " + c("WhereIf") + ", seeding methods"],
        ["2 · Model helpers", "Fluent API extensions and annotations, without services of "
         "their own inside EF Core.", "Custom " + c("HasXyz()") + " methods, ModelBuilder helpers"],
        ["3 · Real plugins", "Own services in the internal service provider: conventions, "
         "query translation, type mappings, migrations, design time.",
         "NetTopologySuite plugin, NodaTime plugin, EFCore.NamingConventions"],
        ["4 · Providers", "A complete database integration including connection, update "
         "pipeline and SQL generation.", "Npgsql, Pomelo (MySQL), SQLite, Cosmos"],
    ], [1.1, 2.4, 1.6])
S.append(p(
    "This book covers <b>level 3</b> completely. Levels 1 and 2 need no book of their own: "
    "they only use EF's public API, and chapter 7 covers what is specific to Fluent API "
    "extensions. The tools of level 4 (such as replacing the SQL generator) are explained "
    "where plugins need them."))
S += h2("Who this book is for")
S.append(p(
    "For developers who already use EF Core confidently: " + c("DbContext") + ", "
    "migrations, LINQ queries and dependency injection are assumed. No knowledge of EF's "
    "internals is required; the book introduces them where they are needed. Experience "
    "with PostgreSQL helps with the sample, but every SQL construct is explained when it "
    "first appears. Chapter 2 ends with a complete mini plugin that shows all building "
    "blocks on a single page before the large sample begins."))
S += h2("What a level 3 plugin can do")
S.append(p(
    "A complete plugin can extend EF Core at practically every point: it can make new CLR "
    "types available as column types, translate custom functions in LINQ to SQL, adapt the "
    "model automatically through conventions, accept metadata through the Fluent API and "
    "attributes, validate the model, intervene when saving and when opening connections, "
    "log its own events, add custom SQL to migrations and extend the " + c("dotnet ef") +
    " tooling. The sample in this book uses every one of these possibilities."))
S += callout("tip", [
    "Before writing your own, look at existing plugins. <b>EFCore.NamingConventions</b> is "
    "a small, clean convention plugin. The <b>NetTopologySuite</b> plugin in the EF Core "
    "repository shows type mappings and translations. Npgsql's <b>NodaTime</b> plugin shows "
    "a provider-specific plugin activated through the provider's options builder.",
], title="Role models in the ecosystem")

# ------------------------------------------------------------ 2
S += h1("EF Core's architecture from a plugin's point of view")
S += h2("Two containers: the internal service provider")
S.append(p(
    "Every " + c("DbContext") + " resolves its infrastructure from an <b>internal</b> " +
    c("IServiceProvider") + ". It is separate from the application's DI container and "
    "contains several hundred services: type mapping, model building, the query pipeline, "
    "change tracking, the update pipeline, migrations. A plugin hooks in by registering "
    "additional services in this container or replacing existing ones."))
S += flow_diagram([
    ("DbContextOptions", "Provider extension (UseNpgsql) and plugin extensions (UseAcmeTenancy)"),
    ("ServiceProviderCache", "Finds a matching internal provider by hash, or builds one"),
    ("ApplyServices()", "Each extension registers its services in the new provider"),
    ("Internal service provider", "Cached and shared by all matching contexts"),
    ("Model (cached)", "Built once per provider: conventions, type mappings, validation"),
    ("Query, SaveChanges, Migrations", "Run with the services of this provider"),
])
S.append(p(
    "Two caches are decisive here. The <b>service provider</b> is cached based on the "
    "options: contexts with the same configuration share one provider. The <b>model</b> is "
    "cached per provider (and context type). Both make EF Core fast, but they have direct "
    "consequences for plugins, which chapter 4 covers in detail."))

S += h2("Lifetimes")
S.append(p(
    "Every EF service has a fixed lifetime. <b>Singleton</b> here means: once per internal "
    "service provider, not per application. <b>Scoped</b> means: once per context instance. "
    "A singleton must not hold a scoped service, and a service that needs different data "
    "per context must not be a singleton. Appendix A lists the lifetimes of all extension "
    "points."))

S += h2("The dependencies pattern")
S.append(p(
    "EF services do not receive their dependencies individually, but as a parameter "
    "object, such as " + c("MigrationsSqlGeneratorDependencies") + " or " +
    c("ProviderConventionSetBuilderDependencies") + ". When you derive from an EF service, "
    "you accept this object in the constructor and pass it on. EF Core adds new "
    "dependencies to the object without breaking constructors. For plugins this is a "
    "blessing: derived services stay compatible across minor versions."))
S += code_block('''
public class TenancyMigrationsSqlGenerator : NpgsqlMigrationsSqlGenerator
{
    public TenancyMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,     // parameter object from EF
        INpgsqlSingletonOptions npgsqlSingletonOptions)      // additionally from the provider
        : base(dependencies, npgsqlSingletonOptions)
    {
    }
''', label="Migrations/Internal/TenancyMigrationsSqlGenerator.cs (header)")
S += callout("note", [
    "All classes in the sample use classic constructors with " + c("private readonly") +
    " fields and " + c("ArgumentNullException.ThrowIfNull") + ", no primary constructors. "
    "Their parameters are mutable throughout the class body, offer no natural place for "
    "validation and hide which state is stored. Classic constructors also match the style "
    "of EF Core and Npgsql, whose classes a plugin derives from. The only exception is "
    "positional records such as " + c("RlsPolicy") + ", where the syntax generates real " +
    c("init") + " properties.",
], title="Code style in the sample")

S += h2("Extend or replace?")
S.append(p(
    "There are two fundamentally different ways to extend EF Core. <b>Plugin interfaces</b> "
    "such as " + c("IMethodCallTranslatorPlugin") + " or " + c("IConventionSetPlugin") +
    " are designed for exactly this. They can be registered multiple times, EF queries all "
    "registrations, and several plugins coexist. <b>Replacing</b> means swapping a single "
    "service such as " + c("IMigrationsSqlGenerator") + " for a derived class. That is "
    "sometimes unavoidable, but only one replacement can win per context."))
S += callout("important", [
    "Always check first whether a plugin interface exists. Replacing a single service "
    "collides with every other plugin that replaces the same service and forces you to "
    "derive from the provider's implementation. That ties the plugin to one provider and "
    "its internal API. Chapter 5 shows how to stay composable anyway.",
], title="Plugin interfaces before replacement")

S += h2("Warm-up: a complete plugin in five pieces")
S.append(p(
    "Before the large sample, here is the smallest plugin that is still complete. It makes "
    "PostgreSQL's " + c("reverse()") + " available in LINQ as " +
    c("EF.Functions.Reverse(...)") + ". Every later chapter elaborates one of its five "
    "pieces:"))
S += code_block('''
// 1. The function users call in LINQ; it is never executed
public static class ReverseDbFunctionsExtensions
{
    public static string Reverse(this DbFunctions _, string value)
        => throw new InvalidOperationException("Reverse can only be used in LINQ queries.");
}

// 2. The translator turns the method call into a SQL function (chapter 12)
public class ReverseTranslator : IMethodCallTranslator
{
    // Constructor stores the ISqlExpressionFactory in _sql

    public SqlExpression? Translate(
        SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        => method == ReverseMethod
            ? _sql.Function("reverse", [arguments[1]], nullable: true,
                argumentsPropagateNullability: [true], typeof(string),
                arguments[1].TypeMapping)
            : null;
}

// 3. The plugin hands the translator to EF (chapter 12)
public class ReverseTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public ReverseTranslatorPlugin(ISqlExpressionFactory sql)
    {
        Translators = [new ReverseTranslator(sql)];
    }

    public IEnumerable<IMethodCallTranslator> Translators { get; }
}
''', label="tests/Acme.EntityFrameworkCore.Tenancy.Tests/MinimalPluginTests.cs (1/2, shortened)")
S += code_block('''
// 4. The options extension registers the plugin in EF's internal provider (chapters 4, 5)
public class ReverseOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
        => new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IMethodCallTranslatorPlugin, ReverseTranslatorPlugin>();

    public void Validate(IDbContextOptions options)
    {
    }

    // ExtensionInfo: not a provider, no settings -> hash 0, always the same provider
}

// 5. The Use method activates the plugin (chapter 6)
public static DbContextOptionsBuilder UseReverse(this DbContextOptionsBuilder builder)
{
    ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
        builder.Options.FindExtension<ReverseOptionsExtension>()
        ?? new ReverseOptionsExtension());
    return builder;
}
''', label="tests/Acme.EntityFrameworkCore.Tenancy.Tests/MinimalPluginTests.cs (2/2, shortened)")
S += code_block('''
options.UseNpgsql(connectionString).UseReverse();

db.Customers.Where(c => EF.Functions.Reverse(c.Name) == "abc");
// SQL: WHERE reverse(c."Name") = 'abc'
''', label="Usage")
S.append(p(
    "That is all a working plugin needs. EF finds the translator because the extension "
    "registers it in the internal service provider, and every context configured with " +
    c("UseReverse()") + " shares that provider. Because the argument references a column, "
    "the function needs no evaluatable expression filter (chapter 13 explains when it "
    "does). The test " + c("MinimalPluginTests") + " checks the translation and that the "
    "same query fails without the plugin. The tenancy plugin consists of exactly these "
    "pieces, only many more of them."))

# ------------------------------------------------------------ 3
S += h1("The sample scenario")
S.append(p(
    "The sample plugin <b>Acme.EntityFrameworkCore.Tenancy</b> makes EF Core multi-tenant "
    "with PostgreSQL. The database itself enforces tenant separation through <b>Row Level "
    "Security</b> (RLS): every table gets a policy that only shows rows of the tenant whose "
    "ID is set in the current database session. Even a faulty query or raw SQL cannot read "
    "another tenant's data."))
S.append(p("From the user's point of view it looks like this:"))
S += code_block('''
public class Order : ITenantOwned               // marker from the core package
{
    public int Id { get; set; }
    public TenantId TenantId { get; set; }      // strongly typed, automatically uuid
    public decimal Total { get; set; }
}

services.AddAcmeTenancy<HttpTenantProvider>();
services.AddDbContext<ShopContext>((sp, o) => o
    .UseNpgsql(connectionString)
    .UseAcmeTenancy(sp, tenancy => tenancy.ApplyQueryFilter()));

// Querying and saving without any tenant logic in application code
db.Orders.Add(new Order { Total = 42 });         // the plugin sets TenantId
var mine = await db.Orders.ToListAsync();        // RLS filters in the database
''', label="User's view")
S.append(p("To make this work, the plugin uses every extension point covered in this book:"))
S += data_table(
    ["Requirement", "Implementation", "Ch."],
    [
        ["Configuration via " + c("UseAcmeTenancy()"),
         "Options extension, settings record, options builder", "4–6"],
        [c("TenantId") + " as a column type without configuration",
         "Type mapping plugin, value converter, JSON reader/writer", "10"],
        [c("EF.Functions.CurrentTenant()") + " in LINQ",
         "Method translator, evaluatable expression filter", "12–13"],
        [c("tenantId.IsEmpty") + " in LINQ", "Member translator", "12"],
        ["RLS for all " + c("ITenantOwned") + " entities",
         "Convention with index and optional query filter", "8"],
        ["Fluent API, attribute " + c("[NoTenantIsolation]"),
         "Annotations, attribute convention, ConfigurationSource", "7–8"],
        ["Detect misconfiguration early", "Validation convention, custom warnings", "9, 16"],
        ["Set TenantId automatically", "SaveChanges interceptor", "15"],
        ["Tenant in the DB session", "Connection interceptor", "15"],
        [c("ENABLE ROW LEVEL SECURITY") + ", " + c("CREATE POLICY"),
         "Annotation provider, migrations SQL generator", "17–18"],
        ["Roles and grants in migrations", "Custom migration operations", "19"],
        ["Readable snapshots", "Annotation code generator, design-time services", "20"],
        ["Compiled models and AOT", c("Default") + " instances, source generators", "21"],
        [c("EF.Functions.IsNotDistinctFrom") + ", " + c("EF.Functions.Every"),
         "Custom SQL node, aggregate translator", "37–38"],
        ["Tenant property with any name", c("IsTenantProperty()") + ", runtime annotation", "39"],
        ["Scaffold RLS from an existing database", "Custom database model factory", "40"],
        ["SQL Server", "Second provider package on the same core", "41–42"],
    ], [1.6, 2, 0.4])

S += h2("Package architecture")
S.append(p(
    "The plugin consists of three NuGet packages. The <b>core package</b> contains "
    "everything that is independent of the database and that assemblies with entity "
    "classes may reference without pulling in a provider. The <b>PostgreSQL package</b> is "
    "the complete implementation this book uses to explain everything. The <b>SQL Server "
    "package</b> uses the same core and shows in chapter 42 what a second provider has to "
    "implement itself."))
S += code_block('''
Acme.EntityFrameworkCore.Tenancy               Core (EF Core relational, no provider)
├─ TenantId, TenantIdConverter, TenantIdJsonReaderWriter, TenancyException
├─ ITenantOwned, ITenantProvider, NoTenantIsolationAttribute
├─ EF.Functions.CurrentTenant(), TenancyDbFunctionsAttribute
├─ Metadata/         runtime annotation, IsTenantProperty(), candidate lookup
├─ Conventions/      value generation for TenantId keys
├─ Query/            evaluatable expression filter (for all packages)
├─ Interceptors/     SaveChanges interceptor, abstract session interceptor
├─ Diagnostics/      event IDs, logging definitions, logger extensions
└─ DependencyInjection/  AddAcmeTenancy<T>()

Acme.EntityFrameworkCore.Tenancy.PostgreSQL    complete implementation
├─ Infrastructure/   options extension, builder, singleton options
├─ Storage/          TenantIdTypeMapping, type mapping plugin
├─ Query/            translators, custom SQL node, aggregate, SQL generator
├─ Metadata/         RLS annotations, RlsPolicy, annotation provider
├─ Builders/         Fluent API (HasRowLevelSecurity, HasPolicy, ...)
├─ Conventions/      isolation, attribute, validation, convention set plugin
├─ Migrations/       SQL generator, custom operations
├─ Design/           code generators, design-time services
├─ Scaffolding/      read RLS and policies from the database
└─ Runtime/          TenantConnectionInterceptor

Acme.EntityFrameworkCore.Tenancy.SqlServer     second provider (chapter 42)
└─ Infrastructure/, Storage/, Query/, Metadata/, Conventions/, Migrations/, Runtime/
''', lang="text", label="Structure of the three packages")
S += callout("tip", [
    "The folder structure follows EF Core's own (Infrastructure, Storage, Query, Metadata, "
    "Migrations, Design). Anyone who knows EF Core finds their way immediately, and when "
    "looking things up in the EF source you know where the counterpart lives.",
])
