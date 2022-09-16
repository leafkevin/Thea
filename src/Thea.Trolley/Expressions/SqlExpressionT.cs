using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Thea.Orm;

namespace Thea.Trolley;

public class SqlExpression<T> : ISqlExpression<T>
{
    private readonly IOrmDbFactory dbFactory = null;
    private readonly TheaConnection connection = null;
    private readonly IOrmProvider ormProvider = null;
    private readonly StringBuilder sqlBuilder = new();
    private readonly SqlExpressionVisitor visitor = null;
    public SqlExpression(IOrmDbFactory dbFactory, TheaConnection connection, SqlExpressionVisitor visitor)
    {
        this.dbFactory = dbFactory;
        this.connection = connection;
        this.ormProvider = connection.OrmProvider;
        this.visitor = visitor;
    }

    public long Count()
    {
        var command = this.connection.CreateCommand();

        throw new NotImplementedException();
    }

    public Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public bool Exists(Expression<Func<T, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public T First()
    {
        throw new NotImplementedException();
    }

    public Task<T> FirstAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IGroupBySqlExpression<TTarget> GroupBy<TTarget>(Expression<Func<T, TTarget>> fieldsExpr)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> InnerJoin(Expression<Func<T, bool>> predicate)
    {
        this.visitor.InnerJoin(typeof(T), predicate.Body);
        return this;
    }

    public ISqlExpression<T> InnerJoin<Source, Target>(Expression<Func<Source, Target, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> LeftJoin(Expression<Func<T, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> OrderBy<TTarget>(Expression<Func<T, TTarget>> fieldsExpr)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> OrderByDescending<TTarget>(Expression<Func<T, TTarget>> fieldsExpr)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> RightJoin(Expression<Func<T, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<TTarget> Select<TTarget>(Expression<Func<T, TTarget>> fieldsExpr)
    {
        this.visitor.Select(fieldsExpr.Body);
        return new SqlExpression<TTarget>(this.dbFactory, this.connection, this.visitor);
    }

    public Dictionary<TKey, TElement> ToDictionary<TKey, TElement>(Func<T, TKey> keySelector, Func<T, TElement> valueSelector)
    {
        throw new NotImplementedException();
    }

    public Task<Dictionary<TKey, TElement>> ToDictionaryAsync<TKey, TElement>(Func<T, TKey> keySelector, Func<T, TElement> valueSelector, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public List<T> ToList()
    {
        //this.visitor.BuildSql()
        throw new NotImplementedException();
    }

    public Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public string ToSql() => this.visitor.BuildSql();

    public ISqlExpression<T> Where(Expression<Func<T, bool>> predicate)
        => this.Where(true, predicate);

    public ISqlExpression<T> Where(bool condition, Expression<Func<T, bool>> predicate)
    {
        if (condition)
            this.visitor.Where(predicate);
        return this;
    }

    public ISqlExpression<T> Where(Expression<Func<T, IOrmDbFactory, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> Where(bool condition, Expression<Func<T, IOrmDbFactory, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> Where<TTarget>(Expression<Func<TTarget, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> Where<T1, T2>(Expression<Func<T1, T2, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> Where<T1, T2, T3>(Expression<Func<T1, T2, T3, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> Where<TTarget>(bool condition, Expression<Func<TTarget, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> Where<T1, T2>(bool condition, Expression<Func<T1, T2, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> Where<T1, T2, T3>(bool condition, Expression<Func<T1, T2, T3, bool>> predicate)
    {
        throw new NotImplementedException();
    }

    public ISqlExpression<T> Include<TTarget>(Expression<Func<T, TTarget>> predicate)
    {
        this.visitor.Include(predicate);
        return this;
    }
}


public class SqlExpression<T1, T2>
{

}
