using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.EntityFrameworkCore.Tenancy.Tests;

// The smallest complete plugin: EF.Functions.Reverse(text) becomes PostgreSQL's reverse().
// Each piece is explained in detail later in the handbook: options extension (chapter "The
// options extension"), service registration (chapter "Registering services"), Use method
// (chapter "Public API and integration into the application"), translators (chapter
// "Translating methods and members").

/// <summary>1. The function users call in LINQ. It is never executed.</summary>
public static class ReverseDbFunctionsExtensions
{
    /// <summary>Reverses a string in the database.</summary>
    public static string Reverse(this DbFunctions _, string value)
        => throw new InvalidOperationException("Reverse can only be used in LINQ queries.");
}

/// <summary>2. The translator turns the method call into a SQL function.</summary>
public class ReverseTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo ReverseMethod =
        typeof(ReverseDbFunctionsExtensions).GetMethod(nameof(ReverseDbFunctionsExtensions.Reverse))!;

    private readonly ISqlExpressionFactory _sql;

    /// <summary>Creates the translator.</summary>
    public ReverseTranslator(ISqlExpressionFactory sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        _sql = sql;
    }

    /// <summary>Translates Reverse; returns null for every other method.</summary>
    public SqlExpression? Translate(
        SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        => method == ReverseMethod
            ? _sql.Function("reverse", [arguments[1]], nullable: true,
                argumentsPropagateNullability: [true], typeof(string),
                arguments[1].TypeMapping)
            : null;
}

/// <summary>3. The plugin hands the translator to EF; plugins can be registered multiple times.</summary>
public class ReverseTranslatorPlugin : IMethodCallTranslatorPlugin
{
    /// <summary>Creates the plugin; EF injects the scoped SQL expression factory.</summary>
    public ReverseTranslatorPlugin(ISqlExpressionFactory sql)
    {
        Translators = [new ReverseTranslator(sql)];
    }

    /// <inheritdoc />
    public IEnumerable<IMethodCallTranslator> Translators { get; }
}

/// <summary>4. The options extension registers the plugin in EF's internal service provider.</summary>
public class ReverseOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
        => new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IMethodCallTranslatorPlugin, ReverseTranslatorPlugin>();

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

        public override string LogFragment => "using Reverse ";

        // No settings: every context with this extension can share the internal provider
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Reverse"] = "1";
    }
}

/// <summary>5. The Use method activates the plugin.</summary>
public static class ReverseDbContextOptionsBuilderExtensions
{
    /// <summary>Adds the Reverse plugin to the options.</summary>
    public static DbContextOptionsBuilder UseReverse(this DbContextOptionsBuilder builder)
    {
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            builder.Options.FindExtension<ReverseOptionsExtension>()
            ?? new ReverseOptionsExtension());
        return builder;
    }
}

/// <summary>
/// The minimal plugin from chapter "EF Core's architecture from a plugin's point of view"
/// works end to end.
/// </summary>
public class MinimalPluginTests
{
    [Fact]
    public void Reverse_is_translated_to_sql()
    {
        using var ctx = Contexts.Create(configure: o => o.UseReverse());

        var sql = ctx.Customers
            .Where(c => EF.Functions.Reverse(c.Name) == "abc")
            .ToQueryString();

        Assert.Contains("reverse(c.\"Name\")", sql);
    }

    [Fact]
    public void Without_the_plugin_the_query_cannot_be_translated()
    {
        using var ctx = Contexts.Create();

        Assert.Throws<InvalidOperationException>(() => ctx.Customers
            .Where(c => EF.Functions.Reverse(c.Name) == "abc")
            .ToQueryString());
    }
}
