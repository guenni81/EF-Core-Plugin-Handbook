# ============================================================ PART IX
S += part(
    "Deep extensions",
    "Custom SQL expressions, aggregates, run-time metadata, scaffolding and a second "
    "provider: the tools for very complex plugins.",
    ["Chapter 37: Custom SQL expression nodes",
     "Chapter 38: Aggregate translators",
     "Chapter 39: Property-level Fluent API and runtime annotations",
     "Chapter 40: Scaffolding in practice",
     "Chapter 41: What belongs in the core",
     "Chapter 42: A second provider: SQL Server"])

# ------------------------------------------------------------ 37
S += h1("Custom SQL expression nodes")
S.append(p(
    "Chapter 12 builds SQL from existing building blocks: functions, constants, casts. That "
    "is enough for most plugins. A <b>custom expression node</b> (a subclass of " +
    c("SqlExpression") + ") becomes necessary when at least one of the following applies:"))
S += data_table(
    ["Reason", "Example"],
    [
        ["The syntax is not a function call", "Operators such as " + c("@@") + ", " + c("<->") +
         ", " + c("IS DISTINCT FROM") + ", keywords such as " + c("AT TIME ZONE")],
        ["The nullability semantics differ", c("IS DISTINCT FROM") + " is never NULL, even "
         "when an operand is NULL"],
        ["Other parts should recognize the node", "Optimizations that only apply to this "
         "expression"],
    ], [1.6, 2.4])
S.append(p(
    "The sample implements the null-safe comparison " +
    c("EF.Functions.IsNotDistinctFrom(a, b)") + " and " + c("IsDistinctFrom(a, b)") +
    ". Npgsql does not offer it as a function; it meets all three reasons."))

S += h2("Anatomy of a node")
S += data_table(
    ["Member", "Purpose", "Consequence if missing"],
    [
        [c("VisitChildren") + " / " + c("Update"), "Visitors can replace children; unchanged "
         "children yield the same instance", "EF throws at the first visitor"],
        [c("Equals") + " / " + c("GetHashCode"), "Structural comparison for the query cache "
         "and optimizations", "Cache misses, wrong deduplication"],
        [c("Print"), "Readable representation in DebugView and error messages", "Unclear errors"],
        [c("Quote"), "C# code for precompiled queries and NativeAOT", "No AOT support"],
        ["SQL generator", "Writes the node as SQL", "<b>Silently</b> missing SQL"],
        ["Nullability processor", "Knows the NULL semantics", "Exception during translation"],
    ], [1.3, 2.1, 1.6])
S += code_block('''
public class DistinctFromExpression : SqlExpression
{
    private static ConstructorInfo? _quotingConstructor;

    public DistinctFromExpression(SqlExpression left, SqlExpression right, bool negated,
        RelationalTypeMapping? typeMapping)
        : base(typeof(bool), typeMapping)
    {
        // ... ThrowIfNull ...
        Left = left;
        Right = right;
        IsNegated = negated;
    }

    public virtual SqlExpression Left { get; }
    public virtual SqlExpression Right { get; }
    public virtual bool IsNegated { get; }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update((SqlExpression)visitor.Visit(Left), (SqlExpression)visitor.Visit(Right));

    public virtual DistinctFromExpression Update(SqlExpression left, SqlExpression right)
        => left == Left && right == Right
            ? this
            : new DistinctFromExpression(left, right, IsNegated, TypeMapping);

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DistinctFromExpression).GetConstructor(
                [typeof(SqlExpression), typeof(SqlExpression), typeof(bool),
                 typeof(RelationalTypeMapping)])!,
            Left.Quote(), Right.Quote(), Constant(IsNegated),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Visit(Left);
        expressionPrinter.Append(IsNegated ? " IS DISTINCT FROM " : " IS NOT DISTINCT FROM ");
        expressionPrinter.Visit(Right);
    }

    public override bool Equals(object? obj)
        => obj is DistinctFromExpression other
           && (ReferenceEquals(this, other)
               || base.Equals(other) && Left.Equals(other.Left)
                  && Right.Equals(other.Right) && IsNegated == other.IsNegated);

    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), Left, Right, IsNegated);
}
''', label="Query/Expressions/DistinctFromExpression.cs (shortened)")
S.append(p(
    "The translator creates the node and aligns the type mappings of both sides: if one "
    "side has a mapping (a column, say), the other gets the same one. That way a constant "
    "becomes the matching literal:"))
S += code_block('''
var definition = method.GetGenericMethodDefinition();
if (definition != IsNotDistinctFromMethod && definition != IsDistinctFromMethod)
    return null;

var left = arguments[1];                        // arguments[0] is DbFunctions
var right = arguments[2];
var typeMapping = ExpressionExtensions.InferTypeMapping(left, right)
                  ?? _typeMappingSource.FindMapping(left.Type);

return new DistinctFromExpression(
    _sql.ApplyTypeMapping(left, typeMapping)!,
    _sql.ApplyTypeMapping(right, typeMapping)!,
    negated: definition == IsDistinctFromMethod,
    _typeMappingSource.FindMapping(typeof(bool)));
''', label="Query/DistinctFromTranslator.cs (excerpt)")

S += h2("Extending the SQL generator")
S.append(p(
    "The " + c("QuerySqlGenerator") + " writes SQL using a visitor. It hands unknown nodes "
    "to " + c("ExpressionVisitor.VisitExtension") + ", which only visits the children. "
    "Without an override there is no error, but <b>SQL without the operator</b>. The plugin "
    "derives from the Npgsql generator:"))
S += code_block('''
public class TenancyNpgsqlQuerySqlGenerator : NpgsqlQuerySqlGenerator
{
    public TenancyNpgsqlQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        bool reverseNullOrderingEnabled, Version postgresVersion)
        : base(dependencies, typeMappingSource, reverseNullOrderingEnabled, postgresVersion)
    {
    }

    protected override Expression VisitExtension(Expression extensionExpression)
        => extensionExpression switch
        {
            DistinctFromExpression e => VisitDistinctFrom(e),
            _ => base.VisitExtension(extensionExpression),
        };

    protected virtual Expression VisitDistinctFrom(DistinctFromExpression expression)
    {
        VisitOperand(expression.Left);
        Sql.Append(expression.IsNegated ? " IS DISTINCT FROM " : " IS NOT DISTINCT FROM ");
        VisitOperand(expression.Right);
        return expression;
    }
}
''', label="Query/Internal/TenancyNpgsqlQuerySqlGenerator.cs (shortened)")
S.append(p(
    "The generator is created through " + c("IQuerySqlGeneratorFactory") + ", a singleton "
    "single service. The plugin replaces it with its own factory taking the same "
    "dependencies as Npgsql's (" + c("QuerySqlGeneratorDependencies") + ", type mapping "
    "source, " + c("INpgsqlSingletonOptions") + ")."))

S += h2("Extending the nullability processor")
S.append(p(
    "After translation, EF optimizes NULL comparisons. To do so it must know for every node "
    "whether it can become NULL. Unknown nodes end up in " + c("VisitCustomSqlExpression") +
    ", whose base implementation throws an exception. The processor hangs off a chain of "
    "three classes, all of which must be replaced:"))
S += code_block('''
public class TenancyNpgsqlSqlNullabilityProcessor : NpgsqlSqlNullabilityProcessor
{
    // Constructor: (…ProcessorDependencies, …ProcessorParameters) : base(...)

    protected override SqlExpression VisitCustomSqlExpression(
        SqlExpression sqlExpression, bool allowOptimizedExpansion, out bool nullable)
    {
        if (sqlExpression is DistinctFromExpression distinctFrom)
        {
            // Visit children (they can be optimized), their nullability does not matter
            var left = Visit(distinctFrom.Left, out _)!;
            var right = Visit(distinctFrom.Right, out _)!;

            nullable = false;                   // IS [NOT] DISTINCT FROM is never NULL
            return distinctFrom.Update(left, right);
        }

        return base.VisitCustomSqlExpression(
            sqlExpression, allowOptimizedExpansion, out nullable);
    }
}

public class TenancyNpgsqlParameterBasedSqlProcessor : NpgsqlParameterBasedSqlProcessor
{
    protected override Expression ProcessSqlNullability(
        Expression selectExpression, ParametersCacheDecorator parametersDecorator)
        => new TenancyNpgsqlSqlNullabilityProcessor(Dependencies, Parameters)
            .Process(selectExpression, parametersDecorator);
}

// + TenancyNpgsqlParameterBasedSqlProcessorFactory
//   : IRelationalParameterBasedSqlProcessorFactory
''', label="Query/Internal/TenancyNpgsqlSqlNullabilityProcessor.cs (shortened)")
S += code_block('''
// 4) Deep extension: custom SQL expression node
services.Replace(ServiceDescriptor
    .Singleton<IQuerySqlGeneratorFactory, TenancyNpgsqlQuerySqlGeneratorFactory>());
services.Replace(ServiceDescriptor
    .Scoped<IRelationalParameterBasedSqlProcessorFactory,
        TenancyNpgsqlParameterBasedSqlProcessorFactory>());
''', label="Infrastructure/TenancyOptionsExtension.cs › ApplyServices (addition)")
S += callout("important", [
    "Custom nodes require replacing two single services and deriving from internal Npgsql "
    "classes. Two plugins that both introduce custom nodes can only be combined if one "
    "derives from the other. Hence: function composition first; custom nodes only when one "
    "of the three reasons applies. And the replacement belongs prominently in the "
    "documentation.",
], title="The price of custom nodes")
S += callout("note", [
    c("Quote") + " and " + c("RelationalExpressionQuotingUtilities") + " belong to the "
    "experimental API for precompiled queries. The compiler reports their use as EF9100; "
    "the sample suppresses it project-wide and documents this in " +
    c("Directory.Build.props") + ".",
], title="Experimental API")
S += code_block('''
[Fact]
public void IsDistinctFrom_in_projection_passes_nullability_processor()
{
    // Without a custom nullability processor, EF would throw here
    using var ctx = Contexts.Create();

    var sql = ctx.Customers
        .Select(c => EF.Functions.IsDistinctFrom(c.Name, (string?)null))
        .ToQueryString();

    Assert.Contains("IS DISTINCT FROM", sql);
}
''', label="tests/.../DeepQueryTests.cs (excerpt)")

# ------------------------------------------------------------ 38
S += h1("Aggregate translators")
S.append(p(
    "Aggregates such as " + c("Sum") + " or " + c("string_agg") + " combine several rows, "
    "typically after " + c("GroupBy") + ". An " + c("IAggregateMethodCallTranslator") +
    " receives, instead of a single argument, an " + c("EnumerableExpression") + " that "
    "describes <i>what</i> is being aggregated:"))
S += data_table(
    ["Property", "Meaning", "Origin in LINQ"],
    [
        [c("Selector"), "The aggregated expression", c("g.Select(o => o.Total > 0)")],
        [c("IsDistinct"), "Distinct values only", c("g.Select(...).Distinct()")],
        [c("Predicate"), "Filter before the aggregate", c("g.Where(o => o.Id > 10)")],
        [c("Orderings"), "Order (for order-dependent aggregates)", c("g.OrderBy(...)")],
    ], [1.1, 1.7, 1.9])
S.append(p(
    "The sample adds " + c("EF.Functions.Every(values)") + " for PostgreSQL's " +
    c("bool_and") + ":"))
S += code_block('''
public virtual SqlExpression? Translate(MethodInfo method, EnumerableExpression source,
    IReadOnlyList<SqlExpression> arguments,
    IDiagnosticsLogger<DbLoggerCategory.Query> logger)
{
    if (method != EveryMethod || source.Selector is not SqlExpression selector)
        return null;

    // Npgsql's factory implements DISTINCT, FILTER (WHERE ...) and ORDER BY in aggregates
    if (_sql is not NpgsqlSqlExpressionFactory npgsql)
        return null;

    var boolean = _typeMappingSource.FindMapping(typeof(bool))!;

    // bool_and returns NULL for an empty set, Enumerable.All returns true
    return _sql.Coalesce(
        npgsql.AggregateFunction("bool_and", [selector], source,
            nullable: true, argumentsPropagateNullability: [false],
            typeof(bool), boolean),
        _sql.Constant(true, boolean),
        boolean);
}
''', label="Query/EveryAggregateTranslator.cs (excerpt)")
S += code_block('''
SELECT COALESCE(bool_and(o."Total" > 0.0) FILTER (WHERE o."Id" > 10), TRUE)
FROM "Orders" AS o
GROUP BY o."TenantId"
''', lang="sql", label="Generated SQL for g.Where(o => o.Id > 10).Select(o => o.Total > 0)")
S += callout("important", [
    "An aggregate translator must match the semantics of the .NET side, not just the "
    "database syntax. Typical pitfalls: empty sets (NULL instead of 0, true or an empty "
    "string), NULL values in the input, and predicates. Npgsql itself aligns " +
    c("string.Join") + " with " + c("string_agg") + " using " + c("COALESCE") + "; the "
    "sample follows this pattern.",
], title="Semantics before syntax")
S.append(p(
    "Databases without a " + c("FILTER") + " clause (such as SQL Server) emulate predicates "
    "with " + c("CASE WHEN predicate THEN value END") + ". A provider-neutral aggregate "
    "translator must know both; that is why this one lives in the PostgreSQL package."))

# ------------------------------------------------------------ 39
S += h1("Property-level Fluent API and runtime annotations")
S += h2("Property builder")
S.append(p(
    "So far the plugin assumes that the tenant property is called " + c("TenantId") + " and "
    "that " + c("ITenantOwned") + " is implemented. With a Fluent API method on " +
    c("PropertyBuilder") + " it works without either, for example with existing models "
    "using a property " + c("MandantId") + " or an explicit interface implementation:"))
S += code_block('''
modelBuilder.Entity<Contract>().Property(c => c.Mandant).IsTenantProperty();
''', label="Usage")
S += code_block('''
public static PropertyBuilder IsTenantProperty(
    this PropertyBuilder builder, bool isTenant = true)
{
    ArgumentNullException.ThrowIfNull(builder);

    if (isTenant)
        builder.Metadata.SetAnnotation(TenancyAnnotationNames.IsTenantProperty, true);
    else
        builder.Metadata.RemoveAnnotation(TenancyAnnotationNames.IsTenantProperty);

    return builder;
}

public static PropertyBuilder<TProperty> IsTenantProperty<TProperty>(
    this PropertyBuilder<TProperty> builder, bool isTenant = true)
    => (PropertyBuilder<TProperty>)IsTenantProperty((PropertyBuilder)builder, isTenant);

public static IConventionPropertyBuilder? IsTenantProperty(
    this IConventionPropertyBuilder builder,
    bool isTenant = true,
    bool fromDataAnnotation = false)
    => builder.HasAnnotation(TenancyAnnotationNames.IsTenantProperty,
           isTenant ? true : null, fromDataAnnotation) is null
        ? null
        : builder;
''', label="Metadata/TenancyPropertyBuilderExtensions.cs (core package)")
S.append(p(
    "The method lives in the core package because it only sets metadata. The conventions "
    "of both provider packages find the tenant property through the same core function:"))
S += code_block('''
public static IReadOnlyProperty? FindTenantPropertyCandidate(
    this IReadOnlyEntityType entityType)
{
    var marked = entityType.GetProperties()
        .Where(p => p[TenancyAnnotationNames.IsTenantProperty] is true)
        .ToList();

    if (marked.Count > 1)
        throw new InvalidOperationException("... several tenant properties ...");
    if (marked.Count == 1)
        return marked[0];

    return typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType)
        ? entityType.FindProperty(nameof(ITenantOwned.TenantId))
        : null;
}
''', label="Metadata/TenancyEntityTypeExtensions.cs (core package, shortened)")
S += data_table(
    ["Builder", "Typical plugin methods", "Note"],
    [
        [c("PropertyBuilder") + " / " + c("<T>"), "Column options, roles of properties",
         "Here: " + c("IsTenantProperty")],
        [c("IndexBuilder") + " / " + c("<T>"), "Index methods, operator classes, filters",
         "Annotation on " + c("Metadata") + " (IMutableIndex)"],
        [c("KeyBuilder"), "Key options (clustering)", "As above"],
        [c("ComplexTypePropertyBuilder"), "Properties in complex types", "Only offer where "
         "the option makes sense there (chapter 30)"],
        [c("ModelBuilder"), "Model-wide options", "Annotation on the model"],
    ], [1.5, 1.8, 1.6])

S += h2("Runtime annotations")
S.append(p(
    "The SaveChanges interceptor needs to know at run time which entities are isolated and "
    "what their tenant property is called. The convention writes this into an annotation "
    "on the entity type; the interceptor reads it from the <i>runtime model</i>:"))
S += code_block('''
// Convention (design-time model)
entityType.Builder.HasAnnotation(TenancyAnnotationNames.TenantProperty, property.Name);

// Interceptor (runtime model)
public static string? GetTenantPropertyName(this IReadOnlyEntityType entityType)
    => entityType.GetRootType()[TenancyAnnotationNames.TenantProperty] as string;
''', label="Convention and core package (excerpt)")
S.append(p(
    "Two questions decide whether this works. Does the annotation reach the runtime model? "
    "Yes: " + c("RuntimeModelConvention") + " only removes EF's own design-time annotations "
    "and keeps all others. Does it reach the compiled model? Yes: EF writes simple values "
    "automatically (chapter 21). The custom " + c("ICSharpRuntimeAnnotationCodeGenerator") +
    " therefore has the opposite job: it removes the RLS annotations that only migrations "
    "need:"))
S += code_block('''
public class TenancyCSharpRuntimeAnnotationCodeGenerator
    : NpgsqlCSharpRuntimeAnnotationCodeGenerator
{
    // Constructor: (CSharpRuntimeAnnotationCodeGeneratorDependencies,
    //               RelationalCSharpRuntimeAnnotationCodeGeneratorDependencies) : base(...)

    public override void Generate(IEntityType entityType,
        CSharpRuntimeAnnotationCodeGeneratorParameters parameters)
    {
        if (!parameters.IsRuntime)
        {
            foreach (var key in parameters.Annotations.Keys
                         .Where(k => k.StartsWith(RlsAnnotationNames.Prefix,
                                                  StringComparison.Ordinal))
                         .ToList())
            {
                parameters.Annotations.Remove(key);
            }
        }

        base.Generate(entityType, parameters);
    }
}
''', label="Design/Internal/TenancyCSharpRuntimeAnnotationCodeGenerator.cs (shortened)")
S.append(p(
    "When the compiled model is generated for NativeAOT (" + c("ForNativeAot") + "), EF also "
    "writes the relational model. The annotation provider has copied the RLS annotations "
    "onto the tables, so they appear a second time there. The generator therefore applies "
    "the same filter in an override of " + c("Generate(ITable, ...)") + "."))
S += data_table(
    ["Annotation", "Design time", "Run time", "Compiled model"],
    [
        [c("Acme:Rls:*") + " (policies)", "yes (migrations)", "yes, unused", "filtered"],
        [c("Acme:Tenancy:TenantProperty"), "yes", "yes (interceptor)", "yes"],
        [c("Acme:Tenancy:IsTenantProperty"), "yes (convention)", "yes, unused", "yes (small)"],
    ], [1.7, 1.1, 1.1, 1.1])
S += callout("tip", [
    "For every annotation it should be clear who reads it and when. Annotations read only "
    "at design time do not belong in the compiled model; those read at run time must be in "
    "it and need a test against the generated model. The sample's compiled model test "
    "checks both.",
])

# ------------------------------------------------------------ 40
S += h1("Scaffolding in practice")
S.append(p(
    "When scaffolding (" + c("dotnet ef dbcontext scaffold") + "), EF reads the schema and "
    "generates entities and a context. A plugin managing its own schema objects should "
    "recognize them in the process. The route has three stations, two of which the plugin "
    "already knows:"))
S += flow_diagram([
    ("IDatabaseModelFactory", "Reads the schema into a DatabaseModel; the plugin attaches "
                              "RLS data as annotations to the tables"),
    ("Scaffolding model factory", "EF transfers table annotations to the entity types"),
    ("IAnnotationCodeGenerator", "Writes them as HasRowLevelSecurity/HasPolicy (chapter 20)"),
])
S += code_block('''
public class TenancyDatabaseModelFactory : NpgsqlDatabaseModelFactory
{
    public TenancyDatabaseModelFactory(
        IDiagnosticsLogger<DbLoggerCategory.Scaffolding> logger)
        : base(logger)
    {
    }

    public override DatabaseModel Create(
        DbConnection dbConnection, DatabaseModelFactoryOptions options)
    {
        var model = base.Create(dbConnection, options);
        // ... open the connection if needed ...
        ReadRowLevelSecurity(dbConnection, model);    // pg_class.relrowsecurity / ...force...
        ReadPolicies(dbConnection, model);            // pg_policies
        return model;
    }

    protected virtual void ReadPolicies(DbConnection connection, DatabaseModel model)
    {
        // SELECT schemaname, tablename, policyname, permissive, roles::text[], cmd,
        //        qual, with_check FROM pg_catalog.pg_policies
        // ...
        var policy = new RlsPolicy(
            Name: reader.GetString(2),
            Command: Enum.Parse<PolicyCommand>(reader.GetString(5), ignoreCase: true),
            Permissive: reader.GetString(3) == "PERMISSIVE",
            Roles: roles is ["public"] ? null : roles,    // "public" = default without TO
            Using: reader.IsDBNull(6) ? null : reader.GetString(6),
            WithCheck: reader.IsDBNull(7) ? null : reader.GetString(7));

        table[RlsAnnotationNames.PolicyPrefix + policy.Name] = policy.Serialize();
    }
}
''', label="Scaffolding/TenancyDatabaseModelFactory.cs (shortened)")
S.append(p(
    "The factory is registered like the code generator in the design-time services, with " +
    c("RemoveAll") + " and " + c("AddSingleton") + ". The data format is the same as with "
    "the Fluent API; snapshot, migrations and code generator therefore work without "
    "further changes."))
S += callout("note", [
    "PostgreSQL stores policy expressions in normalized form, for example " +
    c("((\"TenantId\" = (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid))") +
    ". Scaffolded policies contain exactly this text. That is correct, but looks different "
    "from the form generated by the convention. Teams switching to migrations after "
    "scaffolding should unify the policies once, otherwise the first migration produces a "
    "DROP/CREATE with identical content.",
], title="Normalized expressions")
S += code_block('''
[Fact]
public void Scaffolding_reads_rls_and_policies()
{
    using var ctx = ContextFor(null);
    var factory = new TenancyDatabaseModelFactory(
        ctx.GetService<IDiagnosticsLogger<DbLoggerCategory.Scaffolding>>());

    var model = factory.Create(
        _fixture.OwnerConnectionString, new DatabaseModelFactoryOptions());
    var orders = model.Tables.Single(t => t.Name == "Orders");

    Assert.Equal(true, orders[RlsAnnotationNames.Enabled]);
    Assert.Equal(true, orders[RlsAnnotationNames.Forced]);
}
''', label="tests/...IntegrationTests/AdvancedTests.cs (excerpt)")

# ------------------------------------------------------------ 41
S += h1("What belongs in the core")
S.append(p(
    "A second provider is the acid test for the package architecture. It shows which parts "
    "are truly provider-independent. The rule of thumb: <b>whatever knows no SQL syntax "
    "and no provider type belongs in the core.</b> For the sample this yields:"))
S += data_table(
    ["Building block", "Location", "Reason"],
    [
        [c("TenantId") + ", converter, JSON reader/writer", "Core", "Pure .NET types"],
        [c("ITenantOwned") + ", " + c("ITenantProvider") + ", attributes", "Core",
         "For entity assemblies without a provider dependency"],
        [c("IsTenantProperty()") + ", candidate lookup", "Core", "Metadata only"],
        ["Runtime annotation, " + c("GetTenantPropertyName"), "Core",
         "Read by the provider-neutral interceptor"],
        ["SaveChanges interceptor", "Core", "Works only with the change tracker"],
        [c("TenantSessionInterceptor") + " (abstract)", "Core",
         "Same flow, only the SQL command differs"],
        ["Evaluatable filter with attribute", "Core", "Recognizes function classes of all packages"],
        ["Value generation, logging, " + c("AddAcmeTenancy"), "Core", "No SQL involved"],
        [c("Database.ApplyTenantSession()"), "Core", "Uses the provider's interceptor"],
        ["Type mapping, translators, migrations, RLS conventions", "Provider",
         "Store types, SQL syntax, schema objects"],
        ["Custom SQL nodes, aggregates", "Provider", "Depend on syntax and provider factory"],
    ], [1.9, 0.7, 2.1])
S += callout("important", [
    "Two provider packages that both define an extension method " + c("UseAcmeTenancy") +
    " on " + c("DbContextOptionsBuilder") + " cannot be referenced at the same time: the "
    "call would be ambiguous. The PostgreSQL package uses EF's builder, the SQL Server "
    "package the provider's builder (" + c("UseSqlServer(cs, x => x.UseAcmeTenancy())") +
    "). If you plan several provider packages, use the provider builder consistently from "
    "the start, as the NetTopologySuite plugin does.",
], title="Name collisions between provider packages")

# ------------------------------------------------------------ 42
S += h1("A second provider: SQL Server")
S.append(p(
    "The package " + c("Acme.EntityFrameworkCore.Tenancy.SqlServer") + " implements the "
    "same idea with SQL Server's means. The differences show what a plugin really has to "
    "solve anew per database:"))
S += data_table(
    ["Aspect", "PostgreSQL", "SQL Server"],
    [
        ["Tenant in the session", c("set_config(name, value, false)"),
         c("sp_set_session_context")],
        [c("CurrentTenant()") + " in SQL", c("nullif(current_setting(...), '')::uuid"),
         c("CAST(SESSION_CONTEXT(N'…') AS uniqueidentifier)")],
        ["Column type", c("uuid"), c("uniqueidentifier")],
        ["RLS mechanism", c("CREATE POLICY") + " per table",
         "Predicate function + " + c("CREATE SECURITY POLICY")],
        ["Read / write", c("USING") + " / " + c("WITH CHECK"),
         c("FILTER") + " / " + c("BLOCK ... AFTER INSERT/UPDATE")],
        ["Table owner", "Only bound with " + c("FORCE"),
         "No FORCE; predicates are part of the query"],
        [c("DROP TABLE"), "Policies disappear with it", "Policy must go first (SCHEMABINDING)"],
        ["DDL in batches", "Transactional, freely combinable",
         c("CREATE FUNCTION/SCHEMA") + " alone in a batch: " + c("EXEC(N'...')")],
    ], [1.3, 1.6, 1.9])
S += h2("Activation through the provider builder")
S += code_block('''
services.AddAcmeTenancy<HttpTenantProvider>();                    // core package
services.AddDbContext<ShopContext>((sp, o) => o
    .UseSqlServer(connectionString, sql => sql.UseAcmeTenancy(sp)));

public static SqlServerDbContextOptionsBuilder UseAcmeTenancy(
    this SqlServerDbContextOptionsBuilder sqlServerOptionsBuilder,
    string sessionKey = "TenantId",
    string policySchema = "acme")
{
    // From the provider builder to the actual DbContextOptionsBuilder
    var optionsBuilder =
        ((IRelationalDbContextOptionsBuilderInfrastructure)sqlServerOptionsBuilder)
        .OptionsBuilder;

    var extension = optionsBuilder.Options.FindExtension<SqlServerTenancyOptionsExtension>()
                    ?? new SqlServerTenancyOptionsExtension();
    // ... validate and set settings ...
    ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
        .AddOrUpdateExtension(extension.WithSettings(settings));

    return sqlServerOptionsBuilder;
}
''', label="Usage and Infrastructure/SqlServerTenancyOptionsBuilderExtensions.cs (shortened)")

S += h2("Migrations: security policy instead of policy expression")
S.append(p(
    "For SQL Server, the convention only records the tenant column; the SQL is produced in "
    "the generator. The predicate function is the same for all tables and is created "
    "idempotently:"))
S += code_block('''
protected virtual void CreatePolicy(string table, string? schema, string column,
    MigrationCommandListBuilder builder)
{
    var function = Delimit(PredicateFunctionName, Settings.PolicySchema);
    var target = Delimit(table, schema);
    var predicate = $"{function}({Delimit(column)})";

    // CREATE SCHEMA/FUNCTION must be alone in a batch -> EXEC(N'...')
    Statement(builder, $"IF SCHEMA_ID({Literal(Settings.PolicySchema)}) IS NULL "
        + $"EXEC({Literal($"CREATE SCHEMA {Delimit(Settings.PolicySchema)}")})");

    var functionSql =
        $"CREATE FUNCTION {function}(@TenantId uniqueidentifier) "
        + "RETURNS TABLE WITH SCHEMABINDING AS RETURN SELECT 1 AS [Allowed] "
        + $"WHERE @TenantId = CAST(SESSION_CONTEXT({Literal(Settings.SessionKey)}) "
        + "AS uniqueidentifier)";
    Statement(builder, $"IF OBJECT_ID({Literal(function)}, N'IF') IS NULL "
        + $"EXEC({Literal(functionSql)})");

    Statement(builder,
        $"CREATE SECURITY POLICY {PolicyName(table)} "
        + $"ADD FILTER PREDICATE {predicate} ON {target}, "
        + $"ADD BLOCK PREDICATE {predicate} ON {target} AFTER INSERT, "
        + $"ADD BLOCK PREDICATE {predicate} ON {target} AFTER UPDATE "
        + "WITH (STATE = ON)");
}

protected override void Generate(DropTableOperation operation, IModel? model,
    MigrationCommandListBuilder builder, bool terminate = true)
{
    DropPolicy(operation.Name, builder);       // SCHEMABINDING: the policy first
    base.Generate(operation, model, builder, terminate);
}
''', label="Migrations/Internal/SqlServerTenancyMigrationsSqlGenerator.cs (excerpt)")
S.append(p(
    c("SCHEMABINDING") + " only accepts two-part names. A table without an explicit schema "
    "is therefore qualified by the callers with the model's default schema or, failing "
    "that, with " + c("dbo") + "; otherwise SQL Server rejects the security policy."))
S += callout("important", [
    "The " + c("EXEC(N'...')") + " contains SQL in a string literal that in turn contains "
    "literals. The nesting is produced exclusively through the string mapping's " +
    c("GenerateSqlLiteral") + ", which doubles quotes correctly; identifiers go through " +
    c("DelimitIdentifier") + ". Session key and schema are additionally validated during "
    "configuration.",
], title="Nested SQL")

S += h2("Run time: same flow, different command")
S += code_block('''
public class SqlServerTenantSessionInterceptor : TenantSessionInterceptor
{
    public SqlServerTenantSessionInterceptor(ITenantProvider tenants)
        : base(tenants)
    {
    }

    protected override DbCommand CreateSessionCommand(
        DbConnection connection, DbContext? context, TenantId? tenant)
    {
        var key = context?.GetService<ISqlServerTenancySingletonOptions>()
                      .Settings.SessionKey
                  ?? SqlServerTenancySettings.Default.SessionKey;

        var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = @key, @value = @value";
        AddParameter(command, "@key", key);
        AddParameter(command, "@value", tenant?.Value);    // NULL: no rows visible
        return command;
    }
}
''', label="Runtime/SqlServerTenantSessionInterceptor.cs")

S += h2("Deliberately not ported")
S.append(p(
    "The SQL Server package is a complete but leaner provider. Fluent API policies, the "
    "optional query filter, custom SQL nodes, aggregates, the snapshot code generator and "
    "scaffolding are missing there. That is a product decision, not a technical obstacle: "
    "each of these chapters can be transferred to SQL Server with the same patterns. The "
    "list belongs in the package's documentation so users know what to expect."))
S += code_block('''
[Fact]
public async Task Block_predicate_rejects_rows_of_other_tenant()
{
    await using var db = ContextFor(TenantId.New());

    var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync(
        $"INSERT INTO [Orders] ([TenantId], [Total]) VALUES ({Guid.NewGuid()}, 1)"));

    Assert.Equal(33504, ex.Number);      // violation of a BLOCK PREDICATE
}
''', label="tests/...SqlServer.Tests/SqlServerTests.cs (excerpt)")
S.append(p(
    "As with PostgreSQL, the tests run on two levels: without a database for model, query "
    "and migration SQL, and with Testcontainers against a real SQL Server. There, the "
    "application connects with its own login instead of " + c("sa") + " to reproduce the "
    "production situation."))
