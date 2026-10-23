# ============================================================ APPENDICES
S += part(
    "Appendices",
    "For reference: extension points with lifetimes, files of the sample, terms and a "
    "debugging toolbox.",
    ["Appendix A: Extension points and lifetimes",
     "Appendix B: Files of the sample plugin",
     "Appendix C: Glossary",
     "Appendix D: Debugging toolbox",
     "License"])

S += h1("Appendix A: Extension points and lifetimes", numbered=False)
S.append(p(
    "Lifetimes and cardinality according to EF Core 10's service tables (" +
    c("EntityFrameworkServicesBuilder") + ", " + c("EntityFrameworkRelationalServicesBuilder") +
    "). “Multiple” means: plugin interface, all registrations are used."))
S += data_table(
    ["Service", "Lifetime", "Kind", "Ch."],
    [
        [c("IConventionSetPlugin"), "Scoped", "multiple", "8"],
        [c("ISingletonOptions"), "Singleton", "multiple", "5"],
        [c("ITypeMappingSourcePlugin"), "Singleton", "multiple", "10"],
        [c("IRelationalTypeMappingSourcePlugin"), "Singleton", "multiple", "10"],
        [c("IMethodCallTranslatorPlugin"), "Scoped", "multiple", "12"],
        [c("IMemberTranslatorPlugin"), "Scoped", "multiple", "12"],
        [c("IAggregateMethodCallTranslatorPlugin"), "Scoped", "multiple", "38"],
        [c("IEvaluatableExpressionFilterPlugin"), "Singleton", "multiple", "13"],
        [c("IInterceptor") + " (injected)", "Scoped", "multiple", "15"],
        [c("ISingletonInterceptor"), "Singleton", "multiple", "15"],
        [c("IRelationalAnnotationProvider"), "Singleton", "single", "18"],
        [c("IMigrationsSqlGenerator"), "Scoped", "single", "18"],
        [c("IMigrationsModelDiffer"), "Scoped", "single", "19"],
        [c("IModelValidator"), "Singleton", "single", "9"],
        [c("IModelCustomizer"), "Singleton", "single", "–"],
        [c("IModelCacheKeyFactory"), "Singleton", "single", "31"],
        [c("ISqlGenerationHelper"), "Singleton", "single", "–"],
        [c("IRelationalTypeMappingSource"), "Singleton", "single", "10"],
        [c("ISqlExpressionFactory"), "Scoped", "single", "12"],
        [c("IQuerySqlGeneratorFactory"), "Singleton", "single", "14, 37"],
        [c("IModificationCommandFactory"), "Singleton", "single", "29"],
        [c("ICommandBatchPreparer"), "Scoped", "single", "29"],
        [c("IModificationCommandBatchFactory"), "Scoped", "single", "29"],
        [c("IUpdateSqlGenerator"), "Singleton", "single", "29"],
        [c("IRelationalParameterBasedSqlProcessorFactory"), "Scoped", "single", "37"],
    ], [2.4, 0.9, 0.8, 0.4])
S += h2("Design-time services (via IDesignTimeServices)")
S += data_table(
    ["Service", "Purpose"],
    [
        [c("IAnnotationCodeGenerator"), "Annotations as Fluent API in snapshot and scaffolding"],
        [c("ICSharpRuntimeAnnotationCodeGenerator"), "Filter annotations in the compiled "
         "model or translate complex values (chapter 39)"],
        [c("ICSharpMigrationOperationGenerator"), "Custom operations as C# in migrations"],
        [c("IDatabaseModelFactory"), "Read the schema for scaffolding (chapter 40)"],
        [c("IProviderConfigurationCodeGenerator"), "UseXyz call in the scaffolded context"],
    ], [2, 2.6])

S += h1("Appendix B: Files of the sample plugin", numbered=False)
S += data_table(
    ["File", "Content", "Ch."],
    [
        ["<b>Core package</b>", "", ""],
        [c("TenantId.cs") + ", " + c("TenantIdConverter.cs"), "Strongly typed ID, converter", "10"],
        [c("TenantIdJsonReaderWriter.cs"), "JSON support", "10"],
        [c("ITenantOwned.cs") + ", " + c("ITenantProvider.cs"), "Marker, tenant source", "3"],
        [c("NoTenantIsolationAttribute.cs"), "Data annotation", "8"],
        [c("TenancyDbFunctionsExtensions.cs"), c("EF.Functions.CurrentTenant()"), "12"],
        [c("Diagnostics/*"), "Event IDs, definitions, logger extensions", "16"],
        [c("Interceptors/TenantSaveChangesInterceptor.cs"), "SaveChanges logic", "15"],
        [c("Interceptors/TenantSessionInterceptor.cs"), "Abstract base for session commands",
         "15, 41"],
        [c("TenancyDatabaseFacadeExtensions.cs"), c("Database.ApplyTenantSession()"), "15"],
        [c("Metadata/*") + " (core)", "Runtime annotation, IsTenantProperty, candidate lookup", "39"],
        [c("Query/TenancyEvaluatableExpressionFilterPlugin.cs"), "Filter for all packages", "13, 41"],
        [c("DependencyInjection/*"), "AddAcmeTenancy", "6, 41"],
        [c("ValueGeneration/TenantIdValueGenerator.cs") + ", " + c("…Factory.cs"), "Value generation", "28"],
        [c("Conventions/TenantIdKeyValueGenerationConvention.cs"), "Generator for keys", "28"],
        ["<b>PostgreSQL package</b>", "", ""],
        [c("Infrastructure/TenancyOptionsExtension.cs"), "Extension, info, ApplyServices", "4, 5"],
        [c("Infrastructure/TenancySettings.cs"), "Settings as a record", "4"],
        [c("Infrastructure/ITenancySingletonOptions.cs"), "Singleton options", "5"],
        [c("Infrastructure/TenancyOptionsBuilder.cs"), "Options builder", "6"],
        [c("Infrastructure/Tenancy*Extensions.cs"), "UseAcmeTenancy", "6"],
        [c("Infrastructure/TenancyIdentifiers.cs"), "Identifier validation", "8, 19"],
        [c("Storage/*"), "Type mapping and plugin", "10"],
        [c("Query/*"), "Translators, functions", "12, 13"],
        [c("Metadata/*"), "Annotations, RlsPolicy, PolicyCommand, annotation provider", "7, 18"],
        [c("Builders/*"), "Fluent API, PolicyBuilder", "7"],
        [c("Conventions/*"), "Isolation, attribute, validation, plugin", "8, 9"],
        [c("Migrations/*"), "SQL generator, custom operations", "18, 19"],
        [c("Design/*") + ", " + c("Properties/AssemblyInfo.cs"), "Code generator, design time", "20"],
        [c("Runtime/TenantConnectionInterceptor.cs"), "Set the session setting", "15"],
        [c("Query/Expressions/*") + ", " + c("Query/DistinctFromTranslator.cs"),
         "Custom SQL node", "37"],
        [c("Query/Internal/*"), "SQL generator, nullability processor, factories", "37"],
        [c("Query/EveryAggregateTranslator.cs"), "Aggregate translator", "38"],
        [c("Design/Internal/TenancyCSharpRuntime…Generator.cs"), "Compiled model filter", "39"],
        [c("Scaffolding/*"), "Read RLS from the database", "40"],
        ["<b>SQL Server package</b>", "", ""],
        [c("Infrastructure/*"), "Extension, provider-builder activation", "42"],
        [c("Storage/*") + ", " + c("Query/*"), "uniqueidentifier, SESSION_CONTEXT", "42"],
        [c("Conventions/*") + ", " + c("Metadata/*"), "Isolation, column annotation", "42"],
        [c("Migrations/*"), "Predicate function, security policy", "42"],
        [c("Runtime/*"), "sp_set_session_context", "42"],
        ["<b>Further</b>", "", ""],
        [c("samples/ShopSample"), "Console sample with design-time factory", "3, 32"],
        [c("samples/ShopApi"), "ASP.NET Core sample (minimal API)", "35"],
        [c(".github/workflows/ci.yml"), "Build, tests, handbook, preview build", "36"],
        [c("PublicAPI.*.txt"), "Public API analyzer", "36"],
        [c("docs/handbook/*"), "Sources of this book (German and English)", "–"],
        [c("tests/...Tests"), "Unit tests without a database", "23"],
        [c("tests/.../MinimalPluginTests.cs"), "The mini plugin", "2"],
        [c("tests/.../CompositionTests.cs"), "Decorator example (audit plugin)", "5"],
        [c("tests/.../DebuggingTests.cs"), "Debugging techniques", "App. D"],
        [c("tests/.../SchemaPerTenantTests.cs"), "Model cache key per schema", "31"],
        [c("benchmarks/..."), "BenchmarkDotNet project for the SaveChanges logic", "34"],
        [c("tests/...IntegrationTests"), "Testcontainers tests against PostgreSQL", "15, 23, 27, 33, 40"],
        [c("tests/...SqlServer.Tests"), "Unit and Testcontainers tests for SQL Server", "42"],
    ], [2.3, 2, 0.5])

S += h1("Appendix C: Glossary", numbered=False)
S += data_table(
    ["Term", "Explanation"],
    [
        ["Annotation", "Key-value metadata on a model element."],
        ["ConfigurationSource", "Origin of a piece of metadata (Explicit, DataAnnotation, "
         "Convention); determines who may override whom."],
        ["Convention", "Rule that adapts the model automatically while it is built."],
        ["Decorator", "Service that wraps the previously registered implementation and "
         "delegates to it; composes with other plugins (chapter 5)."],
        ["Design-time model", "Complete model with all annotations; basis for migrations. "
         "Counterpart: the lean runtime model."],
        ["Differ", c("IMigrationsModelDiffer") + "; compares two relational models."],
        ["Funcletizer", "First query stage; evaluates expressions without data reference in .NET."],
        ["Nullability processor", "Query stage that evaluates NULL semantics and optimizes "
         "comparisons; must know custom SQL nodes."],
        ["SqlExpression", "Node in the SQL expression tree; custom nodes for syntax that is "
         "not a function."],
        ["Runtime annotation", "Annotation read at run time, which must therefore survive in "
         "the runtime and compiled model."],
        ["Internal service provider", "EF's own DI container per configuration."],
        ["Pubternal", "Publicly visible but internal API without compatibility guarantees (EF1001)."],
        ["Relational model", "Tables, columns, indexes, derived from the EF model."],
        ["RLS", "Row Level Security: row filters enforced by the database itself."],
        ["Snapshot", "Generated C# code with the model state of the last migration."],
        ["Type mapping", "Connection between CLR type and database type including "
         "conversion, comparison and literals."],
    ], [1.3, 3])

S += h1("Appendix D: Debugging toolbox", numbered=False)
S.append(p(
    "When a plugin does not behave as expected, the question is almost always one of five. "
    "Each technique below is checked in " + c("DebuggingTests") + "."))
S += data_table(
    ["Question", "Technique"],
    [
        ["Is my annotation in the model?", c("ToDebugString(MetadataDebugStringOptions"
         ".LongDefault)") + " on the design-time model"],
        ["Which implementation of a single service won?",
         c("context.GetService<IMigrationsSqlGenerator>().GetType()")],
        ["Is my convention registered?", c("GetService<IConventionSetBuilder>()"
         ".CreateConventionSet()") + ", then e.g. " + c("ModelFinalizingConventions")],
        ["What does the query look like before and after translation?",
         c("query.Expression.Print()") + " and " + c("query.ToQueryString()")],
        ["Does my plugin log?", c("LogTo(lines.Add, [eventId], LogLevel.Debug)") +
         ", restricted to the plugin's event IDs"],
    ], [1.4, 3.2])
S += code_block('''
using var ctx = CreateContext();

// 1. Model: annotations, results of conventions, type mappings
var model = ctx.GetService<IDesignTimeModel>().Model;
Console.WriteLine(model.ToDebugString(MetadataDebugStringOptions.LongDefault));

// 2. Services: which replacement won?
Console.WriteLine(ctx.GetService<IMigrationsSqlGenerator>().GetType());

// 3. Conventions: is mine in the list?
var conventions = ctx.GetService<IConventionSetBuilder>().CreateConventionSet();
Console.WriteLine(string.Join("\\n", conventions.ModelFinalizingConventions));

// 4. Queries: LINQ tree before, SQL after translation
var query = ctx.Orders.Where(o => o.TenantId == EF.Functions.CurrentTenant());
Console.WriteLine(query.Expression.Print());
Console.WriteLine(query.ToQueryString());
''', label="Debugging snippets (checked in tests/.../DebuggingTests.cs)")
S += h2("More tools")
S += data_table(
    ["Tool", "Use"],
    [
        ["Debugger with Source Link, “Just My Code” off", "Step into EF Core and Npgsql; "
         "both ship symbols and Source Link. A breakpoint in your own convention shows when "
         "and how often it runs."],
        [c("DebugView") + " in the debugger", "Model, entity types, query objects and the "
         "change tracker show the same texts as " + c("ToDebugString") + "."],
        [c("dotnet ef ... --verbose"), "Shows which assemblies and design-time services the "
         "tools load; the first stop when a design-time service seems to be ignored."],
        [c("EnableSensitiveDataLogging") + ", " + c("EnableDetailedErrors"), "Parameter "
         "values and detailed errors in the log; for development only."],
        [c("ManyServiceProvidersCreatedWarning"), "EF warns when too many internal "
         "providers are created: almost always a hash mistake (chapter 4)."],
        ["EF Core's source code on GitHub", "The authoritative documentation for internal "
         "APIs; the folder structure matches the namespaces."],
    ], [1.4, 3.2])
S += callout("tip", [
    "Almost every plugin problem can be reproduced without a database: build a context "
    "with the plugin and look at the model, the resolved services or " +
    c("ToQueryString()") + ". Such a test iterates faster than the application and remains "
    "as a regression test afterwards.",
], title="Reproduce in a test, not in the app")

# ------------------------------------------------------------ License
S += h1("License", numbered=False)
S.append(p(
    "This handbook and the sample code in the repository are licensed under the MIT "
    "License. You may use, copy, modify and distribute them, also commercially, provided "
    "that the following copyright and license notice is included."))
MIT_TEXT = [
    "Copyright (c) 2026 guenni81",
    "Permission is hereby granted, free of charge, to any person obtaining a copy of this "
    "software and associated documentation files (the \u201cSoftware\u201d), to deal in the "
    "Software without restriction, including without limitation the rights to use, copy, "
    "modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, "
    "and to permit persons to whom the Software is furnished to do so, subject to the "
    "following conditions:",
    "The above copyright notice and this permission notice shall be included in all copies "
    "or substantial portions of the Software.",
    "THE SOFTWARE IS PROVIDED \u201cAS IS\u201d, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR "
    "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A "
    "PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT "
    "HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF "
    "CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE "
    "OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.",
]
for paragraph in MIT_TEXT:
    S.append(p(paragraph))
S.append(p(
    "The DejaVu fonts embedded in this PDF are under their own free license (Bitstream Vera "
    "Fonts license; the DejaVu changes are in the public domain); see " + c("docs/handbook/fonts/LICENSE-DejaVu.txt") + " in "
    "the repository."))
