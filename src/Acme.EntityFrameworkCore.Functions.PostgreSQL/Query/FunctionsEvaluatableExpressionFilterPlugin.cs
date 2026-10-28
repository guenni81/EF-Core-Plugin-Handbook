using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Acme.EntityFrameworkCore.Functions.PostgreSQL.Query;

/// <summary>
/// Prevents the methods of <see cref="AcmePgDbFunctionsExtensions"/> from being evaluated on
/// the client (chapter "Preventing client-side evaluation").
/// </summary>
public class FunctionsEvaluatableExpressionFilterPlugin : IEvaluatableExpressionFilterPlugin
{
    /// <summary>Returns false for calls to the plugin's functions, true for everything else.</summary>
    /// <param name="expression">The expression the funcletizer considers evaluating.</param>
    /// <returns>True if the expression may be evaluated on the client.</returns>
    public virtual bool IsEvaluatableExpression(Expression expression)
        => expression is not MethodCallExpression { Method.DeclaringType: { } type }
           || type != typeof(AcmePgDbFunctionsExtensions);
}
