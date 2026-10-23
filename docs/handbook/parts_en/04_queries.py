# ============================================================ PART IV
S += part(
    "Queries",
    "How LINQ becomes SQL and how a plugin translates its own functions and members.",
    ["Chapter 11: The query pipeline",
     "Chapter 12: Translating methods and members",
     "Chapter 13: Preventing client-side evaluation",
     "Chapter 14: Further query extension points"])

# ------------------------------------------------------------ 11
S += h1("The query pipeline")
S.append(p(
    "A LINQ query passes through several stages in EF Core before SQL is produced. Knowing "
    "where each stage sits tells you where a plugin has to hook in:"))
S += flow_diagram([
    ("Funcletizer", "Evaluates sub-expressions without data reference in .NET and turns "
                    "them into parameters. Plugin: IEvaluatableExpressionFilterPlugin"),
    ("Preprocessing", "Expands navigations, inserts query filters, normalizes. "
                      "Plugin: IQueryExpressionInterceptor"),
    ("Queryable translation", "Where, Select, OrderBy etc. become a SelectExpression"),
    ("SQL translation", "Expressions become SqlExpressions. Plugins: method, member "
                        "and aggregate translators"),
    ("Postprocessing", "Nullability optimization, parameter-dependent adjustments"),
    ("SQL generation", "QuerySqlGenerator writes the SQL text"),
    ("Materialization", "Rows become objects. Plugin: IMaterializationInterceptor"),
])
S.append(p(
    "The compiled query is cached. Translators therefore run only once per query shape, not "
    "on every execution. Values that change per execution (parameters, session settings) "
    "must therefore appear in SQL as parameters or as a database function, never as a "
    "constant inserted at translation time. That is exactly why the sample translates " +
    c("CurrentTenant()") + " into a call to " + c("current_setting(...)") + " instead of "
    "inserting the current tenant."))

# ------------------------------------------------------------ 12
S += h1("Translating methods and members")
S += h2("The EF.Functions pattern")
S.append(p(
    "Database functions without a .NET counterpart are declared as extension methods on " +
    c("DbFunctions") + ". They are never executed; the body only throws an exception in "
    "case someone calls them outside of LINQ. The declaration lives in the core package, "
    "the translation in the provider package:"))
S += code_block('''
namespace Microsoft.EntityFrameworkCore;

public static class TenancyDbFunctionsExtensions
{
    public static TenantId CurrentTenant(this DbFunctions _)
        => throw new InvalidOperationException(
            $"{nameof(CurrentTenant)} can only be used in LINQ to Entities queries.");
}
''', label="TenancyDbFunctionsExtensions.cs (core package)")

S += h2("A method call translator")
S.append(p(
    "An " + c("IMethodCallTranslator") + " receives every method call EF does not know "
    "itself. It checks whether it is responsible and then builds a SQL expression tree "
    "using " + c("ISqlExpressionFactory") + ":"))
S += code_block('''
public class CurrentTenantTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo CurrentTenantMethod =
        typeof(TenancyDbFunctionsExtensions).GetRuntimeMethod(
            nameof(TenancyDbFunctionsExtensions.CurrentTenant), [typeof(DbFunctions)])!;

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ITenancySingletonOptions _options;

    public CurrentTenantTranslator(
        ISqlExpressionFactory sql,
        IRelationalTypeMappingSource typeMappingSource,
        ITenancySingletonOptions options)
    {
        // ... ArgumentNullException.ThrowIfNull for all parameters ...
        _sql = sql;
        _typeMappingSource = typeMappingSource;
        _options = options;
    }

    public virtual SqlExpression? Translate(
        SqlExpression? instance, MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method != CurrentTenantMethod)
            return null;                          // not responsible -> next translator

        var text = _typeMappingSource.FindMapping(typeof(string))!;
        var boolean = _typeMappingSource.FindMapping(typeof(bool))!;

        var currentSetting = _sql.Function(
            "current_setting",
            [
                _sql.Constant(_options.Settings.SettingName, text),
                _sql.Constant(true, boolean),
            ],
            nullable: true,
            argumentsPropagateNullability: [true, false],
            typeof(string), text);

        var nullIfEmpty = _sql.Function(
            "nullif",
            [currentSetting, _sql.Constant(string.Empty, text)],
            nullable: true,
            argumentsPropagateNullability: [true, false],
            typeof(string), text);

        return _sql.Convert(nullIfEmpty, typeof(TenantId), TenantIdTypeMapping.Default);
    }
}
''', label="Query/CurrentTenantTranslator.cs")
S.append(p("This turns " + c("o.TenantId == EF.Functions.CurrentTenant()") + " into:"))
S += code_block('''
WHERE o."TenantId" = CAST(nullif(current_setting('app.tenant_id', TRUE), '') AS uuid)
''', lang="sql", label="Generated SQL (excerpt)")
S += data_table(
    ["Detail", "Meaning"],
    [
        [c("return null"), "Contract for “not responsible”. Never throw for foreign methods; "
         "the next translator gets its turn."],
        [c("arguments"), "For extension methods, " + c("arguments[0]") + " is the " +
         c("this") + " parameter (here " + c("DbFunctions") + ")."],
        [c("nullable"), "Can the result be NULL? EF uses this to optimize " +
         c("IS NULL") + " checks. Wrong values lead to wrong results."],
        [c("argumentsPropagateNullability"), "Is the result NULL when this argument is NULL? "
         "For " + c("nullif(x, '')") + ": yes for x, no for the empty string."],
        ["Type mappings", "Every SQL expression needs a mapping. It determines literals, "
         "parameters and casts. " + c("Convert") + " with the TenantId mapping produces " +
         c("CAST(... AS uuid)") + "."],
        [c("Constant(...)"), "Constants become literals via the mapping and are escaped "
         "correctly, including the setting name."],
    ], [1.3, 3])

S += h2("A member translator")
S.append(p(
    "For properties and fields there is " + c("IMemberTranslator") + ". The sample "
    "translates " + c("tenantId.IsEmpty") + ". The constant gets the column's type mapping "
    "so that the literal comes out in the right format:"))
S += code_block('''
public class TenantIdMemberTranslator : IMemberTranslator
{
    private static readonly PropertyInfo IsEmptyProperty =
        typeof(TenantId).GetProperty(nameof(TenantId.IsEmpty))!;

    private readonly ISqlExpressionFactory _sql;

    public TenantIdMemberTranslator(ISqlExpressionFactory sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        _sql = sql;
    }

    public virtual SqlExpression? Translate(
        SqlExpression? instance, MemberInfo member, Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is null || !Equals(member, IsEmptyProperty))
            return null;

        return _sql.Equal(instance, _sql.Constant(TenantId.Empty, instance.TypeMapping));
    }
}
''', label="Query/TenantIdMemberTranslator.cs")

S += h2("Translator plugins")
S.append(p(
    "Translators are not registered directly, but through plugin classes that return a "
    "list. The plugins are <b>scoped</b> (appendix A) and can therefore inject scoped "
    "services such as " + c("ISqlExpressionFactory") + ":"))
S += code_block('''
public class TenancyMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public TenancyMethodCallTranslatorPlugin(
        ISqlExpressionFactory sql,
        IRelationalTypeMappingSource typeMappingSource,
        ITenancySingletonOptions options)
    {
        Translators = [new CurrentTenantTranslator(sql, typeMappingSource, options)];
    }

    public virtual IEnumerable<IMethodCallTranslator> Translators { get; }
}

public class TenancyMemberTranslatorPlugin : IMemberTranslatorPlugin
{
    public TenancyMemberTranslatorPlugin(ISqlExpressionFactory sql)
    {
        Translators = [new TenantIdMemberTranslator(sql)];
    }

    public virtual IEnumerable<IMemberTranslator> Translators { get; }
}
''', label="Query/TenancyTranslatorPlugins.cs")
S += callout("tip", [
    c("query.ToQueryString()") + " produces a query's SQL without executing it and without "
    "a database connection. It is the most important tool for developing and testing "
    "translators (chapter 23).",
])

# ------------------------------------------------------------ 13
S += h1("Preventing client-side evaluation")
S.append(p(
    "The first stage of the pipeline, the funcletizer, evaluates every sub-expression in "
    ".NET up front that has no reference to query data. " + c("o.Total > limit * 2") +
    " thus becomes a parameter. For " + c("EF.Functions.CurrentTenant()") + " this would be "
    "fatal: the expression has no data reference, so EF would execute the method, which "
    "throws its exception."))
S.append(p(
    "EF knows only a few exceptions. In the source of " + c("EvaluatableExpressionFilter") +
    " (EF Core 10) they are " + c("DateTime.Now") + ", " + c("DateTime.UtcNow") + ", " +
    c("DateTime.Today") + ", their " + c("DateTimeOffset") + " counterparts, " +
    c("Guid.NewGuid()") + ", " + c("Random.Next(...)") + " and all methods declared in EF's "
    "own class " + c("DbFunctionsExtensions") + ". Custom EF.Functions methods live in a "
    "different class and are <b>not</b> covered. After that, EF asks all registered plugins:"))
S += code_block('''
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TenancyDbFunctionsAttribute : Attribute;

public class TenancyEvaluatableExpressionFilterPlugin : IEvaluatableExpressionFilterPlugin
{
    public virtual bool IsEvaluatableExpression(Expression expression)
        => expression is not MethodCallExpression { Method.DeclaringType: { } type }
           || !type.IsDefined(typeof(TenancyDbFunctionsAttribute), inherit: false);
}

[TenancyDbFunctions]
public static class TenancyDbFunctionsExtensions { /* CurrentTenant() */ }
''', label="Core package: TenancyDbFunctionsAttribute.cs, Query/TenancyEvaluatable…Plugin.cs")
S.append(p(
    "The plugin lives in the <b>core package</b> and recognizes function classes by an "
    "attribute. A single plugin thus also covers the PostgreSQL-specific functions from "
    "chapters 37 and 38 and every future provider package, without the core having to know "
    "these classes."))
S += callout("important", [
    "Whether a custom EF.Functions method needs this plugin depends on whether it has "
    "arguments referencing data. " + c("EF.Functions.Soundex(c.Name)") + " is harmless "
    "because " + c("c.Name") + " points to a column. Methods without arguments or only with "
    "constants (" + c("CurrentTenant()") + ", " + c("Random()") + ", " +
    c("ServerTime()") + ") always need it. Without it, the exception comes from the method "
    "body, which is hard to diagnose.",
], title="When the plugin is needed")

# ------------------------------------------------------------ 14
S += h1("Further query extension points")
S.append(p(
    "The sample needs method and member translators. Complex plugins use further points; "
    "here is an overview with classification:"))
S += data_table(
    ["Extension point", "Purpose", "Notes"],
    [
        [c("IAggregateMethodCallTranslatorPlugin"), "Custom aggregate functions in " +
         c("GroupBy") + " (e.g. " + c("bool_and") + ", statistics functions)",
         "Scoped, multiple registrations; implemented in chapter 38"],
        [c("IQueryExpressionInterceptor"), "Rewrite the LINQ expression tree before "
         "translation", "Singleton interceptor; ideal for query rewrites, tags, hints"],
        [c("IMaterializationInterceptor"), "Create, initialize or check objects during "
         "materialization", "Singleton interceptor; must be stateless and thread-safe"],
        [c("IMethodCallTranslatorProvider") + " etc.", "Replace translator infrastructure",
         "Single provider services; only in exceptional cases"],
        [c("IQuerySqlGeneratorFactory"), "SQL generation for custom SqlExpression types",
         "Singleton, provider service; implemented in chapter 37"],
        [c("IRelationalParameterBasedSqlProcessorFactory"), "Nullability processing of "
         "custom expression nodes", "Scoped, provider service; implemented in chapter 37"],
        [c("IQueryTranslationPostprocessorFactory"), "Adjust the expression tree after "
         "translation", "Provider service; deep intervention, tightly bound to EF internals"],
    ], [1.6, 1.8, 1.6])
S += callout("note", [
    "With NativeAOT and precompiled queries (since EF Core 9), EF translates queries at "
    "build time. Translators then run in the build process, not at run time. They may "
    "therefore only depend on options and model, never on runtime state. The sample meets "
    "this: the setting name is an option; the tenant itself is only evaluated in the "
    "database session.",
], title="Precompiled queries")
