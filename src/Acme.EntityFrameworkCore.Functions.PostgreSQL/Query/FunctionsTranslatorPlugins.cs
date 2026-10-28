using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Functions.PostgreSQL.Query;

/// <summary>Supplies the method call translators of the functions plugin (lifetime: scoped).</summary>
public class FunctionsMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    /// <summary>Creates the plugin and its translators.</summary>
    /// <param name="sql">The factory used to build SQL expression trees.</param>
    /// <param name="typeMappingSource">Resolves type mappings for constants and results.</param>
    public FunctionsMethodCallTranslatorPlugin(
        ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappingSource)
    {
        Translators = [new DistinctFromTranslator(sql, typeMappingSource)];
    }

    /// <summary>The method call translators contributed by the plugin.</summary>
    public virtual IEnumerable<IMethodCallTranslator> Translators { get; }
}

/// <summary>Supplies the aggregate translators of the functions plugin (lifetime: scoped).</summary>
public class FunctionsAggregateMethodCallTranslatorPlugin : IAggregateMethodCallTranslatorPlugin
{
    /// <summary>Creates the plugin and its translators.</summary>
    /// <param name="sql">The factory used to build SQL expression trees.</param>
    /// <param name="typeMappingSource">Resolves type mappings for constants and results.</param>
    public FunctionsAggregateMethodCallTranslatorPlugin(
        ISqlExpressionFactory sql, IRelationalTypeMappingSource typeMappingSource)
    {
        Translators = [new EveryAggregateTranslator(sql, typeMappingSource)];
    }

    /// <summary>The aggregate method call translators contributed by the plugin.</summary>
    public virtual IEnumerable<IAggregateMethodCallTranslator> Translators { get; }
}
