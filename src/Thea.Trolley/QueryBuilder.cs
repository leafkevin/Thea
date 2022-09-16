using System;
using System.Data;
using Thea.Orm;

namespace Thea.Trolley;

public class QueryBuilder : IQueryBuilder
{
    private readonly IOrmDbFactory dbFactory;
    private readonly TheaConnection connection;
    internal readonly IDbCommand command;
    private int index = 0;

    public QueryBuilder(IOrmDbFactory dbFactory, TheaConnection connection, IDbCommand command)
    {
        this.dbFactory = dbFactory;
        this.connection = connection;
        this.command = command;
    }
    public IQueryBuilder Query(string sql, object whereObj = null)
    {
        if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException("sql");
        if (whereObj != null)
        {
            var parameterInfo = this.CreateParameterInfo(whereObj);
            if (parameterInfo.IsMulti)
                throw new Exception("whereObj参数暂时不支持IEnumerable类型，参数的属性值可以是IEnumerable类型");
            var commandInitializer = RepositoryHelper.BuildQueryWhereSqlCache(this.dbFactory, this.connection, CommandType.Text, parameterInfo);
            commandInitializer.Invoke(this.command, whereObj);
        }
        if (!string.IsNullOrEmpty(this.command.CommandText))
            this.command.CommandText += ";" + sql;
        return this;
    }
    public IQueryBuilder Get<TEntity>(object whereObj)
    {
        if (whereObj == null) throw new ArgumentNullException("whereObj");
        var entityType = typeof(TEntity);
        var parameterInfo = this.CreateParameterInfo(whereObj);
        if (parameterInfo.IsMulti)
            throw new Exception("whereObj参数暂时不支持IEnumerable类型，参数的属性值可以是IEnumerable类型");
        var commandInitializer = RepositoryHelper.BuildQueryCache(this.dbFactory, this.connection, entityType, entityType, parameterInfo);
        commandInitializer.Invoke(this.command, whereObj);
        return this;
    }
    public IQueryBuilder Get<TEntity, TTarget>(object whereObj)
    {
        if (whereObj == null) throw new ArgumentNullException("whereObj");
        var entityType = typeof(TEntity);
        var targetType = typeof(TTarget);

        var parameterInfo = this.CreateParameterInfo(whereObj);
        if (parameterInfo.IsMulti)
            throw new Exception("whereObj参数暂时不支持IEnumerable类型，参数的属性值可以是IEnumerable类型");
        var commandInitializer = RepositoryHelper.BuildQueryCache(this.dbFactory, this.connection, entityType, targetType, parameterInfo);
        commandInitializer.Invoke(this.command, whereObj);
        return this;
    }
    public IQueryBuilder Create<TEntity>(object entity)
    {
        if (entity == null) throw new ArgumentNullException("entity");

        var entityType = typeof(TEntity);
        var parameterInfo = this.CreateParameterInfo(entity);
        var createInfo = RepositoryHelper.BuildCreateCache(this.dbFactory, this.connection, entityType, parameterInfo);
        createInfo.Initializer(this.command, entity);
        return this;
    }
    public IQueryBuilder Update<TEntity>(object entity)
    {
        if (entity == null) throw new ArgumentNullException("entity");

        var entityType = typeof(TEntity);
        var parameterInfo = this.CreateParameterInfo(entity);
        var commandInitializer = RepositoryHelper.BuildUpdateKeyCache(this.dbFactory, this.connection, entityType, parameterInfo);
        commandInitializer.Invoke(this.command, entity);
        return this;
    }
    public IQueryBuilder Update<TEntity>(object updateObj, object whereObj)
    {
        if (updateObj == null) throw new ArgumentNullException("updateObj");
        if (whereObj == null) throw new ArgumentNullException("whereObj");

        var mutilIndex = ++this.index;
        var entityType = typeof(TEntity);
        var updateObjInfo = this.CreateParameterInfo(updateObj, mutilIndex);
        var whereObjInfo = this.CreateParameterInfo(whereObj, mutilIndex);

        if (updateObjInfo.IsMulti || whereObjInfo.IsMulti)
            throw new Exception("updateObj和whereObj参数暂时都不支持IEnumerable类型，参数的属性值可以是IEnumerable类型");
        var commandInitializer = RepositoryHelper.BuildUpdateCache(this.dbFactory, this.connection, entityType, updateObjInfo, whereObjInfo);
        commandInitializer.Invoke(this.command, updateObj, whereObj);
        return this;
    }
    public IQueryBuilder Delete<TEntity>(object whereObj)
    {
        if (whereObj == null) throw new ArgumentNullException("whereObj");
        var whereObjInfo = this.CreateParameterInfo(whereObj);
        if (whereObjInfo.IsMulti)
            throw new Exception("whereObj参数暂时不支持IEnumerable类型，参数的属性值可以是IEnumerable类型");
        var commandInitializer = RepositoryHelper.BuildDeleteCache(this.dbFactory, this.connection, typeof(TEntity), whereObjInfo);
        commandInitializer.Invoke(this.command, whereObj);
        return this;
    }
    public IQueryBuilder Exists<TEntity>(object whereObj)
    {
        if (whereObj == null) throw new ArgumentNullException("whereObj");
        var whereObjInfo = this.CreateParameterInfo(whereObj);
        if (whereObjInfo.IsMulti)
            throw new Exception("whereObj参数暂时不支持IEnumerable类型，参数的属性值可以是IEnumerable类型");
        var entityType = typeof(TEntity);
        var commandInitializer = RepositoryHelper.BuildExistsCache(this.dbFactory, this.connection, entityType, whereObjInfo);
        commandInitializer.Invoke(this.command, whereObj);
        return this;
    }
    public IQueryBuilder Query<TEntity>(object whereObj)
    {
        if (whereObj == null) throw new ArgumentNullException("whereObj");
        var entityType = typeof(TEntity);
        var parameterInfo = this.CreateParameterInfo(whereObj);
        if (parameterInfo.IsMulti)
            throw new Exception("whereObj参数暂时不支持IEnumerable类型，参数的属性值可以是IEnumerable类型");
        var commandInitializer = RepositoryHelper.BuildQueryCache(this.dbFactory, this.connection, entityType, entityType, parameterInfo);
        commandInitializer.Invoke(this.command, whereObj);
        return this;
    }
    public IQueryBuilder Query<TEntity, TTarget>(object whereObj)
    {
        if (whereObj == null) throw new ArgumentNullException("whereObj");
        var entityType = typeof(TEntity);
        var targetType = typeof(TTarget);
        var parameterInfo = this.CreateParameterInfo(whereObj);
        if (parameterInfo.IsMulti)
            throw new Exception("whereObj参数暂时不支持IEnumerable类型，参数的属性值可以是IEnumerable类型");
        var commandInitializer = RepositoryHelper.BuildQueryCache(this.dbFactory, this.connection, entityType, targetType, parameterInfo);
        commandInitializer.Invoke(this.command, whereObj);
        return this;
    }
    public IQueryBuilder QueryAll<TEntity>()
    {
        var entityType = typeof(TEntity);
        var commandInitializer = RepositoryHelper.BuildQuerySelectCache(this.dbFactory, this.connection, entityType, entityType);
        commandInitializer.Invoke(command, null);
        return this;
    }
    public IQueryBuilder QueryAll<TEntity, TTarget>()
    {
        var entityType = typeof(TEntity);
        var targetType = typeof(TTarget);
        var commandInitializer = RepositoryHelper.BuildQuerySelectCache(this.dbFactory, this.connection, entityType, targetType);
        commandInitializer.Invoke(command, null);
        return this;
    }
    public IQueryBuilder QueryPage<TEntity>(int pageIndex, int pageSize, string orderBy = null)
    {
        var entityType = typeof(TEntity);
        var commandInitializer = RepositoryHelper.BuildQueryPageCache(this.dbFactory, this.connection, entityType, entityType);
        commandInitializer.Invoke(command, pageIndex, pageSize, orderBy);
        return this;
    }
    public IQueryBuilder QueryPage<TEntity, TTarget>(int pageIndex, int pageSize, string orderBy = null)
    {
        var entityType = typeof(TEntity);
        var targetType = typeof(TTarget);

        var commandInitializer = RepositoryHelper.BuildQueryPageCache(this.dbFactory, this.connection, entityType, targetType);
        commandInitializer.Invoke(command, pageIndex, pageSize, orderBy);
        return this;
    }
    public IQueryBuilder QueryPage<TEntity>(object whereObj, int pageIndex, int pageSize, string orderBy = null)
    {
        if (whereObj == null) throw new ArgumentNullException("whereObj");
        var entityType = typeof(TEntity);
        var parameterInfo = this.CreateParameterInfo(whereObj);
        if (parameterInfo.IsMulti)
            throw new Exception("whereObj参数暂时不支持IEnumerable类型，参数的属性值可以是IEnumerable类型");
        var commandInitializer = RepositoryHelper.BuildQueryPageCache(this.dbFactory, this.connection, entityType, entityType, parameterInfo);
        commandInitializer.Invoke(command, pageIndex, pageSize, orderBy, whereObj);
        return this;
    }
    public IQueryBuilder QueryPage<TEntity, TTarget>(object whereObj, int pageIndex, int pageSize, string orderBy = null)
    {
        if (whereObj == null) throw new ArgumentNullException("whereObj");
        var entityType = typeof(TEntity);
        var targetType = typeof(TTarget);
        var parameterInfo = this.CreateParameterInfo(whereObj);
        if (parameterInfo.IsMulti)
            throw new Exception("whereObj参数暂时不支持IEnumerable类型，参数的属性值可以是IEnumerable类型");
        var commandInitializer = RepositoryHelper.BuildQueryPageCache(this.dbFactory, this.connection, entityType, targetType, parameterInfo);
        commandInitializer.Invoke(command, pageIndex, pageSize, orderBy, whereObj);
        return this;
    }
    public IQueryBuilder Execute(string sql, object parameters = null)
    {
        if (string.IsNullOrEmpty(sql)) throw new ArgumentNullException("sql");
        if (parameters != null)
        {
            var parameterInfo = this.CreateParameterInfo(parameters);
            if (parameterInfo.IsMulti)
                throw new Exception("parameters参数暂时不支持IEnumerable类型，参数的属性值可以是IEnumerable类型");
            var commandInitializer = RepositoryHelper.BuildQueryWhereSqlCache(this.dbFactory, this.connection, CommandType.Text, parameterInfo);
            commandInitializer.Invoke(command, parameters);
        }
        if (!string.IsNullOrEmpty(this.command.CommandText))
            this.command.CommandText += ";" + sql;
        return this;
    }
    public string ToSql() => this.command.CommandText;
    private ParameterInfo CreateParameterInfo(object parameters)
    {
        var parameterInfo = RepositoryHelper.CreateParameterInfo(parameters);
        parameterInfo.MulitIndex = ++this.index;
        return parameterInfo;
    }
    private ParameterInfo CreateParameterInfo(object parameters, int index)
    {
        var parameterInfo = RepositoryHelper.CreateParameterInfo(parameters);
        parameterInfo.MulitIndex = index;
        return parameterInfo;
    }
}
