using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Acme.EntityFrameworkCore.Tenancy.PostgreSQL.Query.Expressions;

/// <summary>
/// Custom SQL expression node: <c>left IS [NOT] DISTINCT FROM right</c>.
/// </summary>
/// <remarks>
/// A custom node is needed because the operator is neither a function call nor a standard
/// binary operator and has its own nullability semantics (never NULL, operands do not
/// propagate NULL). Every custom node needs: VisitChildren/Update, Quote (for precompiled
/// queries), Print, Equals/GetHashCode (for the query cache), an SQL generator that writes it,
/// and a nullability processor that understands it.
/// </remarks>
public class DistinctFromExpression : SqlExpression
{
    private static ConstructorInfo? _quotingConstructor;

    /// <summary>Creates the node.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="negated"><see langword="true"/> for IS DISTINCT FROM.</param>
    /// <param name="typeMapping">The <c>bool</c> type mapping of the result.</param>
    public DistinctFromExpression(
        SqlExpression left,
        SqlExpression right,
        bool negated,
        RelationalTypeMapping? typeMapping)
        : base(typeof(bool), typeMapping)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
        IsNegated = negated;
    }

    /// <summary>The left operand.</summary>
    public virtual SqlExpression Left { get; }

    /// <summary>The right operand.</summary>
    public virtual SqlExpression Right { get; }

    /// <summary>true: IS DISTINCT FROM; false: IS NOT DISTINCT FROM.</summary>
    public virtual bool IsNegated { get; }

    /// <summary>Visits the children; without this override EF would throw an exception.</summary>
    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update((SqlExpression)visitor.Visit(Left), (SqlExpression)visitor.Visit(Right));

    /// <summary>Unchanged children -&gt; same instance (important for visitor performance).</summary>
    /// <param name="left">The (possibly visited) left operand.</param>
    /// <param name="right">The (possibly visited) right operand.</param>
    /// <returns>This instance, or a copy with the new operands.</returns>
    public virtual DistinctFromExpression Update(SqlExpression left, SqlExpression right)
        => left == Left && right == Right
            ? this
            : new DistinctFromExpression(left, right, IsNegated, TypeMapping);

    /// <summary>Produces C# code for the node (precompiled queries, NativeAOT).</summary>
    /// <remarks>Experimental EF API (EF9100), suppressed project-wide.</remarks>
    public override Expression Quote()
        => New(
            // Cached lazily: reflection only runs when queries are actually precompiled
            _quotingConstructor ??= typeof(DistinctFromExpression).GetConstructor(
                [typeof(SqlExpression), typeof(SqlExpression), typeof(bool), typeof(RelationalTypeMapping)])!,
            Left.Quote(),
            Right.Quote(),
            Constant(IsNegated),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    /// <summary>For ToQueryString debugging and error messages (DebugView).</summary>
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Visit(Left);
        expressionPrinter.Append(IsNegated ? " IS DISTINCT FROM " : " IS NOT DISTINCT FROM ");
        expressionPrinter.Visit(Right);
    }

    /// <summary>
    /// Structural equality; EF compares SQL trees (e.g. to deduplicate projections), so all
    /// state must take part.
    /// </summary>
    public override bool Equals(object? obj)
        => obj is DistinctFromExpression other
           && (ReferenceEquals(this, other)
               || base.Equals(other)
               && Left.Equals(other.Left)
               && Right.Equals(other.Right)
               && IsNegated == other.IsNegated);

    /// <summary>Hash code consistent with <see cref="Equals(object)"/>.</summary>
    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), Left, Right, IsNegated);
}
