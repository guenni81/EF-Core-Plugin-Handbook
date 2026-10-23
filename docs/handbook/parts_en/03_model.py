# ============================================================ PART III
S += part(
    "The model",
    "Metadata, Fluent API, conventions, validation and new column types.",
    ["Chapter 7: Annotations and Fluent API",
     "Chapter 8: Conventions",
     "Chapter 9: Model validation",
     "Chapter 10: Type mappings and value converters"])

# ------------------------------------------------------------ 7
S += h1("Annotations and Fluent API")
S.append(p(
    "Annotations are EF's mechanism for extensible metadata: key-value pairs on every model "
    "element (model, entity type, property, index, ...). A plugin stores its configuration "
    "as annotations, and the entire remaining infrastructure (conventions, migrations, "
    "snapshots, compiled models) can work with them."))
S += h2("Rules for annotations")
S += data_table(
    ["Rule", "Reason"],
    [
        ["Own prefix (" + c("Acme:Rls:") + ")",
         "Avoids collisions with EF, providers and other plugins."],
        ["Simple values only: string, bool, numbers, enums",
         "Snapshots and migrations write values as C# literals. Custom classes cannot be "
         "serialized."],
        ["Complex values as a string (e.g. JSON)",
         "The differ compares strings reliably via " + c("Equals") + ". A record with a " +
         c("string[]") + " would be compared by reference and constantly appear “changed”."],
        ["Do not store default values",
         "“Annotation missing” and “annotation has default” differ for the differ and "
         "produce empty migrations."],
        ["One annotation per piece of configuration",
         "Every policy has its own annotation, so the diff shows exactly which one changed."],
    ], [1.5, 2.5])
S += code_block('''
public static class RlsAnnotationNames
{
    public const string Prefix = "Acme:Rls:";
    public const string Enabled = Prefix + "Enabled";
    public const string Forced = Prefix + "Forced";
    public const string PolicyPrefix = Prefix + "Policy:";     // + policy name
    public const string QueryFilterKey = "Acme.Tenancy";
}

public sealed record RlsPolicy(string Name, PolicyCommand Command, bool Permissive,
    string[]? Roles, string? Using, string? WithCheck)
{
    public string Serialize()
        => JsonSerializer.Serialize(this, RlsJsonContext.Default.RlsPolicy);

    public static RlsPolicy Deserialize(string json) => /* ... */;
}
''', label="Metadata/RlsAnnotationNames.cs, Metadata/RlsPolicy.cs (shortened)")

S += h2("The Fluent API")
S.append(p(
    "Fluent API methods are extension methods on EF's builders. Three design rules make "
    "them professional: the <b>non-generic</b> variant is the actual implementation (the "
    "model snapshot uses non-generic builders, chapter 20). The <b>generic</b> variant only "
    "delegates and preserves the type for further chaining. And the class lives in the "
    "namespace " + c("Microsoft.EntityFrameworkCore") + " so it is available everywhere "
    "without a " + c("using") + "."))
S += code_block('''
namespace Microsoft.EntityFrameworkCore;

public static class RlsEntityTypeBuilderExtensions
{
    public static EntityTypeBuilder HasRowLevelSecurity(
        this EntityTypeBuilder builder, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.SetAnnotation(RlsAnnotationNames.Enabled, true);

        if (force)
            builder.Metadata.SetAnnotation(RlsAnnotationNames.Forced, true);
        else
            builder.Metadata.RemoveAnnotation(RlsAnnotationNames.Forced);   // no default!

        return builder;
    }

    public static EntityTypeBuilder HasPolicy(
        this EntityTypeBuilder builder, string name, Action<PolicyBuilder> configure)
    {
        var policyBuilder = new PolicyBuilder(name);
        configure(policyBuilder);
        builder.Metadata.SetAnnotation(
            RlsAnnotationNames.PolicyPrefix + name, policyBuilder.Build().Serialize());
        return builder;
    }

    public static EntityTypeBuilder<TEntity> HasRowLevelSecurity<TEntity>(
        this EntityTypeBuilder<TEntity> builder, bool force = false) where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasRowLevelSecurity(
            (EntityTypeBuilder)builder, force);

    // ... HasNoRowLevelSecurity, HasPolicy<TEntity> analogous
}
''', label="Builders/RlsEntityTypeBuilderExtensions.cs (shortened)")
S.append(p(
    "Nested configuration such as " + c("HasPolicy") + " accepts an " +
    c("Action<PolicyBuilder>") + ". The builder checks the database's rules already while "
    "the model is built. A mistake like “INSERT policy with USING” therefore surfaces on "
    "the application's first start, not when the migration runs in production:"))
S += code_block('''
internal RlsPolicy Build()
{
    if (_using is null && _withCheck is null)
        throw new InvalidOperationException(
            $"Policy '{_name}' requires USING or WITH CHECK.");

    if (_command == PolicyCommand.Insert && _using is not null)
        throw new InvalidOperationException(
            $"INSERT policy '{_name}' only allows WITH CHECK.");

    if (_command is PolicyCommand.Select or PolicyCommand.Delete && _withCheck is not null)
        throw new InvalidOperationException(
            $"{_command} policy '{_name}' only allows USING.");

    return new RlsPolicy(_name, _command, _permissive,
        _roles.Count > 0 ? [.. _roles] : null, _using, _withCheck);
}
''', label="Builders/PolicyBuilder.cs › Build")

S += h2("Configuration sources")
S.append(p(
    "EF remembers for every piece of metadata <i>where</i> it came from. A source can "
    "override an equally strong or weaker one, but never a stronger one. This is the "
    "mechanism through which plugins set sensible defaults that users can always override:"))
S += data_table(
    ["ConfigurationSource", "Set by", "Strength"],
    [
        [c("Explicit"), "Fluent API in " + c("OnModelCreating") + " (" +
         c("Metadata.SetAnnotation") + ")", "strongest"],
        [c("DataAnnotation"), "Attribute conventions (" + c("fromDataAnnotation: true") + ")", "medium"],
        [c("Convention"), "Conventions via " + c("IConvention*Builder"), "weakest"],
    ], [1.2, 2.4, 0.8])
S.append(p(
    "For custom conventions the plugin therefore also offers overloads on the convention "
    "builder. They return " + c("null") + " when a stronger source decided otherwise:"))
S += code_block('''
public static IConventionEntityTypeBuilder? HasRowLevelSecurity(
    this IConventionEntityTypeBuilder builder,
    bool enabled,
    bool fromDataAnnotation = false)
    => builder.HasAnnotation(RlsAnnotationNames.Enabled, enabled, fromDataAnnotation)
           is null
        ? null
        : builder;
''', label="Builders/RlsEntityTypeBuilderExtensions.cs › convention variant")

# ------------------------------------------------------------ 8
S += h1("Conventions")
S.append(p(
    "Conventions change the model automatically. EF itself consists largely of "
    "conventions: discovering keys, finding relationships, deriving table names. A plugin "
    "adds its own to make configuration unnecessary."))
S += h2("Convention types and timing")
S.append(p(
    "A convention implements one or more interfaces. Each stands for an event during model "
    "building. The most important ones:"))
S += data_table(
    ["Interface", "Event", "Typical use"],
    [
        [c("IEntityTypeAddedConvention"), "Entity type was added",
         "Evaluate attributes, base configuration"],
        [c("IPropertyAddedConvention"), "Property was added", "Column types, naming conventions"],
        [c("IForeignKeyAddedConvention"), "Relationship created", "Indexes, delete behavior"],
        [c("IEntityTypeBaseTypeChangedConvention"), "Inheritance changed",
         "Move configuration"],
        [c("IModelFinalizingConvention"), "Model is being finalized (still mutable)",
         "Everything that depends on the finished model"],
        [c("IModelFinalizedConvention"), "Model is finished (read-only)", "Validation"],
    ], [1.6, 1.4, 1.6])
S += callout("important", [
    "Reactive conventions (" + c("*Added") + ", " + c("*Changed") + ") run while the user "
    "is still configuring. A column name may still change afterwards, an entity type may "
    "still get a base type. If you depend on that, you must react to <i>all</i> relevant "
    "events or use " + c("IModelFinalizingConvention") + ". The latter is simpler and "
    "right for most plugins.",
], title="The most common convention mistake")

S += h2("Plugging in conventions")
S.append(p(
    "The plugin interface " + c("IConventionSetPlugin") + " receives the convention set and "
    "adds its own conventions. " + c("ConventionSet.Add") + " registers a convention in all "
    "lists whose interfaces it implements, each time at the end:"))
S += code_block('''
public class TenancyConventionSetPlugin : IConventionSetPlugin
{
    private readonly ProviderConventionSetBuilderDependencies _dependencies;
    private readonly IDbContextOptions _options;
    private readonly ISqlGenerationHelper _sqlGenerationHelper;
    private readonly TenancyLoggingDefinitions _loggingDefinitions;

    public TenancyConventionSetPlugin(
        ProviderConventionSetBuilderDependencies dependencies,
        IDbContextOptions options,
        ISqlGenerationHelper sqlGenerationHelper,
        TenancyLoggingDefinitions loggingDefinitions)
    {
        // ... ArgumentNullException.ThrowIfNull for all parameters ...
        _dependencies = dependencies;
        _options = options;
        _sqlGenerationHelper = sqlGenerationHelper;
        _loggingDefinitions = loggingDefinitions;
    }

    public virtual ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        var settings = _options.FindExtension<TenancyOptionsExtension>()?.Settings;
        if (settings is null)
            return conventionSet;

        conventionSet.Add(new TenantIdKeyValueGenerationConvention());     // chapter 28
        conventionSet.Add(new NoTenantIsolationAttributeConvention(_dependencies));
        conventionSet.Add(new TenantIsolationConvention(
            settings, _sqlGenerationHelper, _dependencies.Logger, _loggingDefinitions));
        conventionSet.Add(new TenancyModelValidationConvention(
            _dependencies.Logger, _loggingDefinitions));

        return conventionSet;
    }
}
''', label="Conventions/TenancyConventionSetPlugin.cs")
S.append(p(
    c("ProviderConventionSetBuilderDependencies") + " is the parameter object that EF's own "
    "conventions receive too. Among other things it contains the model logger, the "
    "validation logger and the type mapping source. Other services such as " +
    c("ISqlGenerationHelper") + " can be injected as well, because the plugin itself is "
    "resolved from the internal provider."))

S += h2("The isolation convention")
S.append(p(
    "The sample's central convention runs at " + c("ModelFinalizing") + " because column "
    "names are only final then. It finds the tenant property (marked with " +
    c("IsTenantProperty()") + " or " + c("TenantId") + " for " + c("ITenantOwned") +
    ", chapter 39), enables RLS with a policy, creates an index, sets a runtime annotation "
    "for the SaveChanges interceptor and optionally a query filter:"))
S += code_block('''
public virtual void ProcessModelFinalizing(
    IConventionModelBuilder modelBuilder,
    IConventionContext<IConventionModelBuilder> context)
{
    foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
    {
        if (entityType.BaseType is not null       // root types of a hierarchy only
            || entityType.IsOwned())              // owned types share the table
            continue;

        // Marked property or "TenantId" with ITenantOwned (core package, chapter 39)
        if (entityType.FindTenantPropertyCandidate() is not IConventionProperty property)
            continue;                             // (+ type check, shortened)

        if (entityType.IsRlsOptedOut())
        {
            _logger.TenantIsolationOptedOut(_loggingDefinitions, entityType);  // warning
            continue;
        }

        // No table (view, ToSqlQuery, keyless) -> no RLS
        if (StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            is not { } table)
            continue;

        // Runtime annotation: this is how the SaveChanges interceptor finds the property
        entityType.Builder.HasAnnotation(
            TenancyAnnotationNames.TenantProperty, property.Name);

        ApplyRowLevelSecurity(entityType, property, table);
        if (_settings.CreateTenantIndex) EnsureTenantIndex(entityType, property);
        if (_settings.ApplyQueryFilter) ApplyQueryFilter(entityType, property);

        _logger.TenantIsolationApplied(_loggingDefinitions, entityType);       // debug
    }
}
''', label="Conventions/TenantIsolationConvention.cs › ProcessModelFinalizing")
S += code_block('''
protected virtual void ApplyRowLevelSecurity(
    IConventionEntityType entityType,
    IConventionProperty property,
    StoreObjectIdentifier table)
{
    // Column name for exactly this table (may differ with table splitting)
    var column = _sqlGenerationHelper.DelimitIdentifier(property.GetColumnName(table)!);
    var condition =
        $"{column} = nullif(current_setting('{_settings.SettingName}', true), '')::uuid";

    var policy = new RlsPolicy(_settings.PolicyName, PolicyCommand.All, Permissive: true,
        Roles: null, Using: condition, WithCheck: condition);

    // Convention builder: does NOT override explicit configuration
    var builder = entityType.Builder;
    builder.HasAnnotation(RlsAnnotationNames.Enabled, true);
    if (_settings.ForceRowLevelSecurity)
        builder.HasAnnotation(RlsAnnotationNames.Forced, true);
    builder.HasAnnotation(RlsAnnotationNames.PolicyPrefix + _settings.PolicyName,
        policy.Serialize());
}
''', label="Conventions/TenantIsolationConvention.cs › ApplyRowLevelSecurity")
S += callout("important", [
    "The setting name is written into the policy expression without escaping. Unlike SQL "
    "that the developer writes in " + c("Using(...)") + " themselves, here the "
    "<i>plugin</i> assembles SQL and is responsible for keeping invalid input out. That is "
    "why the options builder validates the name with a strict regular expression (" +
    c("[a-z_][a-z0-9_]*\\.[a-z_][a-z0-9_]*") + ").",
], title="Validate SQL you generate yourself")

S += h2("Structural changes: index and query filter")
S.append(p(
    "Conventions may not only set annotations but change the model structurally. The index "
    "is only created if no index or key already starts with the tenant column. The query "
    "filter is built as an expression tree and registered as a <b>named filter</b>, new in "
    "EF Core 10. Named filters coexist with the user's own filters and can be switched off "
    "selectively with " + c('IgnoreQueryFilters(["Acme.Tenancy"])') + "."))
S += code_block('''
protected virtual void EnsureTenantIndex(
    IConventionEntityType entityType, IConventionProperty property)
{
    var covered = entityType.GetIndexes().Any(i => i.Properties[0] == property)
                  || entityType.GetKeys().Any(k => k.Properties[0] == property);
    if (!covered)
        entityType.Builder.HasIndex(new[] { property });
}

protected virtual void ApplyQueryFilter(
    IConventionEntityType entityType, IConventionProperty property)
{
    // e => EF.Property<TenantId>(e, "...") == EF.Functions.CurrentTenant()
    // EF.Property works for any property name and for shadow properties too
    var parameter = Expression.Parameter(entityType.ClrType, "e");
    var body = Expression.Equal(
        Expression.Call(typeof(EF), nameof(EF.Property), [typeof(TenantId)],
            parameter, Expression.Constant(property.Name)),
        Expression.Call(CurrentTenantMethod, Expression.Property(null, FunctionsProperty)));

    entityType.Builder.HasQueryFilter(
        RlsAnnotationNames.QueryFilterKey, Expression.Lambda(body, parameter));
}
''', label="Conventions/TenantIsolationConvention.cs (excerpt)")
S.append(p(
    "Notably, the filter uses the plugin's own function " +
    c("EF.Functions.CurrentTenant()") + ". It is therefore translated to SQL by the "
    "translator from chapter 12. The plugin's building blocks interlock."))
S += h2("Final names, whoever sets them")
S.append(p(
    "The isolation convention reads the column name only at " + c("ModelFinalizing") +
    " and only through " + c("GetColumnName(table)") + ". Names that other conventions or "
    "plugins change are final by then. The sample checks this against "
    "EFCore.NamingConventions, which renames tables and columns to snake_case, in both "
    "activation orders:"))
S += code_block('''
options.UseNpgsql(connectionString).UseAcmeTenancy().UseSnakeCaseNamingConvention();
''', label="Usage")
S += code_block('''
ALTER TABLE orders ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON orders AS PERMISSIVE FOR ALL
    USING (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);
CREATE INDEX ix_orders_tenant_id ON orders (tenant_id);
''', lang="sql", label="Generated migration SQL (excerpt, tests/.../NamingConventionTests.cs)")
S += callout("tip", [
    "A plugin that assembles names itself (from " + c("Name") + " properties or by string "
    "concatenation) breaks as soon as a naming plugin is active. Always ask the model for "
    "the final name, and as late as possible.",
], title="Never assemble names yourself")

S += h2("Attribute conventions")
S.append(p(
    "Data annotations are evaluated with an attribute convention. EF provides the base "
    "class " + c("TypeAttributeConventionBase<TAttribute>") + ", which finds the attribute "
    "and only asks for the reaction. The value is set with " +
    c("fromDataAnnotation: true") + " so the Fluent API can override it:"))
S += code_block('''
public class NoTenantIsolationAttributeConvention
    : TypeAttributeConventionBase<NoTenantIsolationAttribute>
{
    public NoTenantIsolationAttributeConvention(
        ProviderConventionSetBuilderDependencies dependencies)
        : base(dependencies)
    {
    }

    protected override void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        NoTenantIsolationAttribute attribute,
        IConventionContext<IConventionEntityTypeBuilder> context)
        => entityTypeBuilder.HasRowLevelSecurity(enabled: false, fromDataAnnotation: true);
}
''', label="Conventions/NoTenantIsolationAttributeConvention.cs")
S += callout("tip", [
    "The attribute itself lives in the <i>core package</i>, the convention in the provider "
    "package. Entity classes can use the attribute without depending on PostgreSQL. This "
    "is the same split as with EF's own attributes in " +
    c("System.ComponentModel.DataAnnotations") + ".",
])

# ------------------------------------------------------------ 9
S += h1("Model validation")
S.append(p(
    "A plugin should detect misconfiguration before it leads to wrong SQL or data leaks. "
    "EF validates models through " + c("IModelValidator") + ", but there is only one, and "
    "it belongs to the provider. For plugins, an " + c("IModelFinalizedConvention") +
    " is the better place: it runs on the finished model and collides with nobody."))
S += code_block('''
public virtual IModel ProcessModelFinalized(IModel model)
{
    foreach (var entityType in model.GetEntityTypes())
    {
        if (!entityType.HasRlsConfiguration())
            continue;

        if (entityType.BaseType is not null)
            throw new InvalidOperationException(
                $"RLS on '{entityType.DisplayName()}' is invalid: only allowed on the root "
                + $"type ('{entityType.GetRootType().DisplayName()}').");

        if (StoreObjectIdentifier.Create(entityType, StoreObjectType.Table) is null)
            throw new InvalidOperationException("... not mapped to a table ...");

        if (!entityType.IsRlsEnabled() && entityType.GetRlsPolicies().Count > 0)
            _logger.PoliciesWithoutRowLevelSecurity(_loggingDefinitions, entityType);
    }
    return model;
}
''', label="Conventions/TenancyModelValidationConvention.cs")
S.append(p(
    "The validation distinguishes two kinds of problems. <b>Structural errors</b> that are "
    "guaranteed to lead to wrong behavior throw an exception. <b>Questionable "
    "configuration</b> that may be intended produces a warning through the logging system. "
    "Users can turn such warnings into errors selectively (chapter 16)."))

# ------------------------------------------------------------ 10
S += h1("Type mappings and value converters")
S.append(p(
    "A type mapping connects a CLR type with a database type. It determines the column type "
    "in migrations, how parameters are set, what literals look like in SQL, how values are "
    "compared and copied and how they are stored in JSON columns. For custom types a plugin "
    "has two options:"))
S += data_table(
    ["Approach", "Advantages", "Disadvantages"],
    [
        ["Value converter (user configures " + c("HasConversion") + ")",
         "No plugin needed", "Must be configured for every property or via " +
         c("ConfigureConventions")],
        [c("IRelationalTypeMappingSourcePlugin"),
         "Type works everywhere without configuration, own literals, full control",
         "Provider-specific (store type)"],
    ], [1.5, 1.7, 1.7])

S += h2("The type and its converter")
S.append(p(
    "The strongly typed " + c("TenantId") + " and its converter live in the core package. "
    "The converter has a shared instance; the section on compiled models explains why."))
S += code_block('''
public readonly record struct TenantId(Guid Value)
{
    public static TenantId Empty => default;
    public bool IsEmpty => Value == Guid.Empty;              // translated to SQL
    public static TenantId New() => new(Guid.CreateVersion7());
}

public sealed class TenantIdConverter : ValueConverter<TenantId, Guid>
{
    public static TenantIdConverter Instance { get; } = new();

    public TenantIdConverter()
        : base(id => id.Value, value => new TenantId(value))
    {
    }
}
''', label="TenantId.cs, TenantIdConverter.cs (shortened)")

S += h2("The type mapping")
S += code_block('''
public class TenantIdTypeMapping : RelationalTypeMapping
{
    // Required for compiled models: public static Default property
    public static TenantIdTypeMapping Default { get; } = new();

    public TenantIdTypeMapping()
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(TenantId),
                converter: TenantIdConverter.Instance,
                jsonValueReaderWriter: TenantIdJsonReaderWriter.Instance),
            storeType: "uuid",
            dbType: System.Data.DbType.Guid))
    {
    }

    protected TenantIdTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    // EF clones mappings, e.g. for facets or changed comparers
    protected override RelationalTypeMapping Clone(
        RelationalTypeMappingParameters parameters)
        => new TenantIdTypeMapping(parameters);

    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var guid = value switch
        {
            TenantId id => id.Value,
            Guid g => g,
            _ => throw new ArgumentException($"Unexpected type {value.GetType()}."),
        };
        return $"'{guid}'::uuid";
    }
}
''', label="Storage/TenantIdTypeMapping.cs")
S.append(p(
    "Every custom mapping needs the protected constructor taking " +
    c("RelationalTypeMappingParameters") + " and a " + c("Clone") + " override that creates "
    "its own type. Without it, a cloned mapping loses its behavior, for example literal "
    "generation."))

S += h2("The type mapping plugin")
S.append(p(
    c("RelationalTypeMappingSource") + " asks all plugins <i>before</i> falling back to the "
    "provider's mappings. A plugin returns " + c("null") + " when it is not responsible:"))
S += code_block('''
public class TenancyTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    public virtual RelationalTypeMapping? FindMapping(
        in RelationalTypeMappingInfo mappingInfo)
    {
        if (mappingInfo.ClrType != typeof(TenantId))
            return null;

        // Explicitly different column type (HasColumnType("text")): not responsible
        var storeTypeBase = mappingInfo.StoreTypeNameBase;
        if (storeTypeBase is not null
            && !string.Equals(storeTypeBase, "uuid", StringComparison.OrdinalIgnoreCase))
            return null;

        return TenantIdTypeMapping.Default;
    }
}
''', label="Storage/TenancyTypeMappingSourcePlugin.cs")
S += callout("note", [
    "Without a mapping, EF would not recognize " + c("TenantId") + " as a scalar type during "
    "model building and would abort with an error. With the plugin, the type works as a "
    "property, as a key, in queries, as a parameter and in JSON columns, without a single "
    "line of configuration.",
])

S += h2("JSON support")
S.append(p(
    "For JSON columns a mapping needs a " + c("JsonValueReaderWriter") + ". It has three "
    "duties: read, write, and describe how it is created in generated code. The pattern "
    "follows EF's own " + c("JsonGuidReaderWriter") + ":"))
S += code_block('''
public sealed class TenantIdJsonReaderWriter : JsonValueReaderWriter<TenantId>
{
    private static readonly PropertyInfo InstanceProperty =
        typeof(TenantIdJsonReaderWriter).GetProperty(nameof(Instance))!;

    public static TenantIdJsonReaderWriter Instance { get; } = new();

    private TenantIdJsonReaderWriter()
    {
    }

    public override TenantId FromJsonTyped(
        ref Utf8JsonReaderManager manager, object? existingObject = null)
        => new(manager.CurrentReader.GetGuid());

    public override void ToJsonTyped(Utf8JsonWriter writer, TenantId value)
        => writer.WriteStringValue(value.Value);

    // For compiled models and precompiled queries
    public override Expression ConstructorExpression
        => Expression.Property(null, InstanceProperty);
}
''', label="TenantIdJsonReaderWriter.cs")
S += callout("important", [
    "When generating for NativeAOT (" + c("dotnet ef dbcontext optimize --nativeaot") +
    "), the compiled model generator (chapter 21) emits code of the form " +
    c("TenantIdTypeMapping.Default.Clone(...)") + " for every type mapping. It requires a "
    "<b>public static property</b> named " + c("Default") + " on a <b>public</b> type and "
    "aborts otherwise. It only takes over converters and readers/writers without extra code "
    "when they are <i>the same instance</i> as in the default mapping. That is why the "
    "mapping uses the static instances rather than " + c("new ...()") + ". Without "
    "NativeAOT, EF 10 resolves type mappings at startup instead; a plugin should still meet "
    "the requirements, because users decide on NativeAOT, not the plugin.",
], title="Compiled models need Default and Instance")
