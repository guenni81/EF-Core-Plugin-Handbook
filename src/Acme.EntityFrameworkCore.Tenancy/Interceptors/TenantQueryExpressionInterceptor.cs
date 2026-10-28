using System.Linq.Expressions;
using System.Reflection;
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
                        Check(lambda, node.Arguments[0]);
                }
            }

            return base.VisitMethodCall(node);
        }

        private void Check(LambdaExpression selector, Expression source)
        {
            // After a projection the setter names a member of the projected type: resolve it to
            // the entity member it was projected from (e.Entity.TenantId -> o.TenantId)
            var (body, roots) = Inline(selector.Body, selector.Parameters[0], source);

            // The property being set and the expression of the entity that owns it:
            // o => o.TenantId, o => EF.Property<TenantId>(o, "TenantId") (also shadow
            // properties), o => o.Customer.TenantId
            var (instance, name) = body switch
            {
                MemberExpression { Expression: { } target } member => (target, member.Member.Name),
                MethodCallExpression
                {
                    Method.Name: nameof(EF.Property),
                    Arguments: [var target, ConstantExpression { Value: string property }]
                } call when call.Method.DeclaringType == typeof(EF) => (target, property),
                _ => (null, null),
            };

            if (instance is null || name is null || !IsRootedAt(instance, roots))
                return;

            // The model decides, not the name: the instance has to be an entity type whose tenant
            // property is the one being set. Its static type, or the operand's type under a cast
            // such as ((ITenantOwned)o).TenantId. Searched instead of FindEntityType(Type), which
            // carries trimming annotations, and over all entity types of the CLR type
            // (shared-type entities); this runs once per query shape.
            Type[] types = [instance.Type, Unconvert(instance).Type];
            if (_context.Model.GetEntityTypes().Any(e =>
                    types.Contains(e.ClrType) && e.GetTenantPropertyName() == name))
                throw new TenancyException(
                    "The tenant property of an entity must not be modified in ExecuteUpdate.");
        }

        /// <summary>
        /// Replaces the setter's parameter with the body of each Select below the ExecuteUpdate,
        /// skipping operators that keep the element type (Where, OrderBy, Take, ...), and reduces
        /// member accesses on the projected <c>new { ... }</c> to the projected expression. Join
        /// and SelectMany are followed through their result selector into both sources.
        /// </summary>
        /// <returns>The resolved body and the parameters it may start at.</returns>
        private static (Expression Body, List<ParameterExpression> Roots) Inline(
            Expression body, ParameterExpression parameter, Expression source)
        {
            while (source is MethodCallExpression { Method.DeclaringType: var type } call
                   && type == typeof(Queryable) && call.Arguments.Count > 0)
            {
                if (call.Method.Name == nameof(Queryable.Select)
                    && Unquote(call.Arguments[1]) is LambdaExpression { Parameters: [var inner] } projection)
                {
                    body = new Projection(parameter, projection.Body).Visit(body);
                    parameter = inner;
                }
                else if (call.Method.Name is nameof(Queryable.Join) or nameof(Queryable.SelectMany)
                         && Unquote(call.Arguments[^1]) is LambdaExpression { Parameters: [var outer, var element] } result)
                {
                    // Join(outer, inner, ..., result); SelectMany(outer, o => collection, result)
                    var collection = call.Method.Name == nameof(Queryable.Join)
                        ? call.Arguments[1]
                        : Unquote(call.Arguments[1]) is LambdaExpression selector ? selector.Body : null;

                    var (resolved, roots) = Inline(
                        new Projection(parameter, result.Body).Visit(body), outer, call.Arguments[0]);
                    if (collection is null)
                        return (resolved, [.. roots, element]);

                    var (both, more) = Inline(resolved, element, collection);
                    return (both, [.. roots, .. more]);
                }
                else if (ElementType(call.Type) != ElementType(call.Arguments[0].Type))
                {
                    break;      // GroupBy, GroupJoin, ...: not resolved, the static type decides
                }

                source = call.Arguments[0];
            }

            return (body, [parameter]);
        }

        private static Type? ElementType(Type type)
            => type.IsGenericType ? type.GetGenericArguments()[0] : null;

        // The setter must start at its (resolved) parameter: o, o.Customer, ((Derived)o).Inner
        private static bool IsRootedAt(Expression expression, List<ParameterExpression> parameters)
        {
            while (true)
            {
                switch (expression)
                {
                    case ParameterExpression p:
                        return parameters.Contains(p);
                    case MemberExpression { Expression: { } inner }:
                        expression = inner;
                        break;
                    case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } cast:
                        expression = cast.Operand;
                        break;
                    default:
                        return false;
                }
            }
        }

        private static Expression Unconvert(Expression expression)
            => expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } cast
                ? cast.Operand
                : expression;

        /// <summary>Substitutes the parameter and reduces <c>new { A = x }.A</c> to <c>x</c>.</summary>
        private sealed class Projection : ExpressionVisitor
        {
            private readonly ParameterExpression _parameter;
            private readonly Expression _body;

            public Projection(ParameterExpression parameter, Expression body)
            {
                _parameter = parameter;
                _body = body;
            }

            protected override Expression VisitParameter(ParameterExpression node)
                => node == _parameter ? _body : node;

            protected override Expression VisitMember(MemberExpression node)
            {
                var inner = Visit(node.Expression);
                switch (inner)
                {
                    // Anonymous types and records with a constructor: the member names the argument
                    case NewExpression { Members: { } members } created:
                        for (var i = 0; i < members.Count; i++)
                        {
                            if (SameMember(members[i], node.Member))
                                return created.Arguments[i];
                        }

                        break;
                    // new Dto { Entity = o }
                    case MemberInitExpression init:
                        foreach (var binding in init.Bindings)
                        {
                            if (binding is MemberAssignment assignment && SameMember(assignment.Member, node.Member))
                                return assignment.Expression;
                        }

                        break;
                }

                return node.Update(inner);
            }

            // Anonymous types name the getter in NewExpression.Members on some compilers
            private static bool SameMember(MemberInfo candidate, MemberInfo member)
                => candidate.Name == member.Name || candidate.Name == "get_" + member.Name;
        }

        private static Expression Unquote(Expression expression)
            => expression is UnaryExpression { NodeType: ExpressionType.Quote } quote
                ? quote.Operand
                : expression;
    }
}
