using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Thea;

public static class PredicateBuilder
{
    public static Expression<Func<T, bool>> True<T>() => f => true;
    public static Expression<Func<T, bool>> False<T>() => f => false;
    public static Expression<Func<T, bool>> Eval<T>(Expression<Func<T, bool>> predicate) => predicate;
    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> first, Expression<Func<T, bool>> second)
        => first.Compose(second, Expression.AndAlso);
    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> first, Expression<Func<T, bool>> second)
        => first.Compose(second, Expression.OrElse);
    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> expr)
        => Expression.Lambda<Func<T, bool>>(Expression.Not(expr.Body), expr.Parameters);
    private static Expression<T> Compose<T>(this Expression<T> first, Expression<T> second, Func<Expression, Expression, Expression> merge)
    {
        var map = first.Parameters.Select((f, i) => new { f, s = second.Parameters[i] }).ToDictionary(p => p.s, p => p.f);
        var body = ParameterRebinder.ReplaceParameters(map, second.Body);
        return Expression.Lambda<T>(merge(first.Body, body), first.Parameters);
    }
    class ParameterRebinder : ExpressionVisitor
    {
        private readonly Dictionary<ParameterExpression, ParameterExpression> map;
        public ParameterRebinder(Dictionary<ParameterExpression, ParameterExpression> map)
            => this.map = map ?? new Dictionary<ParameterExpression, ParameterExpression>();
        public static Expression ReplaceParameters(Dictionary<ParameterExpression, ParameterExpression> map, Expression exp)
            => new ParameterRebinder(map).Visit(exp);
        protected override Expression VisitParameter(ParameterExpression p)
        {
            ParameterExpression replacement;
            if (map.TryGetValue(p, out replacement))
            {
                p = replacement;
            }
            return base.VisitParameter(p);
        }
    }
}
