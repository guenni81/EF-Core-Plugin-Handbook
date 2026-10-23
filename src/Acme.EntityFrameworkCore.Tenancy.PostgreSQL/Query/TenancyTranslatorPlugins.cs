using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Query;

/// <summary>Supplies the plugin's method call translators (lifetime: scoped).</summary>
/// <remarks>
/// EF collects all registered <see cref="IMethodCallTranslatorPlugin"/> instances and asks their
/// translators in turn. Being scoped, the plugin may inject scoped services such as
/// <see cref="ISqlExpressionFactory"/>.
/// </remarks>
public class TenancyMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    /// <summary>Creates the plugin and its translators.</summary>
    /// <param name="sql">The factory used to build SQL expression trees.</param>
    /// <param name="typeMappingSource">Resolves type mappings for constants and results.</param>
    /// <param name="options">The plugin's singleton options (e.g. the setting name).</param>
    public TenancyMethodCallTranslatorPlugin(
        ISqlExpressionFactory sql,
        IRelationalTypeMappingSource typeMappingSource,
        ITenancySingletonOptions options)
    {
        Translators =
        [
            new CurrentTenantTranslator(sql, typeMappingSource, options),
            new DistinctFromTranslator(sql, typeMappingSource),
        ];
    }

    /// <summary>The method call translators contributed by the plugin.</summary>
    public virtual IEnumerable<IMethodCallTranslator> Translators { get; }
}

/// <summary>Supplies the plugin's member translators (lifetime: scoped).</summary>
public class TenancyMemberTranslatorPlugin : IMemberTranslatorPlugin
{
    /// <summary>Creates the plugin and its translators.</summary>
    /// <param name="sql">The factory used to build SQL expression trees.</param>
    public TenancyMemberTranslatorPlugin(ISqlExpressionFactory sql)
    {
        Translators = [new TenantIdMemberTranslator(sql)];
    }

    /// <summary>The member translators contributed by the plugin.</summary>
    public virtual IEnumerable<IMemberTranslator> Translators { get; }
}

/// <summary>Supplies the plugin's aggregate translators (lifetime: scoped).</summary>
public class TenancyAggregateMethodCallTranslatorPlugin : IAggregateMethodCallTranslatorPlugin
{
    /// <summary>Creates the plugin and its translators.</summary>
    /// <param name="sql">The factory used to build SQL expression trees.</param>
    /// <param name="typeMappingSource">Resolves type mappings for constants and results.</param>
    public TenancyAggregateMethodCallTranslatorPlugin(
        ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappingSource)
    {
        Translators = [new EveryAggregateTranslator(sql, typeMappingSource)];
    }

    /// <summary>The aggregate method call translators contributed by the plugin.</summary>
    public virtual IEnumerable<IAggregateMethodCallTranslator> Translators { get; }
}
