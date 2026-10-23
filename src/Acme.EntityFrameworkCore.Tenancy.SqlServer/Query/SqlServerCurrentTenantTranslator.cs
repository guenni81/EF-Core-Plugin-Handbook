using System.Reflection;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.SqlServer.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.SqlServer.Query;

/// <summary>
/// Same core API, different translation:
/// EF.Functions.CurrentTenant() -&gt; CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
/// </summary>
/// <remarks>
/// The value is read on the server at execution time, so the translation is not a parameter
/// and the query cache stays tenant-independent.
/// </remarks>
public class SqlServerCurrentTenantTranslator : IMethodCallTranslator
{
    // Resolved once; Translate is called for every method call in every query
    private static readonly MethodInfo CurrentTenantMethod =
        typeof(TenancyDbFunctionsExtensions).GetRuntimeMethod(
            nameof(TenancyDbFunctionsExtensions.CurrentTenant), [typeof(DbFunctions)])!;

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ISqlServerTenancySingletonOptions _options;

    /// <summary>Creates the translator.</summary>
    /// <param name="sql">Factory for SQL expression nodes.</param>
    /// <param name="typeMappingSource">Used to look up the <c>nvarchar</c> mapping for the key.</param>
    /// <param name="options">Provides the configured session key.</param>
    public SqlServerCurrentTenantTranslator(
        ISqlExpressionFactory sql,
        IRelationalTypeMappingSource typeMappingSource,
        ISqlServerTenancySingletonOptions options)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(typeMappingSource);
        ArgumentNullException.ThrowIfNull(options);

        _sql = sql;
        _typeMappingSource = typeMappingSource;
        _options = options;
    }

    /// <summary>
    /// Translates <c>EF.Functions.CurrentTenant()</c>; returns <see langword="null" /> for every
    /// other method so that the next translator gets its turn.
    /// </summary>
    /// <param name="instance">Not used (static extension method).</param>
    /// <param name="method">The method being translated.</param>
    /// <param name="arguments">The translated arguments (only <c>EF.Functions</c>).</param>
    /// <param name="logger">The query logger.</param>
    /// <returns>The SQL expression, or <see langword="null" />.</returns>
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method != CurrentTenantMethod)
            return null;

        var text = _typeMappingSource.FindMapping(typeof(string))!;   // nvarchar -> N'...'

        // SESSION_CONTEXT returns sql_variant, or NULL if the key is missing. The explicit CAST
        // (Convert) is required: sql_variant does not compare implicitly with uniqueidentifier.
        var sessionContext = _sql.Function(
            "SESSION_CONTEXT",
            [_sql.Constant(_options.Settings.SessionKey, text)],
            nullable: true,
            argumentsPropagateNullability: [false],
            typeof(string),
            text);

        return _sql.Convert(sessionContext, typeof(TenantId), SqlServerTenantIdTypeMapping.Default);
    }
}

/// <summary>Registers <see cref="SqlServerCurrentTenantTranslator" /> with EF's query pipeline.</summary>
public class SqlServerTenancyMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    /// <summary>Creates the plugin; the dependencies are passed on to the translator.</summary>
    /// <param name="sql">Factory for SQL expression nodes.</param>
    /// <param name="typeMappingSource">The relational type mapping source.</param>
    /// <param name="options">The tenancy settings.</param>
    public SqlServerTenancyMethodCallTranslatorPlugin(
        ISqlExpressionFactory sql,
        IRelationalTypeMappingSource typeMappingSource,
        ISqlServerTenancySingletonOptions options)
    {
        Translators = [new SqlServerCurrentTenantTranslator(sql, typeMappingSource, options)];
    }

    /// <summary>The translators contributed by this package.</summary>
    public virtual IEnumerable<IMethodCallTranslator> Translators { get; }
}
