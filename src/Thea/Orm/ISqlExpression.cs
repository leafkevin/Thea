using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Thea.Orm;


public interface ISqlExpression<T>
{
    ISqlExpression<TTarget> Select<TTarget>(Expression<Func<T, TTarget>> fieldsExpr);
    ISqlExpression<T> InnerJoin(Expression<Func<T, bool>> predicate);
    ISqlExpression<T> LeftJoin(Expression<Func<T, bool>> predicate);
    ISqlExpression<T> RightJoin(Expression<Func<T, bool>> predicate);
    ISqlExpression<T> InnerJoin<Source, Target>(Expression<Func<Source, Target, bool>> predicate);
    ISqlExpression<T> Where(Expression<Func<T, bool>> predicate);
    ISqlExpression<T> Where(bool condition, Expression<Func<T, bool>> predicate);
    ISqlExpression<T> Where(Expression<Func<T, IOrmDbFactory, bool>> predicate);
    ISqlExpression<T> Where(bool condition, Expression<Func<T, IOrmDbFactory, bool>> predicate);

    ISqlExpression<T> Where<TTarget>(Expression<Func<TTarget, bool>> predicate);
    ISqlExpression<T> Where<T1, T2>(Expression<Func<T1, T2, bool>> predicate);
    ISqlExpression<T> Where<T1, T2, T3>(Expression<Func<T1, T2, T3, bool>> predicate);

    ISqlExpression<T> Where<TTarget>(bool condition, Expression<Func<TTarget, bool>> predicate);
    ISqlExpression<T> Where<T1, T2>(bool condition, Expression<Func<T1, T2, bool>> predicate);
    ISqlExpression<T> Where<T1, T2, T3>(bool condition, Expression<Func<T1, T2, T3, bool>> predicate);

    IGroupBySqlExpression<TTarget> GroupBy<TTarget>(Expression<Func<T, TTarget>> fieldsExpr);
    ISqlExpression<T> OrderBy<TTarget>(Expression<Func<T, TTarget>> fieldsExpr);
    ISqlExpression<T> OrderByDescending<TTarget>(Expression<Func<T, TTarget>> fieldsExpr);
    string ToSql();
    T First();
    Task<T> FirstAsync(CancellationToken cancellationToken = default);
    List<T> ToList();
    Task<List<T>> ToListAsync(CancellationToken cancellationToken = default);
    Dictionary<TKey, TElement> ToDictionary<TKey, TElement>(Func<T, TKey> keySelector, Func<T, TElement> valueSelector);
    Task<Dictionary<TKey, TElement>> ToDictionaryAsync<TKey, TElement>(Func<T, TKey> keySelector, Func<T, TElement> valueSelector, CancellationToken cancellationToken = default);
    long Count();
    Task<long> CountAsync(CancellationToken cancellationToken = default);
    bool Exists(Expression<Func<T, bool>> predicate);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    //bool Exists<T1>(Expression<Func<T, T1, bool>> predicate);
    //Task<bool> ExistsAsync<T1>(Expression<Func<T, T1, bool>> predicate, CancellationToken cancellationToken = default);
    //bool Exists<T1, T2>(Expression<Func<T, T1, T2, bool>> predicate);
    //Task<bool> ExistsAsync<T1, T2>(Expression<Func<T, T1, T2, bool>> predicate, CancellationToken cancellationToken = default);
}
public interface IGroupBySqlExpression<T>
{
    TTarget Max<TTarget>(Expression<Func<T, TTarget>> fieldExpr);
    TTarget Min<TTarget>(Expression<Func<T, TTarget>> fieldExpr);
    TTarget Average<TTarget>(Expression<Func<T, TTarget>> fieldExpr);
    int Count<TTarget>(Expression<Func<T, TTarget>> fieldExpr);
    ISqlExpression<TTarget> Select<TTarget>(Expression<Func<T, TTarget>> fieldsExpr);
    IGroupBySqlExpression<T> Having(Expression<Func<T, bool>> predicate);
    ISqlExpression<T> OrderBy<TTarget>(Expression<Func<T, TTarget>> fieldsExpr);
    ISqlExpression<T> OrderByDescending<TTarget>(Expression<Func<T, TTarget>> fieldsExpr);
}
