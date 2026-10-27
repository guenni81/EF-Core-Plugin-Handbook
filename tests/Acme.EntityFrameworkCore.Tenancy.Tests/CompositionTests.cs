using System.Reflection;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Migrations.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// Decorator of a second, fictional audit plugin: instead of replacing
/// <see cref="IMigrationsSqlGenerator"/>, it wraps whatever generator is registered (the
/// provider's or another plugin's), delegates to it and appends its own commands. This way
/// it composes with other plugins without any code in the application (chapter "Registering
/// services").
/// </summary>
public class AuditMigrationsSqlGenerator : IMigrationsSqlGenerator
{
    private readonly IMigrationsSqlGenerator _inner;
    private readonly MigrationsSqlGeneratorDependencies _dependencies;

    /// <summary>Creates the decorator around the previously registered generator.</summary>
    /// <param name="inner">The generator that was registered before this plugin.</param>
    /// <param name="dependencies">EF's parameter object, used to build the extra commands.</param>
    public AuditMigrationsSqlGenerator(
        IMigrationsSqlGenerator inner, MigrationsSqlGeneratorDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(dependencies);

        _inner = inner;
        _dependencies = dependencies;
    }

    /// <summary>The wrapped generator; exposed for tests.</summary>
    public IMigrationsSqlGenerator Inner => _inner;

    /// <summary>Generates the inner SQL unchanged, then an audit trigger per new table.</summary>
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

/// <summary>Options extension of the fictional audit plugin.</summary>
public class AuditOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>
    /// The decorator trick: look up the generator registered so far, replace its descriptor
    /// with one that creates the original implementation and wraps it in the decorator.
    /// </summary>
    public void ApplyServices(IServiceCollection services)
    {
        // EF calls ApplyServices in the order the extensions were added, so the provider's
        // generator (or that of a plugin activated earlier) is already registered here.
        var inner = services.LastOrDefault(d => d.ServiceType == typeof(IMigrationsSqlGenerator))
            ?? throw new InvalidOperationException(
                "UseContosoAudit() must be called after the database provider, e.g. UseNpgsql().");

        services.Replace(ServiceDescriptor.Describe(
            typeof(IMigrationsSqlGenerator),
            sp => new AuditMigrationsSqlGenerator(
                (IMigrationsSqlGenerator)CreateInstance(sp, inner),
                sp.GetRequiredService<MigrationsSqlGeneratorDependencies>()),
            inner.Lifetime));     // keep the lifetime of the original registration
    }

    /// <summary>Creates the implementation the way the original descriptor would have.</summary>
    private static object CreateInstance(IServiceProvider sp, ServiceDescriptor descriptor)
        => descriptor.ImplementationInstance
           ?? descriptor.ImplementationFactory?.Invoke(sp)
           ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using ContosoAudit ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["ContosoAudit"] = "1";
    }
}

/// <summary>Use method of the fictional audit plugin.</summary>
public static class AuditDbContextOptionsBuilderExtensions
{
    /// <summary>Activates the audit plugin; call it after the database provider.</summary>
    public static DbContextOptionsBuilder UseContosoAudit(this DbContextOptionsBuilder builder)
    {
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            builder.Options.FindExtension<AuditOptionsExtension>() ?? new AuditOptionsExtension());
        return builder;
    }
}

/// <summary>
/// Combining two plugins that both need <see cref="IMigrationsSqlGenerator"/>: the tenancy
/// plugin replaces it, the audit plugin decorates whatever it finds.
/// </summary>
public class CompositionTests
{
    [Fact]
    public void Decorator_wraps_the_generator_of_the_plugin_activated_before()
    {
        using var ctx = Contexts.Create(configure: o => o.UseContosoAudit());

        var generator = Assert.IsType<AuditMigrationsSqlGenerator>(
            ctx.GetService<IMigrationsSqlGenerator>());
        Assert.IsType<TenancyMigrationsSqlGenerator>(generator.Inner);
    }

    [Fact]
    public void Combined_plugins_emit_sql_of_both()
    {
        using var ctx = Contexts.Create(configure: o => o.UseContosoAudit());

        var sql = Contexts.MigrationSql(null, ctx);

        Assert.Contains(sql, s => s.StartsWith("CREATE POLICY tenant_isolation ON \"Orders\""));
        Assert.Contains(sql, s => s.StartsWith("CREATE TRIGGER audit_changes")
                                  && s.Contains("ON \"Orders\""));
    }

    [Fact]
    public void Decorator_requires_the_provider_to_be_registered_first()
    {
        var builder = new DbContextOptionsBuilder<TestContext>();
        builder.UseContosoAudit();                              // too early: no provider yet
        builder.UseNpgsql("Host=localhost;Database=unused");

        Assert.Throws<InvalidOperationException>(() => new TestContext(builder.Options, null));
    }
}

/// <summary>
/// Options extension of a fictional second plugin that only registers services. The type
/// parameter makes every fictional plugin its own extension type: EF keeps one extension per
/// type, so two plugins sharing one extension class would replace each other.
/// </summary>
public sealed class FakePluginExtension<TPlugin> : IDbContextOptionsExtension
{
    private readonly Action<IServiceCollection> _applyServices;
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>Creates the extension.</summary>
    /// <param name="applyServices">The plugin's registrations.</param>
    public FakePluginExtension(Action<IServiceCollection> applyServices)
        => _applyServices = applyServices;

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services) => _applyServices(services);

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => typeof(TPlugin).Name + " ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo[typeof(TPlugin).Name] = "1";
    }
}

/// <summary>Use method for the fictional plugins of the composition tests.</summary>
public static class FakePluginDbContextOptionsBuilderExtensions
{
    /// <summary>Activates a fictional plugin with the given registrations.</summary>
    public static DbContextOptionsBuilder UseFakePlugin<TPlugin>(
        this DbContextOptionsBuilder builder, Action<IServiceCollection> applyServices)
    {
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            new FakePluginExtension<TPlugin>(applyServices));
        return builder;
    }
}

/// <summary>
/// The composition matrix of chapter "Registering services": which combinations of two plugins
/// work, which depend on the activation order, and which conflict.
/// </summary>
public class CompositionMatrixTests
{
    // Translates string.ToUpper() into a function of the given name
    private sealed class UpperTranslator : IMethodCallTranslator
    {
        private readonly ISqlExpressionFactory _sql;
        private readonly string _function;

        public UpperTranslator(ISqlExpressionFactory sql, string function)
        {
            _sql = sql;
            _function = function;
        }

        public SqlExpression? Translate(
            SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
            => method.DeclaringType == typeof(string) && method.Name == nameof(string.ToUpper)
               && arguments.Count == 0 && instance is not null
                ? _sql.Function(_function, [instance], nullable: true,
                    argumentsPropagateNullability: [true], typeof(string), instance.TypeMapping)
                : null;
    }

    private sealed class PluginATranslators : IMethodCallTranslatorPlugin
    {
        public PluginATranslators(ISqlExpressionFactory sql)
            => Translators = [new UpperTranslator(sql, "upper_a")];

        public IEnumerable<IMethodCallTranslator> Translators { get; }
    }

    private sealed class PluginBTranslators : IMethodCallTranslatorPlugin
    {
        public PluginBTranslators(ISqlExpressionFactory sql)
            => Translators = [new UpperTranslator(sql, "upper_b")];

        public IEnumerable<IMethodCallTranslator> Translators { get; }
    }

    private static void AddTranslatorsA(IServiceCollection services)
        => new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IMethodCallTranslatorPlugin, PluginATranslators>();

    private static void AddTranslatorsB(IServiceCollection services)
        => new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IMethodCallTranslatorPlugin, PluginBTranslators>();

    private static string UpperSql(DbContext ctx)
        => ((TestContext)ctx).Customers.Where(c => c.Name.ToUpper() == "X").ToQueryString();

    [Fact]
    public void Translator_plugins_take_precedence_over_the_provider()
    {
        using var ctx = Contexts.Create(configure: o => o.UseFakePlugin<PluginATranslators>(AddTranslatorsA));

        Assert.Contains("upper_a(", UpperSql(ctx));                 // not Npgsql's upper()
    }

    [Fact]
    public void For_the_same_method_the_translator_plugin_activated_first_wins()
    {
        using var ab = Contexts.Create(configure: o => o
            .UseFakePlugin<PluginATranslators>(AddTranslatorsA)
            .UseFakePlugin<PluginBTranslators>(AddTranslatorsB));
        using var ba = Contexts.Create(configure: o => o
            .UseFakePlugin<PluginBTranslators>(AddTranslatorsB)
            .UseFakePlugin<PluginATranslators>(AddTranslatorsA));

        Assert.Contains("upper_a(", UpperSql(ab));
        Assert.Contains("upper_b(", UpperSql(ba));
    }

    private sealed class LoggingConventionSetPlugin : IConventionSetPlugin
    {
        private readonly List<string> _log;
        private readonly string _name;

        public LoggingConventionSetPlugin(List<string> log, string name)
        {
            _log = log;
            _name = name;
        }

        public ConventionSet ModifyConventions(ConventionSet conventionSet)
        {
            _log.Add(_name);
            return conventionSet;
        }
    }

    [Fact]
    public void Convention_set_plugins_run_in_activation_order()
    {
        var log = new List<string>();
        using var ctx = Contexts.Create(configure: o => o
            .UseFakePlugin<PluginBTranslators>(s => s.AddScoped<IConventionSetPlugin>(
                _ => new LoggingConventionSetPlugin(log, "second plugin")))
            .UseFakePlugin<PluginATranslators>(s => s.AddScoped<IConventionSetPlugin>(
                _ => new LoggingConventionSetPlugin(log, "third plugin"))));

        _ = ctx.Model;

        // The tenancy plugin (activated first) is not logged; the others follow in order
        Assert.Equal(["second plugin", "third plugin"], log);
    }

    private sealed class OtherMigrationsSqlGenerator : NpgsqlMigrationsSqlGenerator
    {
        public OtherMigrationsSqlGenerator(
            MigrationsSqlGeneratorDependencies dependencies,
            INpgsqlSingletonOptions npgsqlSingletonOptions)
            : base(dependencies, npgsqlSingletonOptions)
        {
        }
    }

    // Without the tenancy plugin, which would replace the generator itself
    private sealed class EmptyContext : DbContext
    {
        public EmptyContext(DbContextOptions<EmptyContext> options)
            : base(options)
        {
        }
    }

    [Fact]
    public void Single_service_added_with_TryAdd_depends_on_the_activation_order()
    {
        static void TryAddGenerator(IServiceCollection services)
            => services.TryAddScoped<IMigrationsSqlGenerator, OtherMigrationsSqlGenerator>();

        var before = new DbContextOptionsBuilder<EmptyContext>();
        before.UseFakePlugin<OtherMigrationsSqlGenerator>(TryAddGenerator);
        before.UseNpgsql("Host=localhost;Database=unused").EnableServiceProviderCaching(false);

        var after = new DbContextOptionsBuilder<EmptyContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .EnableServiceProviderCaching(false);
        after.UseFakePlugin<OtherMigrationsSqlGenerator>(TryAddGenerator);

        using var first = new EmptyContext(before.Options);
        using var last = new EmptyContext(after.Options);

        // Before the provider the plugin wins; after it, the provider's TryAdd came first
        Assert.IsType<OtherMigrationsSqlGenerator>(first.GetService<IMigrationsSqlGenerator>());
        Assert.IsNotType<OtherMigrationsSqlGenerator>(last.GetService<IMigrationsSqlGenerator>());
    }

    [Fact]
    public void Replace_wins_regardless_of_the_activation_order()
    {
        var before = new DbContextOptionsBuilder<TestContext>()
            .UseAcmeTenancy()
            .UseNpgsql("Host=localhost;Database=unused")
            .EnableServiceProviderCaching(false);

        using var ctx = new TestContext((DbContextOptions<TestContext>)before.Options, null);

        Assert.IsType<TenancyMigrationsSqlGenerator>(ctx.GetService<IMigrationsSqlGenerator>());
    }

    [Fact]
    public void Of_two_replacements_the_last_one_wins_and_the_first_plugin_loses_its_sql()
    {
        using var ctx = Contexts.Create(configure: o => o
            .UseFakePlugin<OtherMigrationsSqlGenerator>(s => s.Replace(
                ServiceDescriptor.Scoped<IMigrationsSqlGenerator, OtherMigrationsSqlGenerator>())));

        Assert.IsType<OtherMigrationsSqlGenerator>(ctx.GetService<IMigrationsSqlGenerator>());

        // No error, no warning: the tenancy plugin's policies are simply gone
        Assert.DoesNotContain(Contexts.MigrationSql(null, ctx), s => s.StartsWith("CREATE POLICY"));
    }

    [Fact]
    public void ReplaceService_of_the_application_wins_over_every_plugin()
    {
        using var ctx = Contexts.Create(configure: o => o
            .ReplaceService<IMigrationsSqlGenerator, OtherMigrationsSqlGenerator>());

        Assert.IsType<OtherMigrationsSqlGenerator>(ctx.GetService<IMigrationsSqlGenerator>());
    }

    [Fact]
    public void Replacement_activated_after_a_decorator_discards_it_silently()
    {
        var builder = new DbContextOptionsBuilder<TestContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseContosoAudit()                      // decorates Npgsql's generator ...
            .UseAcmeTenancy()                       // ... which Replace then throws away
            .EnableServiceProviderCaching(false);

        using var ctx = new TestContext((DbContextOptions<TestContext>)builder.Options, null);

        Assert.IsType<TenancyMigrationsSqlGenerator>(ctx.GetService<IMigrationsSqlGenerator>());
        Assert.DoesNotContain(Contexts.MigrationSql(null, ctx), s => s.StartsWith("CREATE TRIGGER"));
    }

    private static string DistinctFromSql(DbContext ctx)
        => ((TestContext)ctx).Customers
            .Select(c => EF.Functions.IsDistinctFrom(c.Name, (string?)null))
            .ToQueryString();

    [Fact]
    public void Second_plugin_replacing_the_sql_generator_silently_drops_custom_nodes()
    {
        // Stands for any other plugin that replaces the generator with its own Npgsql subclass
        using var ctx = Contexts.Create(configure: o => o
            .UseFakePlugin<NpgsqlQuerySqlGeneratorFactory>(s => s.Replace(ServiceDescriptor
                .Singleton<IQuerySqlGeneratorFactory, NpgsqlQuerySqlGeneratorFactory>())));

        // The tenancy plugin's node reaches a generator that does not know it
        Assert.DoesNotContain("DISTINCT FROM", DistinctFromSql(ctx));
    }

    [Fact]
    public void Second_plugin_replacing_the_parameter_based_processor_breaks_custom_nodes()
    {
        using var ctx = Contexts.Create(configure: o => o
            .UseFakePlugin<NpgsqlParameterBasedSqlProcessorFactory>(s => s.Replace(ServiceDescriptor
                .Scoped<IRelationalParameterBasedSqlProcessorFactory,
                    NpgsqlParameterBasedSqlProcessorFactory>())));

        Assert.Throws<InvalidOperationException>(() => DistinctFromSql(ctx));
    }

    private sealed class LoggingSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly List<string> _log;
        private readonly string _name;
        private readonly bool _suppress;

        public LoggingSaveChangesInterceptor(List<string> log, string name, bool suppress = false)
        {
            _log = log;
            _name = name;
            _suppress = suppress;
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            _log.Add(_name);
            // The last one suppresses the actual save, so no database is needed
            return _suppress ? InterceptionResult<int>.SuppressWithResult(0) : result;
        }
    }

    [Fact]
    public void Injected_interceptors_run_first_then_those_of_AddInterceptors_in_order()
    {
        var log = new List<string>();
        using var ctx = Contexts.Create(configure: o => o
            .AddInterceptors(
                new LoggingSaveChangesInterceptor(log, "application 1"),
                new LoggingSaveChangesInterceptor(log, "application 2", suppress: true))
            .UseFakePlugin<LoggingSaveChangesInterceptor>(s => s.AddScoped<IInterceptor>(
                _ => new LoggingSaveChangesInterceptor(log, "injected"))));

        ctx.Products.Add(new Product { Id = 1 });
        ctx.SaveChanges();

        Assert.Equal(["injected", "application 1", "application 2"], log);
    }

    [Fact]
    public void Same_options_extension_type_added_twice_keeps_only_the_last()
    {
        var builder = new DbContextOptionsBuilder()
            .UseFakePlugin<PluginATranslators>(AddTranslatorsA)
            .UseFakePlugin<PluginATranslators>(AddTranslatorsB);

        Assert.Single(builder.Options.Extensions.OfType<FakePluginExtension<PluginATranslators>>());
    }
}
