# ============================================================ PART VIII
S += part(
    "Advanced topics",
    "Limits of the run-time hooks, further extension points, operating in production and "
    "the way from the plugin into the application.",
    ["Chapter 27: Bulk operations and the limits of interceptors",
     "Chapter 28: Value generation",
     "Chapter 29: The update pipeline",
     "Chapter 30: Complex types and JSON",
     "Chapter 31: Models per runtime context",
     "Chapter 32: Advanced migrations",
     "Chapter 33: Row Level Security in production",
     "Chapter 34: Plugin performance",
     "Chapter 35: Using the plugin in ASP.NET Core",
     "Chapter 36: CI and API stability"])

# ------------------------------------------------------------ 27
S += h1("Bulk operations and the limits of interceptors")
S.append(p(
    "A SaveChanges interceptor only sees what goes through " + c("SaveChanges") + ". " +
    c("ExecuteUpdate") + " and " + c("ExecuteDelete") + " write directly to the database, "
    "without change tracker and without SaveChanges. The same applies to raw SQL. A plugin "
    "that enforces its guarantees only through SaveChanges has a gap here:"))
S += data_table(
    ["Path", "SaveChanges interceptor", "Connection interceptor", "RLS"],
    [
        [c("SaveChanges"), "yes", "yes", "yes"],
        [c("ExecuteUpdate") + ", " + c("ExecuteDelete"), "<b>no</b>", "yes", "yes"],
        [c("FromSql") + ", " + c("SqlQuery") + ", " + c("ExecuteSql"), "<b>no</b>", "yes", "yes"],
        ["ADO.NET or psql as the app role", "<b>no</b>", "<b>no</b>",
         "yes (without tenant: no rows)"],
    ], [1.6, 1.1, 1.1, 1.2])
S.append(p(
    "In the sample, RLS catches every one of these paths because the database enforces the "
    "rules itself. An integration test proves it by deliberately bypassing the interceptor:"))
S += code_block('''
[Fact]
public async Task ExecuteUpdate_bypasses_interceptor_but_not_rls()
{
    // ... create an order for "tenant" ...
    await using var attacker = ContextFor(tenant);

    // No SaveChanges -> the SaveChanges interceptor does NOT run.
    // The policy's WITH CHECK still prevents the reassignment.
    var ex = await Assert.ThrowsAsync<PostgresException>(() =>
        attacker.Orders.ExecuteUpdateAsync(
            s => s.SetProperty(o => o.TenantId, TenantId.New())));

    Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
}
''', label="tests/...IntegrationTests/AdvancedTests.cs (excerpt)")
S += callout("important", [
    "Plugins without database-level protection (soft delete, audit fields, tenant filtering "
    "via query filter only) must document that bulk operations and raw SQL bypass their "
    "rules. To prevent it, an " + c("IQueryExpressionInterceptor") + " can be used: " +
    c("ExecuteUpdate") + " and " + c("ExecuteDelete") + " go through the query pipeline, "
    "the interceptor sees their expression tree and can, for example, reject a " +
    c("SetProperty") + " on protected columns. Raw SQL remains open regardless.",
], title="What a plugin must document")

# ------------------------------------------------------------ 28
S += h1("Value generation")
S.append(p(
    "EF generates key values either in the database (identity, sequences) or client-side "
    "via a " + c("ValueGenerator") + ". For " + c("Guid") + " keys EF does this by itself, "
    "for custom types like " + c("TenantId") + " it does not. A plugin closes this gap with "
    "a generator, a factory and a convention:"))
S += code_block('''
public class TenantIdValueGenerator : ValueGenerator<TenantId>
{
    // Final values, no temporary placeholders until saving
    public override bool GeneratesTemporaryValues => false;

    public override TenantId Next(EntityEntry entry) => TenantId.New();   // UUIDv7
}
''', label="ValueGeneration/TenantIdValueGenerator.cs (core package)")
S += code_block('''
public class TenantIdValueGeneratorFactory : ValueGeneratorFactory
{
    // Stateless generator: EF may cache it and share it between contexts
    public override ValueGenerator Create(IProperty property, ITypeBase typeBase)
        => new TenantIdValueGenerator();
}
''', label="ValueGeneration/TenantIdValueGeneratorFactory.cs (core package)")
S += code_block('''
public virtual void ProcessModelFinalizing(
    IConventionModelBuilder modelBuilder,
    IConventionContext<IConventionModelBuilder> context)
{
    foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
    {
        if (entityType.BaseType is not null
            || entityType.FindPrimaryKey() is not { Properties: [var property] }
            || property.ClrType != typeof(TenantId))
            continue;

        // Convention source: an explicit ValueGeneratedNever() wins (-> null)
        if (property.Builder.ValueGenerated(ValueGenerated.OnAdd) is null)
            continue;

        property.Builder.HasValueGeneratorFactory(typeof(TenantIdValueGeneratorFactory));
    }
}
''', label="Conventions/TenantIdKeyValueGenerationConvention.cs (core package)")
S += callout("important", [
    "A value generator can be configured in several ways: " + c("HasValueGenerator") +
    " with a type or a factory function, or " + c("HasValueGeneratorFactory") + " with a "
    "factory type. EF Core 10's compiled model generator only accepts the last one and "
    "otherwise aborts with “The property ... has a value generator configured. Use "
    "'HasValueGeneratorFactory' to configure the value generator factory type.” Plugins "
    "should therefore use " + c("HasValueGeneratorFactory(Type)") + " and give the factory "
    "a public parameterless constructor.",
], title="A factory type, not a generator")
S.append(p(
    "The convention lives in the core package because it has nothing to do with "
    "PostgreSQL; the provider package plugs it in via its convention set plugin. The "
    "compiled model test checks that the factory appears in the generated code."))

# ------------------------------------------------------------ 29
S += h1("The update pipeline")
S.append(p(
    "Chapter 11 describes the way from LINQ to SQL. Writing has its own pipeline, which "
    "plugins extend for bulk operations, database-side soft deletes or returned values:"))
S += flow_diagram([
    ("ChangeTracker", "Determines added, modified and deleted entities"),
    ("IModificationCommandFactory", "One ModificationCommand per row and table"),
    ("ICommandBatchPreparer", "Sorts by dependencies (foreign keys) and forms batches"),
    ("IModificationCommandBatchFactory", "Creates the batches (size, provider specifics)"),
    ("IUpdateSqlGenerator", "Writes INSERT, UPDATE, DELETE, possibly with RETURNING"),
    ("Execution", "Results (identity, concurrency tokens) back into the entities"),
])
S += data_table(
    ["Service", "Lifetime", "Kind"],
    [
        [c("IModificationCommandFactory"), "Singleton", "single"],
        [c("ICommandBatchPreparer"), "Scoped", "single"],
        [c("IModificationCommandBatchFactory"), "Scoped", "single"],
        [c("IUpdateSqlGenerator"), "Singleton", "single"],
    ], [2.4, 1, 1])
S.append(p(
    "All of these are single provider services, with the consequences from chapter 5. "
    "Before replacing them, it pays to check the alternatives: a SaveChanges interceptor "
    "can rewrite states (delete becomes modify for soft delete), a command interceptor can "
    "add SQL, and for bulk changes there is " + c("ExecuteUpdate") + ". A custom " +
    c("IUpdateSqlGenerator") + " is only justified when the SQL itself needs a different "
    "shape, for example " + c("INSERT ... ON CONFLICT") + " for upserts."))

# ------------------------------------------------------------ 30
S += h1("Complex types and JSON")
S += h2("Complex types")
S.append(p(
    "Complex types (since EF Core 8) are value types without their own identity whose "
    "properties end up in the owner's table. For plugins this means: conventions iterating " +
    c("entityType.GetProperties()") + " do <b>not</b> see their properties. They hang off " +
    c("IComplexProperty") + " and must be iterated separately:"))
S += code_block('''
foreach (var complexProperty in entityType.GetComplexProperties())
{
    foreach (var property in complexProperty.ComplexType.GetProperties())
    {
        // Column names are prefixed (e.g. "Address_City"); always determine them via
        // property.GetColumnName(table), never assemble them yourself
    }
}
''', label="Pattern for conventions (sketch)")
S.append(p(
    "Fluent API methods for properties should additionally be offered on " +
    c("ComplexTypePropertyBuilder") + " where they make sense there. The sample plugin does "
    "not need this: isolation and value generation only concern entities."))

S += h2("JSON columns")
S.append(p(
    "Owned types and (since EF Core 10) complex types can be stored as a single JSON column "
    "with " + c("ToJson()") + ". Values in it are not read and written through converters "
    "but through the type mapping's " + c("JsonValueReaderWriter") + " (chapter 10). "
    "Without it, model building fails. The sample's unit and integration tests check both "
    "sides:"))
S += code_block('''
using var ctx = Contexts.Create(model: m =>
    m.Entity<Shipment>().OwnsOne(s => s.Details, d => d.ToJson()));

var property = ctx.DesignModel()
    .FindEntityType(typeof(ShipmentDetails))!
    .FindProperty(nameof(ShipmentDetails.ApprovedBy))!;

Assert.Same(TenantIdJsonReaderWriter.Instance,
    property.GetTypeMapping().JsonValueReaderWriter);
''', label="tests/.../JsonTests.cs")
S += callout("note", [
    "PostgreSQL arrays (" + c("List<TenantId>") + " as " + c("uuid[]") + ") are not JSON: "
    "Npgsql maps primitive collections natively as arrays. Whether a provider supports "
    "collections with converted element types is provider-specific and deserves its own "
    "test before you document it.",
])

# ------------------------------------------------------------ 31
S += h1("Models per runtime context")
S.append(p(
    "EF builds the model once per context type and caches it (chapter 2). If the model "
    "should depend on run-time data, such as one schema per tenant, the cache key must "
    "contain that data. That is what " + c("IModelCacheKeyFactory") + " is for:"))
S += code_block('''
public sealed class SchemaModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => context is ISchemaScoped scoped
            ? (context.GetType(), scoped.Schema, designTime)
            : (object)(context.GetType(), designTime);
}

public sealed class SchemaContext : DbContext, ISchemaScoped
{
    // Constructor: (DbContextOptions<SchemaContext> options, string schema)
    public string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.HasDefaultSchema(Schema);
}

// Registration
options.UseNpgsql(connectionString)
       .ReplaceService<IModelCacheKeyFactory, SchemaModelCacheKeyFactory>();
''', label="tests/.../SchemaPerTenantTests.cs (shortened)")
S.append(p(
    "The test " + c("SchemaPerTenantTests") + " checks both sides of the cache key: two "
    "schemas produce two models with their own default schema, and the same schema reuses "
    "the cached model instead of building it again."))
S.append(p(
    "This is the basis for “schema per tenant”, an alternative to RLS. The three usual "
    "approaches compared:"))
S += data_table(
    ["Approach", "Strengths", "Weaknesses"],
    [
        ["Shared tables + RLS (sample)", "One model, one schema, simple migrations; the "
         "database enforces separation", "All tenants share tables and indexes; "
         "misconfigured roles undermine separation"],
        ["Schema per tenant", "Strong separation, separate backups per schema possible",
         "One model per tenant in the cache (memory!), migrations for every schema"],
        ["Database per tenant", "Maximum separation, independent scaling",
         "One model suffices, but connections, migrations and operations per database"],
    ], [1.3, 1.6, 1.8])
S += callout("important", [
    "Every key value produces its own model in memory. With hundreds of tenants and large "
    "models this becomes expensive. In addition, values that change the model must never "
    "come from unchecked input.",
], title="Model cache and memory")

# ------------------------------------------------------------ 32
S += h1("Advanced migrations")
S += h2("Order of operations")
S.append(p(
    "Generated and hand-written operations run in the order they appear in " + c("Up()") +
    ". This is a real source of errors. In the sample, the policy " + c("support_read") +
    " applies " + c("TO shop_support") +
    ". EF generates it directly after " + c("CREATE TABLE") + ". If the role is only "
    "created at the end of " + c("Up()") + ", the migration fails."))
S += code_block('''
protected override void Up(MigrationBuilder migrationBuilder)
{
    // 1. Whatever policies reference: BEFORE the generated operations
    migrationBuilder.CreateApplicationRole("shop_support");

    // 2. Generated by EF: tables, RLS, policies (TO shop_support)
    migrationBuilder.CreateTable(name: "Customers", /* ... */);

    // 3. Whatever requires the tables: AFTER the generated operations
    migrationBuilder.CreateApplicationRole("shop_app", canLogin: true);
    migrationBuilder.GrantTablePrivileges("shop_app");     // applies to existing tables
}
''', label="Migrations/20261021_Initial.cs")

S += h2("Idempotent scripts")
S.append(p(
    c("dotnet ef migrations script --idempotent") + " produces a script that can run "
    "against any database state. For this, Npgsql wraps every operation in a " +
    c("DO $EF$ ... $EF$") + " block that checks the history table. Custom operations that "
    "produce a " + c("DO $$") + " block themselves end up inside it. This works because the "
    "dollar quotes use different tags. Assumptions like this belong in a test:"))
S += code_block('''
var script = db.GetService<IMigrator>()
    .GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

await db.Database.ExecuteSqlRawAsync(script);   // nested DO valid?
await db.Database.ExecuteSqlRawAsync(script);   // second run is skipped
''', label="tests/...IntegrationTests/AdvancedTests.cs (excerpt)")

S += h2("Operations outside of transactions")
S.append(p(
    "Some commands must not run inside a transaction, in PostgreSQL for example " +
    c("CREATE INDEX CONCURRENTLY") + ". Custom operations with such SQL end their command "
    "with " + c("EndStatement(builder, suppressTransaction: true)") + ". EF then executes "
    "it outside the migration transaction. The price: if the migration fails afterwards, "
    "this command is not rolled back; it should therefore be idempotent."))

S += h2("Migration bundles")
S.append(p(
    c("dotnet ef migrations bundle") + " produces an executable that applies migrations "
    "without the SDK. It contains the startup project including the plugin assemblies; "
    "custom operations and the plugin's SQL generator therefore take effect there too. It "
    "is important that the design-time factory or host uses the same plugin options as the "
    "application, otherwise SQL for a different model is produced."))

# ------------------------------------------------------------ 33
S += h1("Row Level Security in production")
S.append(p(
    "RLS is only as strong as its operating conditions. These pitfalls silently render the "
    "policies ineffective or unusable:"))
S += data_table(
    ["Pitfall", "Consequence", "Remedy"],
    [
        ["App runs as table owner", "Policies are ignored",
         c("FORCE ROW LEVEL SECURITY") + " (plugin default) and separate roles"],
        ["Superuser or " + c("BYPASSRLS"), "RLS is always bypassed, even with FORCE",
         "App role with " + c("NOSUPERUSER NOBYPASSRLS") + " (custom operation, ch. 19)"],
        ["Setting not set", "Without " + c("nullif") + ": error; with it: empty results",
         "The interceptor sets the value on every open"],
        ["No index on the tenant column", "Every query scans the whole table",
         "Index convention (chapter 8)"],
        ["Transaction pooling (PgBouncer)", "Session settings reach other clients or get "
         "lost", "Set the setting transaction-locally (see below)"],
        ["Connection opened outside EF", "The session keeps an old tenant or none",
         "Let EF open connections, or " + c("ApplyTenantSession()") + " (chapter 15)"],
        ["“Bypass” switch for the app role", "Anyone who can run SQL as the app role can "
         "flip it", "Separate roles for cross-tenant access (see below)"],
    ], [1.3, 1.3, 1.6])
S += h2("Transaction pooling")
S.append(p(
    "PgBouncer in “transaction” mode shares a server connection between clients per "
    "transaction. A session value set with " + c("set_config(..., false)") + " can then "
    "reach another client, a security hole. The safe variant: set the setting "
    "<i>transaction-locally</i> with " + c("set_config(..., true)") + " and run all database "
    "work in an explicit transaction. Technically, the interceptor moves from " +
    c("IDbConnectionInterceptor") + " to " + c("IDbTransactionInterceptor") + " (" +
    c("TransactionStarted") + "). Because queries without a transaction then have no tenant "
    "at all, the application must adhere to this consistently. The sample stays with the "
    "simpler session model and documents that it requires session or Npgsql pooling."))
S += h2("Policy performance")
S.append(p(
    "PostgreSQL inserts policies into every query like additional " + c("WHERE") + " "
    "conditions. " + c("current_setting") + " is " + c("STABLE") + " and evaluated once per "
    "query; together with the index the overhead is small. Expensive policies come from "
    "subqueries (for example permissions from another table) or functions that are not " +
    c("LEAKPROOF") + " and prevent optimizations. You can check this with " +
    c("EXPLAIN (ANALYZE)") + " as the app role, not as owner, because the owner may not "
    "even see the policies."))

S += h2("Legitimate cross-tenant access")
S.append(p(
    "Sooner or later someone needs data across tenants: reports for the operator, "
    "maintenance jobs, support. The tempting shortcut is a switch in the application, for "
    "example an additional session setting " + c("app.bypass") + " that the policy honors. "
    "That is not a security boundary: whatever the application role can set, an injected "
    "SQL statement can set too. Separation must come from database roles instead:"))
S += data_table(
    ["Need", "Solution", "Why it is safe"],
    [
        ["Reports, analytics", "Separate read-only role with " + c("BYPASSRLS") + ", only "
         + c("SELECT") + ", its own connection string and a context configuration without "
         "tenant", "The app role cannot become this role; its credentials live elsewhere"],
        ["Support reads selected data", "Additional permissive policy for a support role: " +
         c("HasPolicy(...)") + " with " + c("For(Select)") + " and " +
         c("To(\"shop_support\")"),
         "Restricted to SELECT and to members of the role"],
        ["Maintenance per tenant", "Background job that iterates the tenants and sets each "
         "one (chapter 35)", "Runs through the normal policies"],
    ], [1.2, 2, 1.6])
S += code_block('''
-- Created by an administrator: BYPASSRLS requires a superuser
CREATE ROLE shop_reporting LOGIN PASSWORD '...' NOSUPERUSER BYPASSRLS;
GRANT USAGE ON SCHEMA public TO shop_reporting;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO shop_reporting;
''', lang="sql", label="Reporting role (tests/...IntegrationTests/CrossTenantAccessTests.cs)")
S.append(p(
    "The application role, in contrast, cannot switch the policies off. " +
    c("SET row_security = off") + " does not disable RLS for a role without " +
    c("BYPASSRLS") + "; PostgreSQL instead rejects every query the policies would affect. " +
    c("CrossTenantAccessTests") + " checks all three: the reporting role sees all tenants, "
    "it cannot write, and the app role cannot help itself."))
S += callout("important", [
    "Every connection of the reporting role is an access to all tenants. Log it (" +
    c("log_connections") + ", pgaudit) and grant the role to as few people and services "
    "as possible. The plugin's " + c("CreateApplicationRole") + " deliberately always "
    "creates roles with " + c("NOBYPASSRLS") + ": bypass roles do not belong in the "
    "application's migrations.",
], title="Audit cross-tenant access")

# ------------------------------------------------------------ 34
S += h1("Plugin performance")
S.append(p(
    "Plugin code runs at three frequencies. Knowing which code runs how often tells you "
    "where optimization pays off:"))
S += data_table(
    ["Frequency", "Code", "Consequence"],
    [
        ["Once per provider", "Conventions, type mapping plugins, singleton options",
         "Readability over micro-optimization"],
        ["Once per query shape", "Translators, evaluatable filter, query interceptors",
         "Moderate; the result is cached"],
        ["On every execution", "Interceptors for commands, connections, SaveChanges, "
         "materialization", "Hot path: no reflection, no unnecessary allocations"],
    ], [1.2, 2, 1.6])
S.append(p(
    "This leads to a few rules the sample follows: " + c("MethodInfo") + " and " +
    c("PropertyInfo") + " lookups live in static fields. The SaveChanges interceptor only "
    "collects entries when there are tenant-relevant changes. The connection interceptor "
    "costs one round trip per open, which is documented. Such assumptions should be "
    "measured, not guessed. The repository contains a BenchmarkDotNet project for the "
    "SaveChanges logic; it runs without a database:"))
S += code_block('''
[MemoryDiagnoser]
public class SaveChangesInterceptorBenchmark
{
    [Params(1, 100)]
    public int Entities { get; set; }

    // GlobalSetup: context with UseAcmeTenancy() (no connection) and the interceptor

    [Benchmark(Baseline = true)]
    public int TrackOnly()                  // what EF does anyway before saving
    {
        AddOrders();
        var count = _db.ChangeTracker.Entries().Count();
        _db.ChangeTracker.Clear();
        return count;
    }

    [Benchmark]
    public int TrackAndApplyTenant()        // the same plus the plugin's SaveChanges logic
    {
        AddOrders();
        _interceptor.ApplyTenant(_db);
        var count = _db.ChangeTracker.Entries().Count();
        _db.ChangeTracker.Clear();
        return count;
    }
}
''', label="benchmarks/.../SaveChangesInterceptorBenchmark.cs (shortened)")
S += code_block('''
dotnet run -c Release --project benchmarks/Acme.EntityFrameworkCore.Tenancy.Benchmarks
''', lang="text", label="Running the benchmark")
S += data_table(
    ["Method", "Entities", "Mean", "Ratio", "Allocated"],
    [
        [c("TrackOnly"), "1", "1.5 µs", "1.00", "1.8 KB"],
        [c("TrackAndApplyTenant"), "1", "2.3 µs", "1.54", "2.7 KB"],
        [c("TrackOnly"), "100", "123 µs", "1.00", "134 KB"],
        [c("TrackAndApplyTenant"), "100", "170 µs", "1.38", "202 KB"],
    ], [2, 1, 1, 1, 1])
S.append(p(
    "Example results (short job, .NET 10, author's machine; absolute values vary, the "
    "ratios are more meaningful). The plugin adds about half a microsecond per saved "
    "entity, roughly 40 to 50 percent on top of change tracking alone. That sounds like "
    "much, but a single round trip to the database costs hundreds of microseconds; the "
    "connection interceptor's extra round trip per open (chapter 15) therefore weighs far "
    "more than the SaveChanges logic. That is the kind of insight a benchmark provides: it "
    "shows where optimizing is worthwhile and where it is not."))
S += callout("tip", [
    "For the startup of large models, the compiled model (chapter 21) is the most effective "
    "lever. A plugin's conventions then no longer run at run time at all; that is why it "
    "pays to test compiled model support rather than merely claim it.",
])

# ------------------------------------------------------------ 35
S += h1("Using the plugin in ASP.NET Core")
S.append(p(
    "The sample " + c("samples/ShopApi") + " shows the way from the plugin into a web API. "
    "The tenant comes from the claim " + c("tenant_id") + " of the signed-in user:"))
S += code_block('''
public sealed class HttpTenantProvider : ITenantProvider
{
    public const string ClaimType = "tenant_id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        _httpContextAccessor = httpContextAccessor;
    }

    // Determined anew on every access: also works as a singleton (pooling)
    public TenantId? CurrentTenantId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimType)?.Value;
            return Guid.TryParse(value, out var id) ? new TenantId(id) : null;
        }
    }
}
''', label="samples/ShopApi/HttpTenantProvider.cs")
S += code_block('''
builder.Services.AddHttpContextAccessor();
builder.Services.AddAcmeTenancy<HttpTenantProvider>();
builder.Services.AddDbContext<ShopApiContext>((sp, options) => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Shop"))
    .UseAcmeTenancy(sp));

// No tenant filter in code: RLS in the database provides the separation
app.MapGet("/orders", async (ShopApiContext db) =>
    await db.Orders.OrderBy(o => o.Id).ToListAsync());
''', label="samples/ShopApi/Program.cs (excerpt)")
S += callout("important", [
    "The tenant must come from a trustworthy source: a claim from a signed token. A header "
    "or query parameter that the client sets freely undermines the entire isolation. The "
    "sample contains a header simulation for the development environment only and labels "
    "it explicitly.",
], title="Where the tenant comes from")
S.append(p(
    "For " + c("AddDbContextPool") + ", register the provider as a singleton. This is "
    "possible because " + c("HttpTenantProvider") + " determines the tenant anew on every "
    "access; the interceptors ask it on every connection and every save:"))
S += code_block('''
builder.Services.AddAcmeTenancy<HttpTenantProvider>(ServiceLifetime.Singleton);
builder.Services.AddDbContextPool<ShopApiContext>((sp, options) => options
    .UseNpgsql(connectionString)
    .UseAcmeTenancy(sp));   // sp: root provider; interceptors are created once
''', label="Variant with pooling")

S += h2("DbContext factories")
S.append(p(
    c("AddDbContextFactory") + " is the usual choice in Blazor Server and for parallel "
    "work. Unlike " + c("AddDbContext") + ", it registers the factory and the options as "
    "<b>singletons</b> by default. The options callback then receives the root provider, "
    "not the scope of the current request. For a scoped tenant provider that holds state "
    "per scope, this has consequences:"))
S += data_table(
    ["Registration", "Behavior"],
    [
        ["Singleton factory, scope validation on (ASP.NET Core in Development)",
         "Exception: “Cannot resolve scoped service 'ITenantProvider' from root provider.”"],
        ["Singleton factory, scope validation off (production default)", "No error. The "
         "interceptors get an instance from the root container that no request ever fills: "
         "no tenant, saving throws, queries see nothing"],
        ["Factory registered as " + c("Scoped"), "Every scope gets its own factory with its "
         "own tenant provider: correct"],
    ], [2, 2.6])
S += code_block('''
builder.Services.AddDbContextFactory<ShopApiContext>(
    (sp, options) => options.UseNpgsql(connectionString).UseAcmeTenancy(sp),
    ServiceLifetime.Scoped);         // factory and options per scope
''', label="Factory with the tenant of the request")
S.append(p(
    "A provider that determines the tenant anew on every access, such as " +
    c("HttpTenantProvider") + ", works in every variant; the problem affects providers "
    "with state per scope. " + c("AddPooledDbContextFactory") + " always behaves like "
    "pooling: the options exist once, so the tenant provider must be such a singleton. " +
    c("DbContextFactoryTests") + " shows all three variants."))

S += h2("Outside of HTTP requests")
S.append(p(
    "Background services, message handlers and scheduled jobs have no request and no "
    "claim. The tenant then comes from the job itself, for example from the message or "
    "from a list of tenants to process. The sample's " + c("AmbientTenantProvider") +
    " stores it in an " + c("AsyncLocal") + ", which flows with the asynchronous control "
    "flow:"))
S += code_block('''
public sealed class AmbientTenantProvider : ITenantProvider
{
    private static readonly AsyncLocal<TenantId?> Current = new();

    public TenantId? CurrentTenantId => Current.Value;

    public static IDisposable Use(TenantId tenant)
    {
        var previous = Current.Value;
        Current.Value = tenant;
        return new Restore(previous);     // sets the previous tenant back on Dispose
    }

    // private sealed class Restore : IDisposable { ... Current.Value = _previous; }
}
''', label="samples/ShopSample/AmbientTenantProvider.cs (shortened)")
S += code_block('''
// Tenant first, then scope and context: the interceptors are created inside the scope
using (AmbientTenantProvider.Use(tenant))
{
    await using var scope = scopeFactory.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ShopContext>();
    // ... work for this tenant ...
}
''', label="Job pattern (tests/.../BackgroundTenantTests.cs)")
S.append(p(
    "Because the provider reads the " + c("AsyncLocal") + " on every access, it can be "
    "registered as a singleton. Parallel jobs keep their own tenant, and after the " +
    c("using") + " block no tenant is left behind; " + c("BackgroundTenantTests") +
    " checks both with 20 parallel jobs. An application with web requests <i>and</i> jobs "
    "combines both sources in one provider: the claim first, otherwise the ambient tenant."))
S += callout("important", [
    "Without a tenant, a job must fail or see nothing, never everything. The plugin "
    "behaves this way: the session value stays empty, the policies match no rows and "
    "saving throws. Jobs that really need all tenants use the reporting role from chapter "
    "33.",
], title="Never fall back to “all tenants”")

# ------------------------------------------------------------ 36
S += h1("CI and API stability")
S += h2("The workflow")
S.append(p(
    "The GitHub Actions workflow in the repository implements the recommendations from "
    "chapters 23 and 24. One job builds and tests without and with a database; a "
    "second builds the handbook; a third builds against the next EF preview and may fail:"))
S += code_block('''
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { global-json-file: global.json }
      - run: dotnet build -c Release
      - run: dotnet test tests/Acme.EntityFrameworkCore.Tenancy.Tests -c Release --no-build
      - run: dotnet test tests/Acme.EntityFrameworkCore.Tenancy.IntegrationTests
          -c Release --no-build
      - run: dotnet test tests/Acme.EntityFrameworkCore.Tenancy.SqlServer.Tests
          -c Release --no-build

  handbook:                            # both PDFs; fails on overlong code lines
    runs-on: ubuntu-latest
    steps:
      # ... checkout, setup-python, pip install -r docs/handbook/requirements.txt ...
      - run: python docs/handbook/build.py --lang en

  ef-preview:
    runs-on: ubuntu-latest
    continue-on-error: true              # early warning system, not a blocker
    steps:
      # ... setup-dotnet with 11.0.x preview ...
      - run: dotnet build -c Release -p:EFCorePreview=true
''', lang="text", label=".github/workflows/ci.yml (shortened)")
S.append(p(
    "The preview build works through conditional package versions in " +
    c("Directory.Packages.props") + ":"))
S += code_block('''
<PropertyGroup>
  <CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>
</PropertyGroup>
<ItemGroup Condition="'$(EFCorePreview)' != 'true'">
  <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="[10.0.0,11.0.0)" />
</ItemGroup>
<ItemGroup Condition="'$(EFCorePreview)' == 'true'">
  <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="11.0.0-*" />
</ItemGroup>
''', lang="xml", label="Directory.Packages.props (excerpt)")

S += h2("Protecting the public API")
S.append(p(
    "A plugin has two public APIs: the one for users (Fluent API, options, types) and the "
    "one for extenders (virtual methods, dependencies, public services). Both break easily "
    "and unnoticed. The " + c("Microsoft.CodeAnalysis.PublicApiAnalyzers") + " package "
    "keeps the public API in two text files (" + c("PublicAPI.Shipped.txt") + ", " +
    c("PublicAPI.Unshipped.txt") + ") and reports every deviation. Every API change thus "
    "becomes visible as a diff in the pull request."))
S += callout("note", [
    "On the first build, the analyzer reports every public API as RS0016 because the files "
    "are empty. The code fix “Add all items … to the public API” fills them. At release "
    "time, the content moves from Unshipped to Shipped; from then on, removing a shipped "
    "API is a visible breaking change.",
], title="Setup")
