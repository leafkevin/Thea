namespace Thea
{
    public interface IQueryBuilder
    {
        IQueryBuilder Query(string sql, object whereObj = null);
        IQueryBuilder Get<TEntity>(object whereObj);
        IQueryBuilder Get<TEntity, TTarget>(object whereObj);
        IQueryBuilder Create<TEntity>(object entity);
        IQueryBuilder Update<TEntity>(object entity);
        IQueryBuilder Update<TEntity>(object updateObj, object whereObj);
        IQueryBuilder Delete<TEntity>(object whereObj);
        IQueryBuilder Exists<TEntity>(object whereObj);
        IQueryBuilder Query<TEntity>(object whereObj);
        IQueryBuilder Query<TEntity, TTarget>(object whereObj);
        IQueryBuilder QueryAll<TEntity>();
        IQueryBuilder QueryAll<TEntity, TTarget>();
        IQueryBuilder QueryPage<TEntity>(int pageIndex, int pageSize, string orderBy = null);
        IQueryBuilder QueryPage<TEntity, TTarget>(int pageIndex, int pageSize, string orderBy = null);
        IQueryBuilder QueryPage<TEntity>(object whereObj, int pageIndex, int pageSize, string orderBy = null);
        IQueryBuilder QueryPage<TEntity, TTarget>(object whereObj, int pageIndex, int pageSize, string orderBy = null);
        IQueryBuilder Execute(string sql, object parameters = null);
        string ToSql();
    }
}
