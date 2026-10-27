using System.Reflection;
using Acme.EntityFrameworkCore.Tenancy.Interceptors;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

/// <summary>
/// The "What happens if ...?" failure cases of the handbook: each plausible wrong variant next
/// to the correct one, so that the described behavior of EF Core 10 stays verified.
/// </summary>
public class FailureCaseTests
{
    // ------------------------------------------------ Chapter "The options extension"

    /// <summary>WRONG: a model-relevant setting that is missing from the hash.</summary>
    private sealed class HashlessExtension : IDbContextOptionsExtension
    {
        private DbContextOptionsExtensionInfo? _info;

        public HashlessExtension(string settingName) => SettingName = settingName;

        public string SettingName { get; }

        public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

        public void ApplyServices(IServiceCollection services)
            => new EntityFrameworkServicesBuilder(services)
                .TryAdd<ISingletonOptions, SettingOptions>(p => p.GetRequiredService<SettingOptions>())
                .TryAddProviderSpecificServices(m => m.TryAddSingleton<SettingOptions, SettingOptions>());

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

            public override string LogFragment => "";

            // Copied from a plugin without settings, the setting was added later
            public override int GetServiceProviderHashCode() => 0;

            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
                => other is ExtensionInfo;

            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            {
            }
        }
    }

    private sealed class SettingOptions : ISingletonOptions
    {
        public string SettingName { get; private set; } = "";

        public void Initialize(IDbContextOptions options)
            => SettingName = options.FindExtension<HashlessExtension>()!.SettingName;

        public void Validate(IDbContextOptions options)
        {
        }
    }

    private sealed class PlainContext : DbContext
    {
        public PlainContext(DbContextOptions<PlainContext> options)
            : base(options)
        {
        }
    }

    [Fact]
    public void Setting_missing_from_the_hash_shares_the_provider_of_another_configuration()
    {
        PlainContext Create(string settingName)
        {
            var builder = new DbContextOptionsBuilder<PlainContext>()
                .UseNpgsql("Host=localhost;Database=unused")
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                new HashlessExtension(settingName));
            return new PlainContext(builder.Options);
        }

        var unique = Guid.NewGuid().ToString("N");          // not shared with earlier runs
        using var a = Create("app.a_" + unique);
        _ = a.GetService<SettingOptions>();
        using var b = Create("app.b_" + unique);

        // Context b is configured with app.b, but runs on the provider built for app.a
        Assert.Equal("app.a_" + unique, b.GetService<SettingOptions>().SettingName);
    }

    // ------------------------------------------------ Chapter "Registering services"

    [Fact]
    public void Plugin_interface_registered_as_singleton_instead_of_scoped()
    {
        // WRONG: AddSingleton instead of the services builder, which would use Scoped
        using var ctx = Contexts.Create(configure: o => o.UseFakePlugin<ReverseTranslatorPlugin>(
            s => s.AddSingleton<IMethodCallTranslatorPlugin, ReverseTranslatorPlugin>()));

        // The singleton is created in the root scope; the scoped ISqlExpressionFactory it needs
        // cannot be resolved there. The error names neither the plugin nor the lifetime.
        Assert.Throws<ArgumentNullException>(() => ctx.Customers
            .Where(c => EF.Functions.Reverse(c.Name) == "abc")
            .ToQueryString());
    }

    // ------------------------------------------------ Chapter "Annotations and Fluent API"

    /// <summary>Enables RLS for every tenant-owned entity at ModelFinalizing.</summary>
    private sealed class EnableRlsConvention : IModelFinalizingConvention
    {
        private readonly bool _throughBuilder;

        public EnableRlsConvention(bool throughBuilder) => _throughBuilder = throughBuilder;

        public void ProcessModelFinalizing(
            IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (var entityType in modelBuilder.Metadata.GetEntityTypes()
                         .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType)))
            {
                if (_throughBuilder)
                    entityType.Builder.HasAnnotation(RlsAnnotationNames.Enabled, true);
                else
                    entityType.SetAnnotation(RlsAnnotationNames.Enabled, true);    // WRONG
            }
        }
    }

    private sealed class EnableRlsPlugin : IConventionSetPlugin
    {
        private readonly bool _throughBuilder;

        public EnableRlsPlugin(bool throughBuilder) => _throughBuilder = throughBuilder;

        public ConventionSet ModifyConventions(ConventionSet conventionSet)
        {
            conventionSet.Add(new EnableRlsConvention(_throughBuilder));
            return conventionSet;
        }
    }

    private static object? RlsEnabledAfterOptOut(bool throughBuilder)
    {
        using var ctx = Contexts.Create(
            model: m => m.Entity<Order>().HasNoRowLevelSecurity(),
            configure: o => o.UseFakePlugin<EnableRlsPlugin>(
                s => s.AddScoped<IConventionSetPlugin>(_ => new EnableRlsPlugin(throughBuilder))));

        return ctx.DesignModel().FindEntityType(typeof(Order))![RlsAnnotationNames.Enabled];
    }

    [Fact]
    public void Convention_writing_to_the_metadata_overrides_the_explicit_opt_out()
        => Assert.Equal(true, RlsEnabledAfterOptOut(throughBuilder: false));

    [Fact]
    public void Convention_writing_through_the_builder_respects_the_explicit_opt_out()
        => Assert.Equal(false, RlsEnabledAfterOptOut(throughBuilder: true));

    // ------------------------------------------------ Chapter "Conventions"

    private const string ColumnAnnotation = "Test:TenantColumn";

    /// <summary>WRONG: reads the column name as soon as the property is added.</summary>
    private sealed class EarlyColumnConvention : IPropertyAddedConvention
    {
        public void ProcessPropertyAdded(
            IConventionPropertyBuilder propertyBuilder, IConventionContext<IConventionPropertyBuilder> context)
        {
            if (propertyBuilder.Metadata.Name == nameof(ITenantOwned.TenantId))
                propertyBuilder.HasAnnotation(ColumnAnnotation, propertyBuilder.Metadata.GetColumnName());
        }
    }

    private sealed class LateColumnConvention : IModelFinalizingConvention
    {
        public void ProcessModelFinalizing(
            IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (var property in modelBuilder.Metadata.GetEntityTypes()
                         .Select(e => e.FindProperty(nameof(ITenantOwned.TenantId)))
                         .OfType<IConventionProperty>())
                property.Builder.HasAnnotation(ColumnAnnotation, property.GetColumnName());
        }
    }

    private sealed class ColumnConventionPlugin : IConventionSetPlugin
    {
        private readonly IConvention _convention;

        public ColumnConventionPlugin(IConvention convention) => _convention = convention;

        public ConventionSet ModifyConventions(ConventionSet conventionSet)
        {
            conventionSet.Add(_convention);
            return conventionSet;
        }
    }

    private static object? StoredColumnName(IConvention convention)
    {
        using var ctx = Contexts.Create(
            model: m => m.Entity<Order>().Property(o => o.TenantId).HasColumnName("tenant"),
            configure: o => o.UseFakePlugin<ColumnConventionPlugin>(
                s => s.AddScoped<IConventionSetPlugin>(_ => new ColumnConventionPlugin(convention))));

        return ctx.DesignModel().FindEntityType(typeof(Order))!
            .FindProperty(nameof(Order.TenantId))![ColumnAnnotation];
    }

    [Fact]
    public void Convention_reading_the_column_name_too_early_keeps_the_default_name()
        => Assert.Equal("TenantId", StoredColumnName(new EarlyColumnConvention()));

    [Fact]
    public void Convention_at_ModelFinalizing_sees_the_configured_name()
        => Assert.Equal("tenant", StoredColumnName(new LateColumnConvention()));

    // ------------------------------------------------ Chapter "Translating methods and members"

    private static readonly MethodInfo EmptyAsNullMethod =
        typeof(FailureCaseFunctions).GetMethod(nameof(FailureCaseFunctions.EmptyAsNull))!;

    private sealed class EmptyAsNullTranslator : IMethodCallTranslator
    {
        private readonly ISqlExpressionFactory _sql;
        private readonly bool _nullable;
        private readonly bool _propagates;

        public EmptyAsNullTranslator(ISqlExpressionFactory sql, bool nullable, bool propagates)
        {
            _sql = sql;
            _nullable = nullable;
            _propagates = propagates;
        }

        public SqlExpression? Translate(
            SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
            => method == EmptyAsNullMethod
                ? _sql.Function("nullif", [arguments[1], _sql.Constant("", arguments[1].TypeMapping)],
                    nullable: _nullable, argumentsPropagateNullability: [_propagates, false],
                    typeof(string), arguments[1].TypeMapping)
                : null;
    }

    private sealed class EmptyAsNullPlugin : IMethodCallTranslatorPlugin
    {
        public EmptyAsNullPlugin(ISqlExpressionFactory sql, bool nullable, bool propagates)
            => Translators = [new EmptyAsNullTranslator(sql, nullable, propagates)];

        public IEnumerable<IMethodCallTranslator> Translators { get; }
    }

    private static string EmptyNameSql(bool nullable, bool propagates = true)
    {
        using var ctx = Contexts.Create(configure: o => o.UseFakePlugin<EmptyAsNullPlugin>(
            s => s.AddScoped<IMethodCallTranslatorPlugin>(
                p => new EmptyAsNullPlugin(p.GetRequiredService<ISqlExpressionFactory>(), nullable, propagates))));

        return ctx.Customers.Where(c => EF.Functions.EmptyAsNull(c.Name) == null).ToQueryString();
    }

    [Fact]
    public void Nullable_function_with_a_propagating_argument_is_treated_as_never_null()
    {
        // WRONG: nullif can return NULL although its argument is not NULL. With a propagating
        // argument, EF derives the nullability from that (required) column only.
        Assert.EndsWith("WHERE FALSE", EmptyNameSql(nullable: true, propagates: true));
        Assert.EndsWith("WHERE FALSE", EmptyNameSql(nullable: false, propagates: true));
    }

    [Fact]
    public void Nullable_function_without_propagating_arguments_keeps_the_null_check()
        => Assert.EndsWith("WHERE nullif(c.\"Name\", '') IS NULL",
            EmptyNameSql(nullable: true, propagates: false));

    // ------------------------------------------------ Chapter "Preventing client-side evaluation"

    [Fact]
    public void Function_without_data_reference_and_without_filter_plugin()
    {
        using var ctx = Contexts.Create(configure: o => o.UseFakePlugin<ServerTimePlugin>(
            s => s.AddScoped<IMethodCallTranslatorPlugin, ServerTimePlugin>()));

        // WRONG: no evaluatable filter knows the class, so the funcletizer calls the method
        var ex = Assert.Throws<InvalidOperationException>(() => ctx.Customers
            .Where(c => EF.Functions.ServerTime() > DateTime.MinValue).ToQueryString());
        Assert.Contains("evaluate a LINQ query parameter expression", ex.Message);
        Assert.Equal("ServerTime can only be used in LINQ queries.", ex.InnerException!.Message);

        // Right: the class carries the attribute that the core package's filter recognizes
        Assert.Contains("now()", ctx.Customers
            .Where(c => EF.Functions.MarkedServerTime() > DateTime.MinValue).ToQueryString());
    }

    private sealed class ServerTimePlugin : IMethodCallTranslatorPlugin
    {
        public ServerTimePlugin(ISqlExpressionFactory sql, IRelationalTypeMappingSource mappings)
            => Translators = [new ServerTimeTranslator(sql, mappings)];

        public IEnumerable<IMethodCallTranslator> Translators { get; }
    }

    private sealed class ServerTimeTranslator : IMethodCallTranslator
    {
        private readonly ISqlExpressionFactory _sql;
        private readonly IRelationalTypeMappingSource _mappings;

        public ServerTimeTranslator(ISqlExpressionFactory sql, IRelationalTypeMappingSource mappings)
        {
            _sql = sql;
            _mappings = mappings;
        }

        public SqlExpression? Translate(
            SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
            => method.Name is nameof(FailureCaseFunctions.ServerTime)
                   or nameof(MarkedFailureCaseFunctions.MarkedServerTime)
                ? _sql.Function("now", [], nullable: false, argumentsPropagateNullability: [],
                    typeof(DateTime), _mappings.FindMapping(typeof(DateTime)))
                : null;
    }

    // ------------------------------------------------ Chapter "Interceptors"

    private sealed class AsyncOnlyInterceptor : SaveChangesInterceptor
    {
        public bool Called { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            return ValueTask.FromResult(result);
        }
    }

    // Suppresses the actual save in both variants, so no database is needed
    private sealed class SuppressingInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
            => InterceptionResult<int>.SuppressWithResult(0);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
    }

    [Fact]
    public async Task Interceptor_with_only_the_async_variant_misses_synchronous_saves()
    {
        var interceptor = new AsyncOnlyInterceptor();
        using var ctx = Contexts.Create(configure: o => o.AddInterceptors(interceptor, new SuppressingInterceptor()));
        ctx.Products.Add(new Product { Id = 1 });

        ctx.SaveChanges();
        Assert.False(interceptor.Called);

        await ctx.SaveChangesAsync();
        Assert.True(interceptor.Called);
    }

    // ------------------------------------------------ Chapter "Declarative SQL from annotations"

    [Fact]
    public void Differ_rejects_the_runtime_model_with_a_clear_message()
    {
        using var ctx = Contexts.Create();

        var ex = Assert.Throws<InvalidOperationException>(() => ctx
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, ctx.Model.GetRelationalModel()));
        Assert.Contains("IDesignTimeModel", ex.Message);
    }

    [Fact]
    public void Table_annotations_of_the_runtime_model_lack_the_design_time_annotations()
    {
        using var ctx = Contexts.Create();

        static object? RlsEnabled(IModel model)
            => model.GetRelationalModel().FindTable("Orders", null)![RlsAnnotationNames.Enabled];

        // WRONG: plugin code that reads its schema annotations from context.Model sees nothing
        Assert.Null(RlsEnabled(ctx.Model));
        Assert.Equal(true, RlsEnabled(ctx.GetService<IDesignTimeModel>().Model));
    }

    // ------------------------------------------------ Chapter "State, lifetimes and thread safety"

    private sealed class RequestCounter
    {
        public int Queries { get; set; }
    }

    private sealed class ResettableRequestCounter : IResettableService
    {
        public int Queries { get; set; }

        public void ResetState() => Queries = 0;

        public Task ResetStateAsync(CancellationToken cancellationToken = default)
        {
            ResetState();
            return Task.CompletedTask;
        }
    }

    private static PooledDbContextFactory<PlainContext> PooledFactory(Action<IServiceCollection> services)
    {
        var builder = new DbContextOptionsBuilder<PlainContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .EnableServiceProviderCaching(false);
        builder.UseFakePlugin<RequestCounter>(services);
        return new PooledDbContextFactory<PlainContext>(builder.Options);
    }

    [Fact]
    public void Scoped_state_survives_into_the_next_request_with_pooling()
    {
        var factory = PooledFactory(s => s.AddScoped<RequestCounter>());     // WRONG with pooling

        using (var first = factory.CreateDbContext())
            first.GetService<RequestCounter>().Queries = 3;

        using var second = factory.CreateDbContext();
        Assert.Equal(3, second.GetService<RequestCounter>().Queries);
    }

    [Fact]
    public void Scoped_state_registered_as_resettable_is_reset_for_the_next_request()
    {
        var factory = PooledFactory(s => s
            .AddScoped<ResettableRequestCounter>()
            .AddScoped<IResettableService>(p => p.GetRequiredService<ResettableRequestCounter>()));

        using (var first = factory.CreateDbContext())
            first.GetService<ResettableRequestCounter>().Queries = 3;

        using var second = factory.CreateDbContext();
        Assert.Equal(0, second.GetService<ResettableRequestCounter>().Queries);
    }
}

/// <summary>Database functions of the failure case tests; the class has no marker attribute.</summary>
public static class FailureCaseFunctions
{
    /// <summary>NULL for an empty string (SQL: nullif).</summary>
    public static string? EmptyAsNull(this DbFunctions _, string value)
        => throw new InvalidOperationException("EmptyAsNull can only be used in LINQ queries.");

    /// <summary>The database's current time; no argument with a data reference.</summary>
    public static DateTime ServerTime(this DbFunctions _)
        => throw new InvalidOperationException("ServerTime can only be used in LINQ queries.");
}

/// <summary>The same function, recognized by the core package's evaluatable filter.</summary>
[TenancyDbFunctions]
public static class MarkedFailureCaseFunctions
{
    /// <summary>The database's current time.</summary>
    public static DateTime MarkedServerTime(this DbFunctions _)
        => throw new InvalidOperationException("MarkedServerTime can only be used in LINQ queries.");
}
