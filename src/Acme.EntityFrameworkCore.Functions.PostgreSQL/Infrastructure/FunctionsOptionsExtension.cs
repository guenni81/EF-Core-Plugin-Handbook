using Acme.EntityFrameworkCore.Functions.PostgreSQL.Query;
using Acme.EntityFrameworkCore.Functions.PostgreSQL.Query.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace Acme.EntityFrameworkCore.Functions.PostgreSQL.Infrastructure;

/// <summary>
/// Registers the functions plugin: translators, the client-evaluation filter and the two
/// single services a custom SQL node needs (chapter "Custom SQL expression nodes").
/// </summary>
/// <remarks>
/// Has no settings. Uses the internal Npgsql type NpgsqlOptionsExtension (EF1001) to detect
/// UseNpgsql().
/// </remarks>
public class FunctionsOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>Metadata for EF: log fragment, service provider hash, debug info.</summary>
    public virtual DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>Registers the plugin's services in EF's internal service provider.</summary>
    /// <param name="services">The internal service collection.</param>
    public virtual void ApplyServices(IServiceCollection services)
    {
        // Plugin interfaces: multiple registrations, composable with other plugins
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IMethodCallTranslatorPlugin, FunctionsMethodCallTranslatorPlugin>()
            .TryAdd<IAggregateMethodCallTranslatorPlugin, FunctionsAggregateMethodCallTranslatorPlugin>()
            .TryAdd<IEvaluatableExpressionFilterPlugin, FunctionsEvaluatableExpressionFilterPlugin>();

        // Deep extension: the custom SQL node needs a SQL generator that writes it and a
        // nullability processor that knows it. Both are single services: a second plugin that
        // replaces them as well has to derive from these classes (chapter "Registering services")
        services.Replace(ServiceDescriptor
            .Singleton<IQuerySqlGeneratorFactory, FunctionsNpgsqlQuerySqlGeneratorFactory>());
        services.Replace(ServiceDescriptor
            .Scoped<IRelationalParameterBasedSqlProcessorFactory,
                FunctionsNpgsqlParameterBasedSqlProcessorFactory>());
    }

    /// <summary>Ensures the plugin is combined with the Npgsql provider.</summary>
    /// <param name="options">The complete context options.</param>
    /// <exception cref="InvalidOperationException">UseNpgsql() was not called.</exception>
    public virtual void Validate(IDbContextOptions options)
    {
        if (options.FindExtension<NpgsqlOptionsExtension>() is null)
            throw new InvalidOperationException("UseAcmeFunctions() requires UseNpgsql().");
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(FunctionsOptionsExtension extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "AcmeFunctions ";

        // No settings: every context with the plugin can share one service provider
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Acme:Functions"] = "1";
    }
}
