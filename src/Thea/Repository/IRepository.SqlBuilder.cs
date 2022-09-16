//using System;
//using System.Collections.Generic;
//using System.Threading.Tasks;

//namespace Thea
//{
//    partial interface IRepository
//    {
//        #region 同步方法
//        TEntity QueryFirst<TEntity>(Action<ISqlBuilder> sqlBuilder);
//        List<TEntity> Query<TEntity>(Action<ISqlBuilder> sqlBuilder);
//        IPagedList<TEntity> QueryPage<TEntity>(Action<ISqlBuilder> sqlBuilder, int pageIndex, int pageSize, string orderBy = null);
//        IQueryReader QueryMultiple(Action<ISqlBuilder> sqlBuilder);
//        int Execute(Action<ISqlBuilder> sqlBuilder);
//        #endregion

//        #region 异步方法
//        Task<TEntity> QueryFirstAsync<TEntity>(Action<ISqlBuilder> sqlBuilder);
//        Task<List<TEntity>> QueryAsync<TEntity>(Action<ISqlBuilder> sqlBuilder);
//        Task<IPagedList<TEntity>> QueryPageAsync<TEntity>(Action<ISqlBuilder> sqlBuilder, int pageIndex, int pageSize, string orderBy = null);
//        Task<IQueryReader> QueryMultipleAsync(Action<ISqlBuilder> sqlBuilder);
//        Task<int> ExecuteAsync(Action<ISqlBuilder> sqlBuilder);
//        #endregion
//    }
//}
