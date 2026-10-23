using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Acme.EntityFrameworkCore.Tenancy.Query;

/// <summary>
/// Prevents the plugin's EF.Functions methods from being evaluated on the client.
/// Provider-independent: recognizes all classes marked with [TenancyDbFunctions].
/// </summary>
/// <remarks>
/// EF's funcletizer evaluates every expression without a reference to query data in .NET
/// up front. Only known cases are exempt (DateTime.Now, Guid.NewGuid, methods on EF's own
/// DbFunctionsExtensions class). Without this plugin, EF.Functions.CurrentTenant(), for example,
/// would be executed and throw its InvalidOperationException.
/// </remarks>
public class TenancyEvaluatableExpressionFilterPlugin : IEvaluatableExpressionFilterPlugin
{
    /// <summary>
    /// Returns false for calls to methods of classes marked with
    /// <see cref="TenancyDbFunctionsAttribute"/>, true for everything else.
    /// </summary>
    /// <param name="expression">The expression the funcletizer considers evaluating.</param>
    /// <returns>True if the expression may be evaluated on the client.</returns>
    /// <remarks>
    /// EF combines all plugins with AND: returning true does not force evaluation, it only
    /// leaves the decision to EF and the other filters.
    /// </remarks>
    public virtual bool IsEvaluatableExpression(Expression expression)
        => expression is not MethodCallExpression { Method.DeclaringType: { } type }
           || !type.IsDefined(typeof(TenancyDbFunctionsAttribute), inherit: false);
}
