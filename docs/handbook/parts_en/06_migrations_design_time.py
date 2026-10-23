# ============================================================ PART VI
S += part(
    "Migrations and design time",
    "Generating schema changes, extending the dotnet ef tooling and supporting compiled "
    "models.",
    ["Chapter 17: How migrations work",
     "Chapter 18: Declarative SQL from annotations",
     "Chapter 19: Custom migration operations",
     "Chapter 20: Design-time services",
     "Chapter 21: Compiled models, trimming and NativeAOT",
     "Chapter 22: Scaffolding (reverse engineering)"])

# ------------------------------------------------------------ 17
S += h1("How migrations work")
S.append(p(
    "Migrations compare two models and produce operations and SQL from them. A plugin can "
    "hook in at several points; to choose the right one you need to know the flow:"))
S += flow_diagram([
    ("Design-time model", "Model including all design-time annotations"),
    ("IRelationalAnnotationProvider", "Transfers annotations to tables, columns and indexes "
                                       "of the relational model"),
    ("IMigrationsModelDiffer", "Compares the old (snapshot) and new relational model"),
    ("MigrationOperations", "CreateTable, AlterTable, ... with .Annotation / .OldAnnotation"),
    ("ICSharpMigrationsGenerator", "Writes migration, designer file and snapshot (dotnet ef)"),
    ("IMigrationsSqlGenerator", "Produces SQL when applying or scripting"),
])
S.append(p(
    "There are two basic strategies for plugins. <b>Declarative</b>: the plugin describes "
    "the desired state as annotations; EF detects changes by itself, and the plugin only "
    "has to produce SQL. <b>Imperative</b>: the plugin offers its own operations that users "
    "write into migrations by hand. Good plugins use both: declarative for everything that "
    "belongs to the model, imperative for everything else."))
S += callout("note", [
    c("Database.EnsureCreated()") + " uses the same pipeline: it computes the diff from the "
    "empty state to the design-time model and executes the SQL of the migrations "
    "generator. A plugin's custom migration SQL therefore takes effect there too, which "
    "simplifies integration tests considerably (chapter 23).",
])

# ------------------------------------------------------------ 18
S += h1("Declarative SQL from annotations")
S += h2("The annotation provider")
S.append(p(
    "The differ compares the <i>relational</i> model (tables, columns), not the entity "
    "types. Which annotations a table carries is decided by the " +
    c("IRelationalAnnotationProvider") + ". There is one per context, and it belongs to the "
    "provider; the plugin therefore derives from the Npgsql implementation:"))
S += code_block('''
public class TenancyAnnotationProvider : NpgsqlAnnotationProvider
{
    public TenancyAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
        : base(dependencies)
    {
    }

    public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
    {
        foreach (var annotation in base.For(table, designTime))
            yield return annotation;

        if (!designTime)          // migrations only: the runtime model stays lean
            yield break;

        var entityType = (IEntityType)table.EntityTypeMappings.First().TypeBase;
        foreach (var annotation in entityType.GetAnnotations())
        {
            if (!annotation.Name.StartsWith(RlsAnnotationNames.Prefix,
                                            StringComparison.Ordinal))
                continue;
            // Do not pass on the opt-out marker, otherwise empty migrations appear
            if (annotation.Name == RlsAnnotationNames.Enabled && annotation.Value is false)
                continue;
            yield return annotation;
        }
    }
}
''', label="Metadata/Internal/TenancyAnnotationProvider.cs")
S.append(p(
    "From here EF takes over: when a policy changes, the differ produces an " +
    c("AlterTableOperation") + " with new and old annotations. In the generated migration "
    "it looks like this, without any custom code generator:"))
S += code_block('''
migrationBuilder.AlterTable(name: "Orders")
    .Annotation("Acme:Rls:Policy:tenant_isolation", "{...new...}")
    .OldAnnotation("Acme:Rls:Policy:tenant_isolation", "{...old...}");
''', label="Generated migration (excerpt)")

S += h2("The SQL generator")
S.append(p(
    "There is also only one " + c("IMigrationsSqlGenerator") + ". The plugin derives from "
    "the Npgsql generator and extends " + c("CreateTable") + " and " + c("AlterTable") + ":"))
S += code_block('''
protected override void Generate(
    CreateTableOperation operation, IModel? model,
    MigrationCommandListBuilder builder, bool terminate = true)
{
    // Unterminated SQL cannot take further statements (as with Npgsql comments)
    if (!terminate && operation.HasRlsConfiguration())
        throw new ArgumentException("RLS cannot be emitted in an unterminated CREATE TABLE.");

    base.Generate(operation, model, builder, terminate);
    if (terminate)
        GenerateRowLevelSecurity(operation.Name, operation.Schema, previous: null,
            operation, builder);
}

protected override void Generate(
    AlterTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
{
    base.Generate(operation, model, builder);
    GenerateRowLevelSecurity(operation.Name, operation.Schema, operation.OldTable,
        operation, builder);
}
''', label="Migrations/Internal/TenancyMigrationsSqlGenerator.cs (excerpt)")
S += code_block('''
protected virtual void GenerateRowLevelSecurity(string tableName, string? schema,
    IReadOnlyAnnotatable? previous, IReadOnlyAnnotatable current,
    MigrationCommandListBuilder builder)
{
    var table = Delimit(tableName, schema);
    var oldPolicies = previous?.GetRlsPolicies() ?? new Dictionary<string, string>();
    var newPolicies = current.GetRlsPolicies();

    // 1. Drop removed or changed policies
    foreach (var (name, json) in oldPolicies)
        if (!newPolicies.TryGetValue(name, out var n) || n != json)
            Statement(builder, $"DROP POLICY {Delimit(name)} ON {table}");

    // 2. RLS state (ENABLE/DISABLE, FORCE/NO FORCE)
    if ((previous?.IsRlsEnabled() ?? false) != current.IsRlsEnabled())
        Statement(builder, $"ALTER TABLE {table} "
            + (current.IsRlsEnabled() ? "ENABLE" : "DISABLE") + " ROW LEVEL SECURITY");
    // ... FORCE analogous

    // 3. Create new or changed policies
    foreach (var (name, json) in newPolicies)
        if (!oldPolicies.TryGetValue(name, out var o) || o != json)
            Statement(builder, CreatePolicySql(table, RlsPolicy.Deserialize(json)));
}

protected virtual void Statement(MigrationCommandListBuilder builder, string sql)
{
    builder.Append(sql).AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
    EndStatement(builder);
}
''', label="Migrations/Internal/TenancyMigrationsSqlGenerator.cs (shortened)")
S += data_table(
    ["Detail", "Reason"],
    [
        ["DROP + CREATE instead of " + c("ALTER POLICY"), c("ALTER POLICY") + " can change "
         "neither " + c("FOR") + " nor PERMISSIVE/RESTRICTIVE. DDL is transactional in "
         "PostgreSQL; no visible gap occurs."],
        [c("EndStatement(builder)"), "Ends the command like the base class does, honoring "
         "transaction suppression."],
        ["Own " + c("Delimit") + " helpers", "Npgsql's " + c("DelimitIdentifier") +
         " helpers are private. Always escape identifiers via " +
         c("Dependencies.SqlGenerationHelper") + ", never by string concatenation."],
        ["DropTable, RenameTable", "Nothing to do: PostgreSQL removes or moves policies "
         "together with the table."],
    ], [1.3, 3])

# ------------------------------------------------------------ 19
S += h1("Custom migration operations")
S.append(p(
    "Some things do not belong in the model but in migrations: roles, grants, extensions, "
    "data backfills. For these the plugin offers its own operations. They consist of "
    "three parts: the operation class, an extension method on " + c("MigrationBuilder") +
    " and the SQL generation in the generator."))
S += code_block('''
public class CreateApplicationRoleOperation : MigrationOperation
{
    public required string Name { get; init; }
    public bool CanLogin { get; init; }       // passwords do NOT belong in migrations
}
''', label="Migrations/Operations/TenancyOperations.cs (excerpt)")
S += code_block('''
namespace Microsoft.EntityFrameworkCore.Migrations;      // always imported in migrations

public static class TenancyMigrationBuilderExtensions
{
    public static OperationBuilder<CreateApplicationRoleOperation> CreateApplicationRole(
        this MigrationBuilder migrationBuilder, string name, bool canLogin = false)
    {
        var operation = new CreateApplicationRoleOperation
        {
            Name = TenancyIdentifiers.ValidateIdentifier(name, "role name"),
            CanLogin = canLogin,
        };
        migrationBuilder.Operations.Add(operation);
        return new OperationBuilder<CreateApplicationRoleOperation>(operation);
    }
}
''', label="Migrations/Operations/TenancyMigrationBuilderExtensions.cs (excerpt)")
S.append(p(
    "The generator dispatches operations through the central " +
    c("Generate(MigrationOperation, ...)") + " method. Unknown types go to the base class:"))
S += code_block('''
protected override void Generate(
    MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
{
    switch (operation)
    {
        case CreateApplicationRoleOperation createRole: Generate(createRole, builder); break;
        case DropApplicationRoleOperation dropRole:     Generate(dropRole, builder); break;
        case GrantTablePrivilegesOperation grant:       Generate(grant, builder); break;
        default: base.Generate(operation, model, builder); break;
    }
}

protected virtual void Generate(CreateApplicationRoleOperation operation,
    MigrationCommandListBuilder builder)
{
    var literal = Literal(operation.Name);           // 'shop_app'  (for the comparison)
    var identifier = Delimit(operation.Name);    // shop_app (CREATE ROLE, "..." if needed)
    var login = operation.CanLogin ? "LOGIN" : "NOLOGIN";

    builder
        .AppendLine("DO $$")
        .AppendLine("BEGIN")
        .AppendLine("    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles")
        .AppendLine($"                   WHERE rolname = {literal}) THEN")
        .AppendLine($"        CREATE ROLE {identifier} {login} NOSUPERUSER NOBYPASSRLS;")
        .AppendLine("    END IF;")
        .AppendLine("END")
        .Append("$$");
    builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
    EndStatement(builder);
}
''', label="Migrations/Internal/TenancyMigrationsSqlGenerator.cs (excerpt)")
S += callout("important", [
    "The role name appears twice, but in two forms: as a <b>string literal</b> in the "
    "comparison (" + c("'shop_app'") + ", produced by the string type mapping) and as an "
    "<b>identifier</b> in " + c("CREATE ROLE") + " (" + c("shop_app") + ", in double quotes if it needs them). Mixing them "
    "up or building them by string concatenation produces invalid SQL or an injection "
    "hole. The extension method additionally validates the name.",
], title="Literal or identifier")
S += callout("tip", [
    "Write operations idempotently (" + c("IF NOT EXISTS") + ", " + c("DROP ... IF EXISTS") +
    "). Roles exist cluster-wide and outlive databases; a migration that fails on its "
    "second run against a new database is a common annoyance.",
])
S.append(p("In a migration, the operations are used like EF's own:"))
S += code_block('''
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateApplicationRole("shop_support");  // BEFORE the tables (ch. 32)
    // ... operations generated by EF (tables, RLS via annotations) ...
    migrationBuilder.CreateApplicationRole("shop_app", canLogin: true);
    migrationBuilder.GrantTablePrivileges("shop_app");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropApplicationRole("shop_app");
    // ...
}
''', label="Migrations/20261021_Initial.cs (hand-edited)")
S += callout("note", [
    "If the <i>differ</i> should produce custom operations automatically, you need a "
    "derived " + c("IMigrationsModelDiffer") + " and an " +
    c("ICSharpMigrationOperationGenerator") + " that writes the operation as C#. Both are "
    "single services with a lot of internal API. The declarative route via annotations "
    "(chapter 18) is almost always the better choice.",
], title="When the differ should produce custom operations")

# ------------------------------------------------------------ 20
S += h1("Design-time services")
S.append(p(
    "The " + c("dotnet ef") + " tools (adding migrations, writing snapshots, scaffolding, "
    "compiled models) use their own service provider with design-time services. A plugin "
    "extends it through " + c("IDesignTimeServices") + "."))
S += h2("Loaded automatically")
S.append(p(
    "So that users do not need to configure anything, an assembly attribute points to the "
    "class. The tooling finds it in every assembly the startup project references. The "
    "second argument restricts loading to contexts with the matching provider:"))
S += code_block('''
[assembly: DesignTimeServicesReference(
    "Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Design.TenancyDesignTimeServices, "
        + "Acme.EntityFrameworkCore.Tenancy.PostgreSQL",
    "Npgsql.EntityFrameworkCore.PostgreSQL")]
''', label="Properties/AssemblyInfo.cs")
S += code_block('''
public class TenancyDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        // Remove all registrations (EF default, Npgsql), then add ours:
        // independent of the order in which the tooling loads services
        services.RemoveAll<IAnnotationCodeGenerator>();
        services.AddSingleton<IAnnotationCodeGenerator, TenancyAnnotationCodeGenerator>();
    }
}
''', label="Design/TenancyDesignTimeServices.cs")

S += h2("Readable snapshots: the annotation code generator")
S.append(p(
    "By default EF writes unknown annotations as " + c("HasAnnotation(...)") + " into the "
    "snapshot, for policies with JSON values. That works, but is hardly readable. A custom " +
    c("IAnnotationCodeGenerator") + " translates them into the Fluent API:"))
S += code_block('''
// Before
b.HasAnnotation("Acme:Rls:Enabled", true);
b.HasAnnotation("Acme:Rls:Policy:tenant_isolation", "{\\"Name\\":\\"tenant_isolation\\",...}");

// After
RlsEntityTypeBuilderExtensions.HasRowLevelSecurity(b, true);
RlsEntityTypeBuilderExtensions.HasPolicy(b, "tenant_isolation", p => p
    .Using("\\"TenantId\\" = nullif(current_setting('app.tenant_id', true), '')::uuid")
    .WithCheck("\\"TenantId\\" = nullif(current_setting('app.tenant_id', true), '')::uuid"));
''', label="ShopContextModelSnapshot.cs")
S.append(p(
    "EF Core 10 writes extension methods in the snapshot as static calls (" +
    c("Extensions.Method(b, ...)") + ") instead of " + c("b.Method(...)") + ". This avoids "
    "ambiguities when several providers or plugins define methods with the same name. Tests "
    "that check the generated code should expect this form."))
S += code_block('''
public override IReadOnlyList<MethodCallCodeFragment> GenerateFluentApiCalls(
    IEntityType entityType, IDictionary<string, IAnnotation> annotations)
{
    var calls = new List<MethodCallCodeFragment>();

    // CONTRACT: REMOVE translated annotations from the dictionary
    if (annotations.TryGetValue(RlsAnnotationNames.Enabled, out var enabled)
        && enabled.Value is true)
    {
        annotations.Remove(RlsAnnotationNames.Enabled);
        var forced = annotations.Remove(RlsAnnotationNames.Forced, out var f)
                     && f.Value is true;
        calls.Add(new MethodCallCodeFragment(HasRowLevelSecurityMethod, forced));
    }

    foreach (var key in annotations.Keys
                 .Where(k => k.StartsWith(RlsAnnotationNames.PolicyPrefix,
                                          StringComparison.Ordinal))
                 .ToList())                         // copy: the dictionary changes
    {
        var policy = RlsPolicy.Deserialize((string)annotations[key].Value!);
        annotations.Remove(key);
        calls.Add(new MethodCallCodeFragment(HasPolicyMethod, policy.Name,
            new NestedClosureCodeFragment("p", BuildPolicyChain(policy))));
    }

    calls.AddRange(base.GenerateFluentApiCalls(entityType, annotations));
    return calls;
}
''', label="Design/Internal/TenancyAnnotationCodeGenerator.cs (excerpt)")
S += data_table(
    ["Pitfall", "Solution"],
    [
        ["Translated annotation stays in the dictionary", "Appears twice in the snapshot. "
         "Always remove it; whatever remains, EF writes as " + c("HasAnnotation") + "."],
        ["Generic method in the " + c("MethodInfo"), "The snapshot uses the non-generic " +
         c("EntityTypeBuilder") + ". Pick the non-generic overload via " +
         c("GetRuntimeMethod") + " with exact parameter types."],
        [c("params") + " argument as an array", c("MethodCallCodeFragment") + " takes " +
         c("params object?[]") + ". A " + c("string[]") + " would become the argument list "
         "through array covariance: wrap it in " + c("new object?[] { roles }") + "."],
        ["Enums in the snapshot", "Rendered as " + c("PolicyCommand.Select") + ". The enum "
         "therefore lives in the always-imported namespace " +
         c("Microsoft.EntityFrameworkCore") + "."],
        ["Lambda arguments", c("NestedClosureCodeFragment(\"p\", chain)") + " produces " +
         c("p => p.Using(...)") + "; chains are built with " + c("fragment.Chain(...)") + "."],
    ], [1.4, 2.6])
S.append(p(
    "The generator only affects the snapshot and designer files. The actual migrations "
    "keep " + c(".Annotation(...)") + " on " + c("AlterTable") + "; that is the intended "
    "mechanism for changes."))

# ------------------------------------------------------------ 21
S += h1("Compiled models, trimming and NativeAOT")
S.append(p(
    c("dotnet ef dbcontext optimize") + " produces a <b>compiled model</b>: C# code that "
    "builds the runtime model directly instead of computing it at startup through "
    "conventions. This shortens startup for large models considerably and is a "
    "prerequisite for NativeAOT. A plugin must be prepared for it in four places:"))
S += data_table(
    ["Building block", "Requirement", "In the sample"],
    [
        ["Type mappings", "Public type with a public static property " + c("Default") +
         " (used for NativeAOT); converters/comparers as shared instances",
         c("TenantIdTypeMapping.Default") + ", " + c("TenantIdConverter.Instance")],
        ["JSON reader/writer", "Static " + c("Instance") + " and " +
         c("ConstructorExpression"), c("TenantIdJsonReaderWriter")],
        ["Value generators", "Configured with " + c("HasValueGeneratorFactory(Type)") +
         "; factory with a public parameterless constructor",
         c("TenantIdValueGeneratorFactory") + " (chapter 28)"],
        ["Annotations", "Simple values are taken over by EF automatically; an " +
         c("ICSharpRuntimeAnnotationCodeGenerator") + " filters design-time annotations and "
         "translates complex values", "Filters RLS policies, keeps the runtime annotation "
         "(chapter 39)"],
    ], [1.1, 2.2, 1.7])
S.append(p(
    "The third point is often misunderstood. EF takes annotations with simple values "
    "(strings, numbers, bool, enums) into the compiled model <b>automatically</b>; the "
    "source of " + c("CSharpRuntimeAnnotationCodeGenerator") + " shows this, writing them "
    "via " + c("GenerateSimpleAnnotations") + ". A custom generator is therefore not needed "
    "to <i>keep</i> annotations, but to <i>filter out</i> pure design-time annotations that "
    "nobody reads at run time, and to translate annotations with complex values into code. "
    "Chapter 39 shows both in the sample."))
S += h2("Trimming and AOT")
S.append(p(
    "Both packages set " + c("<IsAotCompatible>true</IsAotCompatible>") + ". The compiler "
    "then reports code that uses reflection without annotations. The JSON serialization of "
    "the policies uses a source generator instead of reflection:"))
S += code_block('''
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(RlsPolicy))]
internal partial class RlsJsonContext : JsonSerializerContext;
''', label="Metadata/RlsPolicy.cs (excerpt)")
S += callout("note", [
    "Conventions, snapshots and migrations only run at design time or during model "
    "building; with a compiled model and precompiled queries they disappear at run time. "
    "For AOT, the critical parts are therefore type mappings, converters, translators (run "
    "at build time) and interceptors.",
])

# ------------------------------------------------------------ 22
S += h1("Scaffolding (reverse engineering)")
S.append(p(
    c("dotnet ef dbcontext scaffold") + " reads an existing database schema and generates "
    "entities and a context. A plugin that manages its own schema objects can recognize "
    "them during this too. The route goes through two design-time services:"))
S += data_table(
    ["Service", "Task"],
    [
        [c("IDatabaseModelFactory"), "Reads the schema into a " + c("DatabaseModel") +
         ". A derived Npgsql factory can query " + c("pg_policies") + " and " +
         c("pg_class.relrowsecurity") + " and attach them as annotations to " +
         c("DatabaseTable") + "."],
        [c("IAnnotationCodeGenerator"), "Writes these annotations as Fluent API into the "
         "generated " + c("OnModelCreating") + ", with the same logic as for the snapshot."],
    ], [1.3, 3])
S.append(p(
    "The sample implements both: a custom database model factory reads RLS state and "
    "policies from the database, and the annotation code generator from chapter 20 writes "
    "them into the scaffolded context. Chapter 40 shows the implementation in detail."))
