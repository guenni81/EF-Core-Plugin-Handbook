using System.Linq.Expressions;
using Acme.EntityFrameworkCore.Tenancy.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Acme.EntityFrameworkCore.Tenancy.Interceptors;

/// <summary>
/// Optional: rejects <c>ExecuteUpdate</c>/<c>ExecuteUpdateAsync</c> calls that set the tenant
/// property. Bulk updates bypass the change tracker and with it the SaveChanges interceptor;
/// they do pass through the query pipeline, where this interceptor sees their expression tree
/// (chapter "Bulk operations and the limits of interceptors").
/// </summary>
/// <remarks>
/// It runs once per query shape, before EF translates and caches the query, so the check costs
/// nothing on later executions; it keeps no state. Although the interface derives from
/// <see cref="ISingletonInterceptor"/>, EF only calls query expression interceptors that are
/// passed with AddInterceptors or registered as <see cref="IInterceptor"/>; the provider
/// packages therefore register it as <see cref="IInterceptor"/>. Row level security still
/// decides which rows an update reaches; this check only stops moving rows to another tenant
/// early and with a clear message.
/// </remarks>
public class TenantQueryExpressionInterceptor : IQueryExpressionInterceptor
{
    /// <summary>Checks the query tree and returns it unchanged.</summary>
    /// <param name="queryExpression">The LINQ expression tree of the query.</param>
    /// <param name="eventData">Context and logger of the query.</param>
    /// <returns><paramref name="queryExpression"/>, unchanged.</returns>
    /// <exception cref="TenancyException">An ExecuteUpdate sets the tenant property.</exception>
    public virtual Expression QueryCompilationStarting(
        Expression queryExpression, QueryExpressionEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is { } context)
            new SetterVisitor(context).Visit(queryExpression);

        return queryExpression;
    }

    /// <summary>
    /// Finds ExecuteUpdate calls. EF Core 10 represents their setters as an array of tuples:
    /// <c>ExecuteUpdate(source, new[] { new Tuple(e => e.Property, value), ... })</c>.
    /// </summary>
    private sealed class SetterVisitor : ExpressionVisitor
    {
        private readonly DbContext _context;

        public SetterVisitor(DbContext context) => _context = context;

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                && node.Method.Name is nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate)
                    or nameof(EntityFrameworkQueryableExtensions.ExecuteUpdateAsync)
                && node.Arguments.Count > 1
                && node.Arguments[1] is NewArrayExpression setters)
            {
                foreach (var setter in setters.Expressions)
                {
                    if (setter is NewExpression { Arguments: [var selector, ..] }
                        && Unquote(selector) is LambdaExpression lambda)
                        Check(lambda);
                }
            }

            return base.VisitMethodCall(node);
        }

        private void Check(LambdaExpression selector)
        {
            var parameter = selector.Parameters[0];
            var name = selector.Body switch
            {
                // e => e.TenantId
                MemberExpression member when member.Expression == parameter => member.Member.Name,
                // e => EF.Property<TenantId>(e, "TenantId"), also for shadow properties
                MethodCallExpression
                {
                    Method.Name: nameof(EF.Property),
                    Arguments: [var target, ConstantExpression { Value: string property }]
                } when target == parameter => property,
                _ => null,
            };

            // Searched instead of FindEntityType(Type), which carries trimming annotations; this
            // runs once per query shape
            if (name is not null
                && _context.Model.GetEntityTypes().FirstOrDefault(e => e.ClrType == parameter.Type)
                    ?.GetTenantPropertyName() == name)
                throw new TenancyException(
                    "The tenant property of an entity must not be modified in ExecuteUpdate.");
        }

        private static Expression Unquote(Expression expression)
            => expression is UnaryExpression { NodeType: ExpressionType.Quote } quote
                ? quote.Operand
                : expression;
    }
}
