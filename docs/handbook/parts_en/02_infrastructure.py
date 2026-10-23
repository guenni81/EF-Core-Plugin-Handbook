# ============================================================ PART II
S += part(
    "Infrastructure",
    "How a plugin is configured, registers its services and connects to the application's "
    "DI container.",
    ["Chapter 4: The options extension",
     "Chapter 5: Registering services",
     "Chapter 6: Public API and integration into the application"])

# ------------------------------------------------------------ 4
S += h1("The options extension")
S.append(p(
    "The entry point of every real plugin is a class implementing " +
    c("IDbContextOptionsExtension") + ". It has three jobs: it carries the configuration, it "
    "registers the services in the internal provider in " + c("ApplyServices") + ", and it "
    "describes itself to the cache through " + c("Info") + "."))
S += h2("Configuration as a record")
S.append(p(
    "All model-relevant settings of the sample live in a record. Records provide " +
    c("Equals") + " and " + c("GetHashCode") + " over all fields, which is exactly what the "
    "cache key needs. New settings are added to the record without touching hashing or "
    "comparison."))
S += code_block('''
public sealed record TenancySettings
{
    public static TenancySettings Default { get; } = new();

    public string SettingName { get; init; } = "app.tenant_id";
    public string PolicyName { get; init; } = "tenant_isolation";
    public bool ForceRowLevelSecurity { get; init; } = true;
    public bool ApplyQueryFilter { get; init; }
    public bool CreateTenantIndex { get; init; } = true;
}
''', label="Infrastructure/TenancySettings.cs")

S += h2("Immutability")
S.append(p(
    "EF compares and caches options. Once added, an extension must therefore never change "
    "again; every change creates a copy. The pattern from EF Core itself: a protected copy "
    "constructor and a virtual " + c("Clone") + " method, so derived extensions clone their "
    "own type."))
S += code_block('''
public class TenancyOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public TenancyOptionsExtension()
    {
    }

    protected TenancyOptionsExtension(TenancyOptionsExtension copyFrom)
    {
        ArgumentNullException.ThrowIfNull(copyFrom);

        Settings = copyFrom.Settings;
    }

    public virtual TenancySettings Settings { get; private set; } = TenancySettings.Default;

    public virtual DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    protected virtual TenancyOptionsExtension Clone() => new(this);

    public virtual TenancyOptionsExtension WithSettings(TenancySettings settings)
    {
        var clone = Clone();
        clone.Settings = settings;
        return clone;
    }
''', label="Infrastructure/TenancyOptionsExtension.cs (excerpt)")

S += h2("The info class and the service provider cache")
S.append(p(
    "The info class decides whether EF may reuse a cached internal service provider. Two "
    "methods must agree for this: " + c("GetServiceProviderHashCode") + " as a fast "
    "pre-filter and " + c("ShouldUseSameServiceProvider") + " as the exact comparison."))
S += code_block('''
private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
{
    public ExtensionInfo(TenancyOptionsExtension extension)
        : base(extension)
    {
    }

    private new TenancyOptionsExtension Extension => (TenancyOptionsExtension)base.Extension;

    public override bool IsDatabaseProvider => false;

    public override string LogFragment
        => $"AcmeTenancy(Setting={Extension.Settings.SettingName}) ";

    public override int GetServiceProviderHashCode() => Extension.Settings.GetHashCode();

    public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        => other is ExtensionInfo o && o.Extension.Settings == Extension.Settings;

    public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
    {
        debugInfo["Acme:Tenancy:Force"] =
            Extension.Settings.ForceRowLevelSecurity.ToString();
        // ... further settings
    }
}
''', label="Infrastructure/TenancyOptionsExtension.cs › ExtensionInfo")
S.append(p("There are two classic mistakes with these methods, with opposite consequences:"))
S += data_table(
    ["Mistake", "Consequence"],
    [
        ["A model-relevant option is missing from the hash",
         "Two contexts with different configuration share provider and model. The second "
         "gets the cached, <b>wrong</b> model. Often unnoticed in tests, because only one "
         "configuration runs there."],
        ["A value that varies per context is in the hash (tenant, connection string, instance)",
         "A new internal provider including model is created for every context. Memory and "
         "startup time explode; EF warns after 20 providers with " +
         c("ManyServiceProvidersCreatedWarning") + "."],
    ], [1.3, 2.7])
S += callout("important", [
    "The hash must contain exactly what <b>influences which services are registered or "
    "what the model looks like</b>, and nothing else. The tenant itself changes per "
    "request and must therefore never end up in the options; it arrives at run time "
    "through an interceptor (chapter 15).",
], title="The rule of thumb for the hash")
S.append(p(
    c("LogFragment") + " appears in EF's startup message (“initialized using provider … "
    "with options: …”) and helps when troubleshooting. " + c("PopulateDebugInfo") + " "
    "supplies values for EF's diagnostics when too many service providers are created. " +
    c("IsDatabaseProvider") + " is " + c("true") + " only for real providers."))

S += h2("Validation")
S.append(p(
    c("Validate") + " is called when the service provider is created. Here the plugin "
    "checks preconditions arising from the combination of extensions and reports them "
    "with a clear message instead of a later follow-up error:"))
S += code_block('''
public virtual void Validate(IDbContextOptions options)
{
    if (options.FindExtension<NpgsqlOptionsExtension>() is null)
        throw new InvalidOperationException(
            "UseAcmeTenancy() requires UseNpgsql(). Other databases need their own "
            + "provider package.");
}
''', label="Infrastructure/TenancyOptionsExtension.cs › Validate")

# ------------------------------------------------------------ 5
S += h1("Registering services")
S.append(p(
    c("ApplyServices") + " fills the internal service provider. EF provides a builder for "
    "this that knows the lifetime and cardinality of every EF service: " +
    c("EntityFrameworkServicesBuilder") + " for core services and " +
    c("EntityFrameworkRelationalServicesBuilder") + " additionally for relational services. "
    "It registers each service with the correct lifetime and automatically uses " +
    c("TryAddEnumerable") + " for services that can be registered multiple times."))
S += code_block('''
public virtual void ApplyServices(IServiceCollection services)
{
    // 1) Plugin interfaces: multiple registrations, composable with other plugins
    new EntityFrameworkRelationalServicesBuilder(services)
        .TryAdd<IRelationalTypeMappingSourcePlugin, TenancyTypeMappingSourcePlugin>()
        .TryAdd<IMethodCallTranslatorPlugin, TenancyMethodCallTranslatorPlugin>()
        .TryAdd<IMemberTranslatorPlugin, TenancyMemberTranslatorPlugin>()
        .TryAdd<IEvaluatableExpressionFilterPlugin,
                TenancyEvaluatableExpressionFilterPlugin>()
        .TryAdd<IConventionSetPlugin, TenancyConventionSetPlugin>()
        .TryAdd<ISingletonOptions, ITenancySingletonOptions>(
            p => p.GetRequiredService<ITenancySingletonOptions>())
        // 2) Own services that EF does not know
        .TryAddProviderSpecificServices(m => m
            .TryAddSingleton<ITenancySingletonOptions, TenancySingletonOptions>()
            .TryAddSingleton<TenancyLoggingDefinitions, TenancyLoggingDefinitions>());

    // 3) Replace single services of the provider
    services.Replace(ServiceDescriptor
        .Scoped<IMigrationsSqlGenerator, TenancyMigrationsSqlGenerator>());
    services.Replace(ServiceDescriptor
        .Singleton<IRelationalAnnotationProvider, TenancyAnnotationProvider>());
}
''', label="Infrastructure/TenancyOptionsExtension.cs › ApplyServices")
S += data_table(
    ["Method", "When", "Why"],
    [
        [c("TryAdd<TService, TImpl>()"), "EF services and plugin interfaces",
         "Lifetime and multiple registration come from EF's table. Wrong lifetimes otherwise "
         "cause exceptions or subtly wrong behavior."],
        [c("TryAddProviderSpecificServices"), "Own services EF does not know",
         "The builder rejects unknown types; here you set the lifetime yourself."],
        [c("services.Replace(...)"), "Replace a single provider service",
         "The provider registers with " + c("TryAdd") + ". " + c("Replace") +
         " wins regardless of whether the plugin runs before or after the provider."],
    ], [1.3, 1.2, 2.2])
S += callout("note", [
    "EF calls the extensions' " + c("ApplyServices") + " in the order they were added. If "
    "the plugin runs first, " + c("Replace") + " adds the service and the provider's later " +
    c("TryAdd") + " does nothing. If it runs afterwards, " + c("Replace") + " replaces the "
    "provider's service. Both orders lead to the same result.",
], title="Why Replace works in both orders")

S += h2("Singleton options")
S.append(p(
    "Singleton services such as type mapping plugins cannot resolve " +
    c("IDbContextOptions") + " per context. For them there is " + c("ISingletonOptions") +
    ": a singleton that EF initializes once from the options when creating the provider "
    "and validates for every further context."))
S += code_block('''
public class TenancySingletonOptions : ITenancySingletonOptions
{
    public TenancySettings Settings { get; private set; } = TenancySettings.Default;

    // Once per internal service provider
    public virtual void Initialize(IDbContextOptions options)
        => Settings = options.FindExtension<TenancyOptionsExtension>()?.Settings
                      ?? TenancySettings.Default;

    // For every context: options must not differ from the provider's state
    public virtual void Validate(IDbContextOptions options)
    {
        var settings = options.FindExtension<TenancyOptionsExtension>()?.Settings
                       ?? TenancySettings.Default;
        if (settings != Settings)
            throw new InvalidOperationException("Tenancy options changed afterwards ...");
    }
}
''', label="Infrastructure/ITenancySingletonOptions.cs")
S.append(p(
    "The double registration in " + c("ApplyServices") + " is required: once under its "
    "own interface so services can inject it, and once as " + c("ISingletonOptions") +
    " so EF finds and initializes it. The factory variant ensures that both registrations "
    "return <i>the same</i> instance."))

S += h2("Staying composable despite replacement")
S.append(p(
    "When a plugin replaces a single service, a second plugin doing the same cannot be "
    "active at the same time. Professional plugins mitigate this by additionally offering "
    "the actual logic publicly and reusably. In the sample, " + c("CreatePolicySql") +
    " is public and all generator methods are virtual. A user who must combine two plugins "
    "writes their own generator deriving from one and calling the logic of the other. This "
    "option belongs in the documentation."))
S.append(p(
    "Better still, a plugin avoids the conflict altogether by <b>decorating</b> the service "
    "instead of replacing it: it takes whatever implementation is registered at that point "
    "(the provider's or another plugin's), wraps it and delegates to it. An example is a "
    "(fictional) audit plugin that attaches a trigger to every new table. In its " +
    c("ApplyServices") + " it looks up the existing registration and replaces only the "
    "descriptor, not the logic:"))
S += code_block('''
public void ApplyServices(IServiceCollection services)
{
    // Registered so far: the provider's generator or that of another plugin
    var inner = services.LastOrDefault(d => d.ServiceType == typeof(IMigrationsSqlGenerator))
        ?? throw new InvalidOperationException("Call UseContosoAudit() after UseNpgsql().");

    services.Replace(ServiceDescriptor.Describe(
        typeof(IMigrationsSqlGenerator),
        sp => new AuditMigrationsSqlGenerator(
            (IMigrationsSqlGenerator)CreateInstance(sp, inner),
            sp.GetRequiredService<MigrationsSqlGeneratorDependencies>()),
        inner.Lifetime));     // lifetime of the original
}

// Creates the implementation the way the original descriptor would have
private static object CreateInstance(IServiceProvider sp, ServiceDescriptor descriptor)
    => descriptor.ImplementationInstance
       ?? descriptor.ImplementationFactory?.Invoke(sp)
       ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
''', label="tests/Acme.EntityFrameworkCore.Tenancy.Tests/CompositionTests.cs › AuditOptionsExtension")
S.append(p(
    "The trick works because EF calls " + c("ApplyServices") + " in the order in which the "
    "extensions were added; when the audit plugin runs, the provider has already registered "
    "its generator. " + c("CreateInstance") + " reproduces the original registration "
    "exactly: an instance, a factory, or a type whose dependencies (such as Npgsql's "
    "singleton options) " + c("ActivatorUtilities") + " resolves from the container. The "
    "decorator itself only uses the public interface:"))
S += code_block('''
public class AuditMigrationsSqlGenerator : IMigrationsSqlGenerator
{
    // Constructor: (IMigrationsSqlGenerator inner, MigrationsSqlGeneratorDependencies deps)

    public IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        var commands = _inner.Generate(operations, model, options).ToList();   // unchanged
        var builder = new MigrationCommandListBuilder(_dependencies);
        var sql = _dependencies.SqlGenerationHelper;

        // Appended at the end: by then every table of this migration exists
        foreach (var table in operations.OfType<CreateTableOperation>())
        {
            builder.Append("CREATE TRIGGER audit_changes ")
                .Append("AFTER INSERT OR UPDATE OR DELETE ON ")
                .Append(sql.DelimitIdentifier(table.Name, table.Schema))
                .Append(" FOR EACH ROW EXECUTE FUNCTION audit.log_change()")
                .AppendLine(sql.StatementTerminator)
                .EndCommand();
        }

        commands.AddRange(builder.GetCommandList());
        return commands;
    }
}
''', label="tests/.../CompositionTests.cs › AuditMigrationsSqlGenerator (shortened)")
S.append(p(
    "In the application, the two plugins are combined without a single line of glue code:"))
S += code_block('''
services.AddDbContext<ShopContext>((sp, options) => options
    .UseNpgsql(connectionString)
    .UseAcmeTenancy(sp)       // replaces IMigrationsSqlGenerator
    .UseContosoAudit());      // wraps whatever is registered: here Acme's
''', label="Program.cs")
S += callout("important", [
    "Decorators compose with each other in any order, because each one wraps whatever it "
    "finds. A plugin that uses " + c("Replace") + " however discards everything registered "
    "before it, including decorators. A replacing plugin therefore has to be activated "
    "first, and a decorator must check that there is something to decorate and fail with "
    "a clear message otherwise (the provider is missing or was added later).",
    "Decorating requires that the public interface is enough. The tenancy plugin needs "
    "protected hooks inside " + c("CREATE TABLE") + " and therefore derives from Npgsql's "
    "generator. For such cases the fallback described above remains: public logic such as " +
    c("CreatePolicySql") + ", virtual methods, and " + c("ReplaceService") + " in the "
    "application.",
], title="Decorate where possible, replace where necessary")
S += callout("tip", [
    "The test " + c("CompositionTests") + " in the sample contains the complete audit "
    "plugin. It checks that the decorator wraps Acme's generator, that the migration "
    "contains the SQL of both plugins, and that activating the decorator before the "
    "provider fails.",
])

# ------------------------------------------------------------ 6
S += h1("Public API and integration into the application")
S += h2("The Use method")
S.append(p(
    "Users activate the plugin through an extension method on " +
    c("DbContextOptionsBuilder") + ". It fetches the existing extension or creates a new "
    "one and registers it through " + c("IDbContextOptionsBuilderInfrastructure") + ":"))
S += code_block('''
public static DbContextOptionsBuilder UseAcmeTenancy(
    this DbContextOptionsBuilder optionsBuilder,
    Action<TenancyOptionsBuilder>? configure = null)
{
    ArgumentNullException.ThrowIfNull(optionsBuilder);

    var extension = optionsBuilder.Options.FindExtension<TenancyOptionsExtension>()
                    ?? new TenancyOptionsExtension();

    ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
    configure?.Invoke(new TenancyOptionsBuilder(optionsBuilder));
    return optionsBuilder;
}

// Generic overload: method chaining stays typed
public static DbContextOptionsBuilder<TContext> UseAcmeTenancy<TContext>(
    this DbContextOptionsBuilder<TContext> optionsBuilder,
    Action<TenancyOptionsBuilder>? configure = null) where TContext : DbContext
    => (DbContextOptionsBuilder<TContext>)UseAcmeTenancy(
        (DbContextOptionsBuilder)optionsBuilder, configure);
''', label="Infrastructure/TenancyDbContextOptionsBuilderExtensions.cs (excerpt)")
S.append(p(
    "The file lives in the namespace " + c("Microsoft.EntityFrameworkCore") + ". That way "
    "the method can be found without an additional " + c("using") + ", just like " +
    c("UseNpgsql") + ". This is the common convention in the EF ecosystem."))

S += h2("The options builder")
S.append(p(
    "Settings are configured through a dedicated builder. It validates input immediately "
    "and writes every change back as a new extension:"))
S += code_block('''
public class TenancyOptionsBuilder
{
    public TenancyOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        OptionsBuilder = optionsBuilder;
    }

    protected virtual DbContextOptionsBuilder OptionsBuilder { get; }

    public virtual TenancyOptionsBuilder UseSettingName(string settingName)
        => With(s => s with
        {
            SettingName = TenancyIdentifiers.ValidateSettingName(settingName),
        });

    public virtual TenancyOptionsBuilder ApplyQueryFilter(bool apply = true)
        => With(s => s with { ApplyQueryFilter = apply });

    protected virtual TenancyOptionsBuilder With(
        Func<TenancySettings, TenancySettings> change)
    {
        var extension = OptionsBuilder.Options.FindExtension<TenancyOptionsExtension>()
                        ?? new TenancyOptionsExtension();

        ((IDbContextOptionsBuilderInfrastructure)OptionsBuilder)
            .AddOrUpdateExtension(extension.WithSettings(change(extension.Settings)));
        return this;
    }
}
''', label="Infrastructure/TenancyOptionsBuilder.cs (excerpt)")
S += callout("note", [
    "Provider-specific plugins are sometimes activated through the provider's options "
    "builder instead, for example " + c("UseNpgsql(cs, o => o.UseNodaTime())") + ". For "
    "this you write an extension method on " + c("NpgsqlDbContextOptionsBuilder") + " and "
    "reach the actual builder through " +
    c("IRelationalDbContextOptionsBuilderInfrastructure.OptionsBuilder") + ". The PostgreSQL "
    "package uses the simpler route via " + c("DbContextOptionsBuilder") + "; the SQL Server "
    "package in chapter 42 shows the provider-builder style.",
], title="Alternative: activation through the provider builder")

S += h2("Connecting two containers")
S.append(p(
    "Some parts of a plugin need services of the <i>application</i>: the connection "
    "interceptor needs the current tenant, which only the application knows. The internal "
    "EF provider cannot resolve app services, though. The clean solution: such components "
    "live in the app container and are handed to EF via " + c("AddInterceptors") + "."))
S += code_block('''
// Core package: only register the tenant provider in the app container
public static IServiceCollection AddAcmeTenancy<TTenantProvider>(
    this IServiceCollection services,
    ServiceLifetime lifetime = ServiceLifetime.Scoped)
    where TTenantProvider : class, ITenantProvider
{
    services.TryAdd(ServiceDescriptor.Describe(
        typeof(ITenantProvider), typeof(TTenantProvider), lifetime));
    return services;
}

// Provider package: Use overload creates the interceptors from this provider
public static DbContextOptionsBuilder UseAcmeTenancy(
    this DbContextOptionsBuilder optionsBuilder,
    IServiceProvider applicationServices,
    Action<TenancyOptionsBuilder>? configure = null)
{
    var tenants = applicationServices.GetRequiredService<ITenantProvider>();

    return optionsBuilder
        .UseAcmeTenancy(configure)
        .AddInterceptors(
            new TenantConnectionInterceptor(tenants),
            new TenantSaveChangesInterceptor(tenants));
}
''', label="DependencyInjection/… (core), Infrastructure/… (PostgreSQL), excerpts")
S.append(p(
    "With " + c("AddDbContext((sp, o) => ...)") + ", " + c("sp") + " is the scope of the "
    "current request, because EF builds options per scope by default. The tenant provider "
    "and therefore the interceptors see the right tenant. The registration lives in the "
    "core package because it is the same for every provider (chapter 41). For the bridge in "
    "the other direction, an interceptor can read services from the <i>internal</i> "
    "provider via " + c("eventData.Context.GetService<T>()") + "; the connection "
    "interceptor gets the setting name from the singleton options that way (chapter 15)."))
S += callout("important", [
    "With " + c("AddDbContextPool") + ", contexts are reused and receive their options only "
    "once. Scoped interceptors from the first scope would be reused for all later requests, "
    "with the tenant of the first request. For pooling, the tenant provider must be a "
    "singleton that determines the tenant anew on every access, for example via " +
    c("IHttpContextAccessor") + ". Chapter 35 shows this variant, and the same applies to " +
    c("AddDbContextFactory") + ", whose factory is a singleton by default.",
], title="DbContext pooling")
