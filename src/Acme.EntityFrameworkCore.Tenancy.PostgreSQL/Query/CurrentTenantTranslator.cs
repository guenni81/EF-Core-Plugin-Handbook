using System.Reflection;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Infrastructure;
using Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Query;

/// <summary>
/// EF.Functions.CurrentTenant()
///   -&gt; CAST(nullif(current_setting('app.tenant_id', TRUE), '') AS uuid)
/// </summary>
/// <remarks>
/// The tenant is read on the server from the session setting that the connection interceptor
/// sets, so the value never becomes part of the SQL text or the query cache key.
/// </remarks>
public class CurrentTenantTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo CurrentTenantMethod =
        typeof(TenancyDbFunctionsExtensions).GetRuntimeMethod(
            nameof(TenancyDbFunctionsExtensions.CurrentTenant), [typeof(DbFunctions)])!;

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ITenancySingletonOptions _options;

    /// <summary>Creates the translator.</summary>
    /// <param name="sql">The factory used to build the function calls.</param>
    /// <param name="typeMappingSource">Resolves the <c>text</c> and <c>boolean</c> mappings.</param>
    /// <param name="options">Supplies the configured setting name.</param>
    public CurrentTenantTranslator(
        ISqlExpressionFactory sql,
        IRelationalTypeMappingSource typeMappingSource,
        ITenancySingletonOptions options)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(typeMappingSource);
        ArgumentNullException.ThrowIfNull(options);

        _sql = sql;
        _typeMappingSource = typeMappingSource;
        _options = options;
    }

    /// <summary>
    /// Translates <c>EF.Functions.CurrentTenant()</c>; returns <see langword="null"/> for every
    /// other method so that the next translator gets its turn.
    /// </summary>
    /// <returns>The translated expression, or <see langword="null"/> if not responsible.</returns>
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // null = "not responsible": the next translator gets its turn
        if (method != CurrentTenantMethod)
            return null;

        var text = _typeMappingSource.FindMapping(typeof(string))!;
        var boolean = _typeMappingSource.FindMapping(typeof(bool))!;

        // current_setting(name, missing_ok => true): NULL instead of an error if not set.
        // The setting name is emitted as an SQL literal (escaped correctly).
        // Both functions return NULL although no argument is NULL, so no argument propagates
        // nullability: if any did, EF would derive the result's nullability from the arguments
        // alone and treat it as never NULL (CurrentTenant() == null would become FALSE).
        var currentSetting = _sql.Function(
            "current_setting",
            [_sql.Constant(_options.Settings.SettingName, text), _sql.Constant(true, boolean)],
            nullable: true,
            argumentsPropagateNullability: [false, false],
            typeof(string),
            text);

        // After RESET/DISCARD, current_setting returns '' instead of NULL -> nullif.
        // Without it, the cast to uuid would fail on pooled connections.
        var nullIfEmpty = _sql.Function(
            "nullif",
            [currentSetting, _sql.Constant(string.Empty, text)],
            nullable: true,
            argumentsPropagateNullability: [false, false],
            typeof(string),
            text);

        return _sql.Convert(nullIfEmpty, typeof(TenantId), TenantIdTypeMapping.Default);
    }
}
