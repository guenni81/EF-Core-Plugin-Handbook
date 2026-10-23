using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Query;

/// <summary>tenantId.IsEmpty -&gt; tenant_id = '00000000-...'::uuid</summary>
public class TenantIdMemberTranslator : IMemberTranslator
{
    private static readonly PropertyInfo IsEmptyProperty =
        typeof(TenantId).GetProperty(nameof(TenantId.IsEmpty))!;

    private readonly ISqlExpressionFactory _sql;

    /// <summary>Creates the translator.</summary>
    /// <param name="sql">The factory used to build the comparison.</param>
    public TenantIdMemberTranslator(ISqlExpressionFactory sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        _sql = sql;
    }

    /// <summary>
    /// Translates <see cref="TenantId.IsEmpty"/>; returns <see langword="null"/> for every other
    /// member so that the next translator gets its turn.
    /// </summary>
    /// <returns>The translated expression, or <see langword="null"/> if not responsible.</returns>
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is null || !Equals(member, IsEmptyProperty))
            return null;

        // Constant with the column's type mapping -> correct literal
        return _sql.Equal(instance, _sql.Constant(TenantId.Empty, instance.TypeMapping));
    }
}
