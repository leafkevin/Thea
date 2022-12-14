using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Thea.Orm;

namespace Thea.Trolley;

/// <summary>
/// PostgreSql:
/// UPDATE sys_order a 
/// SET "TotalAmount"=a."TotalAmount"+b."TotalAmount"+50
/// FROM sys_order_detail b
/// WHERE a."Id"=b."OrderId";
/// 
/// MSSql:
/// UPDATE sys_order
/// SET [TotalAmount]=sys_order.[TotalAmount]+b.[TotalAmount]+50
/// FROM sys_order_detail b
/// WHERE sys_order.[Id]=b.[OrderId];
///
/// MySql:
/// UPDATE sys_order a 
/// INNER JOIN sys_order_detail b ON a.`Id` = b.`OrderId`
/// SET `TotalAmount`=a.`TotalAmount`+b.`TotalAmount`+50
/// WHERE a.`Id`=1;
/// 
/// UPDATE sys_order a 
/// INNER JOIN sys_order_detail b ON a.`Id` = b.`OrderId`
/// SET a.`TotalAmount`=a.`TotalAmount`+b.`TotalAmount`+50
/// WHERE a.`Id`=1;
/// 
/// UPDATE sys_order a 
/// LEFT JOIN sys_order_detail b ON a.`Id` = b.`OrderId`
/// SET a.`TotalAmount`=a.`TotalAmount`+b.`TotalAmount`+50
/// WHERE a.`TotalAmount` IS NULL;
/// </summary>
/// <typeparam name="TEntity"></typeparam>
class Update<TEntity> : IUpdate<TEntity>
{
    private readonly IOrmDbFactory dbFactory;
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;

    public Update(IOrmDbFactory dbFactory, TheaConnection connection, IDbTransaction transaction)
    {
        this.dbFactory = dbFactory;
        this.connection = connection;
        this.transaction = transaction;
    }
    public IUpdateSet<TEntity> WithBy<TField>(TField parameters, int bulkCount = 500)
    {
        if (parameters == null)
            throw new ArgumentNullException(nameof(parameters));

        return new UpdateSet<TEntity>(this.dbFactory, this.connection, this.transaction, null, parameters, bulkCount);
    }
    public IUpdateSet<TEntity> WithBy<TField>(Expression<Func<TEntity, TField>> fieldsExpr, object parameters, int bulkCount = 500)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (parameters == null)
            throw new ArgumentNullException(nameof(parameters));
        if (fieldsExpr.Body.NodeType != ExpressionType.MemberAccess && fieldsExpr.Body.NodeType != ExpressionType.New)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持MemberAccess或New类型表达式");

        var setFields = new List<SetField>();
        switch (fieldsExpr.Body.NodeType)
        {
            case ExpressionType.MemberAccess:
                var memberExpr = fieldsExpr.Body as MemberExpression;
                setFields.Add(new SetField { MemberName = memberExpr.Member.Name });
                break;
            case ExpressionType.New:
                var newExpr = fieldsExpr.Body as NewExpression;
                var entityMapper = this.dbFactory.GetEntityMap(typeof(TEntity));
                UpdateVisitor visitor = null;
                bool hasParameterFields = false;
                for (int i = 0; i < newExpr.Arguments.Count; i++)
                {
                    var memberInfo = newExpr.Members[i];
                    if (!entityMapper.TryGetMemberMap(memberInfo.Name, out _))
                        continue;
                    var argumentExpr = newExpr.Arguments[i];
                    if (argumentExpr is MemberExpression newMemberExpr && newMemberExpr.Member.Name == memberInfo.Name)
                    {
                        setFields.Add(new SetField { MemberName = memberInfo.Name });
                        hasParameterFields = true;
                    }
                    else
                    {
                        visitor ??= new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity));
                        var sqlSegment = visitor.SetValue(fieldsExpr, argumentExpr, out var dbParameters);
                        setFields.Add(new SetField
                        {
                            MemberName = memberInfo.Name,
                            Value = sqlSegment.Value.ToString(),
                            DbParameters = dbParameters
                        });
                    }
                }
                if (!hasParameterFields)
                    throw new NotSupportedException("WithBy方法参数fieldsExpr需要有直接成员访问的栏位才能被parameters参数进行设置，如：WithBy(f => new { f.OrderNo ,f.TotalAmount }, parameters)");
                break;
        }
        return new UpdateSet<TEntity>(this.dbFactory, this.connection, this.transaction, setFields, parameters, bulkCount);
    }
    public IUpdateSetting<TEntity> Set<TFields>(Expression<Func<TEntity, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        var visitor = new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity));
        return new UpdateSetting<TEntity>(visitor.Set(fieldsExpr));
    }
    public IUpdateSetting<TEntity> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        var visitor = new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity));
        return new UpdateSetting<TEntity>(visitor.Set(fieldExpr, fieldValue));
    }
    public IUpdateSetting<TEntity> Set<TFields>(bool condition, Expression<Func<TEntity, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        var visitor = new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity));
        if (condition) visitor.Set(fieldsExpr);
        return new UpdateSetting<TEntity>(visitor);
    }
    public IUpdateSetting<TEntity> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        var visitor = new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity));
        if (condition) visitor.Set(fieldExpr, fieldValue);
        return new UpdateSetting<TEntity>(visitor);
    }
    public IUpdateFrom<TEntity, T> From<T>()
    {
        var visitor = new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity))
            .From(typeof(T));
        return new UpdateFrom<TEntity, T>(visitor);
    }
    public IUpdateFrom<TEntity, T1, T2> From<T1, T2>()
    {
        var visitor = new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity))
            .From(typeof(T1), typeof(T2));
        return new UpdateFrom<TEntity, T1, T2>(visitor);
    }
    public IUpdateFrom<TEntity, T1, T2, T3> From<T1, T2, T3>()
    {
        var visitor = new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity))
             .From(typeof(T1), typeof(T2), typeof(T3));
        return new UpdateFrom<TEntity, T1, T2, T3>(visitor);
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4> From<T1, T2, T3, T4>()
    {
        var visitor = new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity))
             .From(typeof(T1), typeof(T2), typeof(T3), typeof(T4));
        return new UpdateFrom<TEntity, T1, T2, T3, T4>(visitor);
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4, T5> From<T1, T2, T3, T4, T5>()
    {
        var visitor = new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity))
             .From(typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5));
        return new UpdateFrom<TEntity, T1, T2, T3, T4, T5>(visitor);
    }
    public IUpdateJoin<TEntity, T> InnerJoin<T>(Expression<Func<TEntity, T, bool>> joinOn)
    {
        var visitor = new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity))
            .Join("INNER JOIN", typeof(T), joinOn);
        return new UpdateJoin<TEntity, T>(visitor);
    }
    public IUpdateJoin<TEntity, T> LeftJoin<T>(Expression<Func<TEntity, T, bool>> joinOn)
    {
        var visitor = new UpdateVisitor(this.dbFactory, this.connection, this.transaction, typeof(TEntity))
           .Join("INNER JOIN", typeof(T), joinOn);
        return new UpdateJoin<TEntity, T>(visitor);
    }
}
class UpdateSet<TEntity> : IUpdateSet<TEntity>
{
    private static ConcurrentDictionary<int, object> objCommandInitializerCache = new();
    private static ConcurrentDictionary<int, object> sqlCommandInitializerCache = new();
    private static ConcurrentDictionary<int, object> setFieldsCommandInitializerCache = new();
    private readonly IOrmDbFactory dbFactory;
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private List<SetField> setFields = null;
    private object parameters = null;
    private int? bulkCount = null;

    public UpdateSet(IOrmDbFactory dbFactory, TheaConnection connection, IDbTransaction transaction, List<SetField> setFields, object parameters, int? bulkCount = null)
    {
        this.dbFactory = dbFactory;
        this.connection = connection;
        this.transaction = transaction;
        this.setFields = setFields;
        this.parameters = parameters;
        this.bulkCount = bulkCount;
    }
    public int Execute()
    {
        bool isMulti = false;
        bool isDictionary = false;
        var entityType = typeof(TEntity);
        Type parameterType = null;
        IEnumerable entities = null;
        if (this.parameters is Dictionary<string, object> dict)
            isDictionary = true;
        else if (this.parameters is IEnumerable && parameterType != typeof(string))
        {
            isMulti = true;
            entities = this.parameters as IEnumerable;
            foreach (var entity in entities)
            {
                if (entity is Dictionary<string, object>)
                    isDictionary = true;
                else parameterType = entity.GetType();
                break;
            }
        }
        else parameterType = this.parameters.GetType();

        string fixSetSql = null;
        bool isFixSetSql = false;
        var ormProvider = this.connection.OrmProvider;
        using var command = this.connection.CreateCommand();

        if (this.setFields != null)
        {
            var builder = new StringBuilder();
            var entityMapper = this.dbFactory.GetEntityMap(entityType);
            int index = 0;
            foreach (var setField in this.setFields)
            {
                if (!entityMapper.TryGetMemberMap(setField.MemberName, out var memberMapper))
                    continue;
                if (string.IsNullOrEmpty(setField.Value))
                    continue;
                if (index > 0) builder.Append(',');
                builder.Append(ormProvider.GetFieldName(memberMapper.FieldName));
                builder.Append('=');
                builder.Append(setField.Value);
                if (setField.DbParameters != null && setField.DbParameters.Count > 0)
                    setField.DbParameters.ForEach(f => command.Parameters.Add(f));
                index++;
            }
            if (index > 0)
            {
                builder.Insert(0, $"UPDATE {ormProvider.GetTableName(entityMapper.TableName)} SET ");
                fixSetSql = builder.ToString();
                isFixSetSql = true;
            }
        }

        if (isMulti)
        {
            this.bulkCount ??= 500;
            int result = 0, index = 0;
            Action<IDbCommand, IOrmProvider, StringBuilder, int, object> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildBatchCommandInitializer(entityType, isFixSetSql);
            else commandInitializer = this.BuildBatchCommandInitializer(entityType, parameterType, isFixSetSql);

            var sqlBuilder = new StringBuilder();
            foreach (var entity in entities)
            {
                if (index > 0) sqlBuilder.Append(';');
                if (isFixSetSql) sqlBuilder.Append(fixSetSql);
                commandInitializer.Invoke(command, this.connection.OrmProvider, sqlBuilder, index, entity);

                if (index >= this.bulkCount)
                {
                    command.CommandText = sqlBuilder.ToString();
                    command.CommandType = CommandType.Text;
                    this.connection.Open();
                    result += command.ExecuteNonQuery();
                    sqlBuilder.Clear();
                    index = 0;
                    continue;
                }
                index++;
            }
            if (index > 0)
            {
                command.CommandText = sqlBuilder.ToString();
                command.CommandType = CommandType.Text;
                this.connection.Open();
                result += command.ExecuteNonQuery();
            }
            command.Dispose();
            return result;
        }
        else
        {
            string sql = null;
            Func<IDbCommand, IOrmProvider, object, string> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildCommandInitializer(entityType, isFixSetSql);
            else commandInitializer = this.BuildCommandInitializer(entityType, parameterType, isFixSetSql);

            if (isFixSetSql)
                sql = fixSetSql + commandInitializer.Invoke(command, this.connection.OrmProvider, this.parameters);
            else sql = commandInitializer.Invoke(command, this.connection.OrmProvider, this.parameters);

            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.Transaction = this.transaction;
            this.connection.Open();
            var result = command.ExecuteNonQuery();
            command.Dispose();
            return result;
        }
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        bool isMulti = false;
        bool isDictionary = false;
        var entityType = typeof(TEntity);
        Type parameterType = null;
        IEnumerable entities = null;
        if (this.parameters is Dictionary<string, object> dict)
            isDictionary = true;
        else if (this.parameters is IEnumerable && parameterType != typeof(string))
        {
            isMulti = true;
            entities = this.parameters as IEnumerable;
            foreach (var entity in entities)
            {
                if (entity is Dictionary<string, object>)
                    isDictionary = true;
                else parameterType = entity.GetType();
                break;
            }
        }
        else parameterType = this.parameters.GetType();

        string fixSetSql = null;
        bool isFixSetSql = false;
        var ormProvider = this.connection.OrmProvider;
        using var cmd = this.connection.CreateCommand();

        if (this.setFields != null)
        {
            var builder = new StringBuilder();
            var entityMapper = this.dbFactory.GetEntityMap(entityType);
            int index = 0;
            foreach (var setField in this.setFields)
            {
                if (!entityMapper.TryGetMemberMap(setField.MemberName, out var memberMapper))
                    continue;
                if (string.IsNullOrEmpty(setField.Value))
                    continue;
                if (index > 0) builder.Append(',');
                builder.Append(ormProvider.GetFieldName(memberMapper.FieldName));
                builder.Append('=');
                builder.Append(setField.Value);
                if (setField.DbParameters != null && setField.DbParameters.Count > 0)
                    setField.DbParameters.ForEach(f => cmd.Parameters.Add(f));
                index++;
            }
            builder.Insert(0, $"UPDATE {ormProvider.GetTableName(entityMapper.TableName)} SET ");
            fixSetSql = builder.ToString();
            isFixSetSql = true;
        }

        if (isMulti)
        {
            this.bulkCount ??= 500;
            int result = 0, index = 0;
            Action<IDbCommand, IOrmProvider, StringBuilder, int, object> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildBatchCommandInitializer(entityType, isFixSetSql);
            else commandInitializer = this.BuildBatchCommandInitializer(entityType, parameterType, isFixSetSql);

            if (cmd is not DbCommand command)
                throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

            var sqlBuilder = new StringBuilder();
            foreach (var entity in entities)
            {
                if (index > 0) sqlBuilder.Append(';');
                if (isFixSetSql) sqlBuilder.Append(fixSetSql);
                commandInitializer.Invoke(command, this.connection.OrmProvider, sqlBuilder, index, entity);

                if (index >= this.bulkCount)
                {
                    command.CommandText = sqlBuilder.ToString();
                    command.CommandType = CommandType.Text;
                    await this.connection.OpenAsync(cancellationToken);
                    result += await command.ExecuteNonQueryAsync(cancellationToken);
                    sqlBuilder.Clear();
                    index = 0;
                    continue;
                }
                index++;
            }
            if (index > 0)
            {
                command.CommandText = sqlBuilder.ToString();
                command.CommandType = CommandType.Text;
                await this.connection.OpenAsync(cancellationToken);
                result += await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await command.DisposeAsync();
            return result;
        }
        else
        {
            string sql = null;
            Func<IDbCommand, IOrmProvider, object, string> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildCommandInitializer(entityType, isFixSetSql);
            else commandInitializer = this.BuildCommandInitializer(entityType, parameterType, isFixSetSql);

            if (isFixSetSql)
                sql = fixSetSql + commandInitializer.Invoke(cmd, this.connection.OrmProvider, this.parameters);
            else sql = commandInitializer.Invoke(cmd, this.connection.OrmProvider, this.parameters);

            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            cmd.Transaction = this.transaction;
            if (cmd is not DbCommand command)
                throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

            await this.connection.OpenAsync(cancellationToken);
            var result = await command.ExecuteNonQueryAsync(cancellationToken);
            await command.DisposeAsync();
            return result;
        }
    }
    public string ToSql(out List<IDbDataParameter> dbParameters)
    {
        dbParameters = null;
        bool isMulti = false;
        bool isDictionary = false;
        var entityType = typeof(TEntity);
        Type parameterType = null;
        IEnumerable entities = null;
        if (this.parameters is Dictionary<string, object> dict)
            isDictionary = true;
        else if (this.parameters is IEnumerable && parameterType != typeof(string))
        {
            isMulti = true;
            entities = this.parameters as IEnumerable;
            foreach (var entity in entities)
            {
                if (entity is Dictionary<string, object>)
                    isDictionary = true;
                else parameterType = entity.GetType();
                break;
            }
        }
        else parameterType = this.parameters.GetType();

        string fixSetSql = null;
        bool isFixSetSql = false;
        var ormProvider = this.connection.OrmProvider;
        using var command = this.connection.CreateCommand();

        if (this.setFields != null)
        {
            var builder = new StringBuilder();
            var entityMapper = this.dbFactory.GetEntityMap(entityType);
            int index = 0;
            foreach (var setField in this.setFields)
            {
                if (!entityMapper.TryGetMemberMap(setField.MemberName, out var memberMapper))
                    continue;
                if (string.IsNullOrEmpty(setField.Value))
                    continue;
                if (index > 0) builder.Append(',');
                builder.Append(ormProvider.GetFieldName(memberMapper.FieldName));
                builder.Append('=');
                builder.Append(setField.Value);
                if (setField.DbParameters != null && setField.DbParameters.Count > 0)
                    setField.DbParameters.ForEach(f => command.Parameters.Add(f));
                index++;
            }
            builder.Insert(0, $"UPDATE {ormProvider.GetTableName(entityMapper.TableName)} SET ");
            fixSetSql = builder.ToString();
            isFixSetSql = true;
        }

        if (isMulti)
        {
            this.bulkCount ??= 500;
            int index = 0;
            Action<IDbCommand, IOrmProvider, StringBuilder, int, object> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildBatchCommandInitializer(entityType, isFixSetSql);
            else commandInitializer = this.BuildBatchCommandInitializer(entityType, parameterType, isFixSetSql);

            var sqlBuilder = new StringBuilder();
            foreach (var entity in entities)
            {
                if (index > 0) sqlBuilder.Append(';');
                if (isFixSetSql) sqlBuilder.Append(fixSetSql);
                commandInitializer.Invoke(command, this.connection.OrmProvider, sqlBuilder, index, entity);

                if (index >= this.bulkCount)
                    break;
                index++;
            }
            string sql = null;
            if (index > 0)
                sql = sqlBuilder.ToString();
            if (command.Parameters != null && command.Parameters.Count > 0)
                dbParameters = command.Parameters.Cast<IDbDataParameter>().ToList();
            command.Dispose();
            return sql;
        }
        else
        {
            string sql = null;
            Func<IDbCommand, IOrmProvider, object, string> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildCommandInitializer(entityType, isFixSetSql);
            else commandInitializer = this.BuildCommandInitializer(entityType, parameterType, isFixSetSql);

            if (isFixSetSql)
                sql = fixSetSql + commandInitializer.Invoke(command, this.connection.OrmProvider, this.parameters);
            else sql = commandInitializer.Invoke(command, this.connection.OrmProvider, this.parameters);

            if (command.Parameters != null && command.Parameters.Count > 0)
                dbParameters = command.Parameters.Cast<IDbDataParameter>().ToList();
            command.Dispose();
            return sql;
        }
    }
    private Action<IDbCommand, IOrmProvider, StringBuilder, int, object> BuildBatchCommandInitializer(Type entityType, Type parameterType, bool isFixSetSql)
    {
        int cacheKey = 0;
        ConcurrentDictionary<int, object> commandInitializerCache = null;
        if (this.setFields == null)
        {
            cacheKey = HashCode.Combine("UpdateBatch", connection.OrmProvider, entityType, parameterType);
            commandInitializerCache = objCommandInitializerCache;
        }
        else
        {
            cacheKey = this.GetCacheKey("UpdateBatch", connection.OrmProvider, entityType, parameterType, isFixSetSql, this.setFields);
            commandInitializerCache = setFieldsCommandInitializerCache;
        }
        if (!commandInitializerCache.TryGetValue(cacheKey, out var commandInitializerDelegate))
        {
            int columnIndex = 0;
            var entityMapper = dbFactory.GetEntityMap(entityType);
            var parameterMapper = dbFactory.GetEntityMap(parameterType);
            var ormProvider = this.connection.OrmProvider;
            var commandExpr = Expression.Parameter(typeof(IDbCommand), "cmd");
            var ormProviderExpr = Expression.Parameter(typeof(IOrmProvider), "ormProvider");
            var builderExpr = Expression.Parameter(typeof(StringBuilder), "builder");
            var indexExpr = Expression.Parameter(typeof(int), "index");
            var parameterExpr = Expression.Parameter(typeof(object), "parameter");

            var typedParameterExpr = Expression.Variable(parameterType, "typedParameter");
            var parameterNameExpr = Expression.Variable(typeof(string), "parameterName");
            var blockParameters = new List<ParameterExpression>();
            var blockBodies = new List<Expression>();
            blockParameters.Add(typedParameterExpr);
            blockParameters.Add(parameterNameExpr);
            blockBodies.Add(Expression.Assign(typedParameterExpr, Expression.Convert(parameterExpr, parameterType)));

            var methodInfo1 = typeof(StringBuilder).GetMethod(nameof(StringBuilder.Append), new Type[] { typeof(char) });
            var methodInfo2 = typeof(StringBuilder).GetMethod(nameof(StringBuilder.Append), new Type[] { typeof(string) });
            var methodInfo3 = typeof(string).GetMethod(nameof(string.Concat), new Type[] { typeof(string), typeof(string) });

            if (!isFixSetSql)
                blockBodies.Add(Expression.Call(builderExpr, methodInfo2, Expression.Constant($"UPDATE {ormProvider.GetTableName(entityMapper.TableName)} SET ")));

            foreach (var parameterMemberMapper in parameterMapper.MemberMaps)
            {
                if (!entityMapper.TryGetMemberMap(parameterMemberMapper.MemberName, out var propMapper)
                    || propMapper.IsIgnore || propMapper.IsNavigation || propMapper.MemberType.IsEntityType())
                    continue;

                if (this.setFields != null)
                {
                    var setField = this.setFields.Find(f => f.MemberName == parameterMemberMapper.MemberName);
                    if (setField == null) continue;
                    if (!string.IsNullOrEmpty(setField.Value))
                        continue;
                }
                else
                {
                    if (propMapper.IsKey) continue;
                }

                var parameterName = ormProvider.ParameterPrefix + propMapper.MemberName;
                var suffixExpr = Expression.Call(indexExpr, typeof(int).GetMethod(nameof(int.ToString), Type.EmptyTypes));
                var concatExpr = Expression.Call(methodInfo3, Expression.Constant(parameterName), suffixExpr);
                blockBodies.Add(Expression.Assign(parameterNameExpr, concatExpr));

                if (isFixSetSql || columnIndex > 0)
                    blockBodies.Add(Expression.Call(builderExpr, methodInfo1, Expression.Constant(',')));
                blockBodies.Add(Expression.Call(builderExpr, methodInfo2, Expression.Constant(ormProvider.GetFieldName(propMapper.FieldName) + "=")));
                blockBodies.Add(Expression.Call(builderExpr, methodInfo2, parameterNameExpr));

                RepositoryHelper.AddParameter(commandExpr, ormProviderExpr, parameterNameExpr, typedParameterExpr, parameterMemberMapper.MemberName, propMapper.NativeDbType, blockBodies);
                columnIndex++;
            }
            columnIndex = 0;
            blockBodies.Add(Expression.Call(builderExpr, methodInfo2, Expression.Constant(" WHERE ")));
            foreach (var keyMapper in entityMapper.KeyMembers)
            {
                if (!parameterMapper.TryGetMemberMap(keyMapper.MemberName, out var parameterMemberMapper))
                    throw new ArgumentNullException($"参数类型{parameterType.FullName}缺少主键字段{keyMapper.MemberName}", "parameters");

                if (columnIndex > 0)
                    blockBodies.Add(Expression.Call(builderExpr, methodInfo2, Expression.Constant(" AND ")));
                var fieldExpr = Expression.Constant(ormProvider.GetFieldName(keyMapper.FieldName) + "=");
                blockBodies.Add(Expression.Call(builderExpr, methodInfo2, fieldExpr));

                var parameterName = ormProvider.ParameterPrefix + "k" + keyMapper.MemberName;
                var suffixExpr = Expression.Call(indexExpr, typeof(int).GetMethod(nameof(int.ToString), Type.EmptyTypes));
                var concatExpr = Expression.Call(methodInfo3, Expression.Constant(parameterName), suffixExpr);
                blockBodies.Add(Expression.Assign(parameterNameExpr, concatExpr));
                blockBodies.Add(Expression.Call(builderExpr, methodInfo2, parameterNameExpr));

                RepositoryHelper.AddParameter(commandExpr, ormProviderExpr, parameterNameExpr, typedParameterExpr, parameterMemberMapper.MemberName, keyMapper.NativeDbType, blockBodies);
                columnIndex++;
            }
            commandInitializerDelegate = Expression.Lambda<Action<IDbCommand, IOrmProvider, StringBuilder, int, object>>(Expression.Block(blockParameters, blockBodies), commandExpr, ormProviderExpr, builderExpr, indexExpr, parameterExpr).Compile();
            commandInitializerCache.TryAdd(cacheKey, commandInitializerDelegate);
        }
        return (Action<IDbCommand, IOrmProvider, StringBuilder, int, object>)commandInitializerDelegate;
    }
    private Func<IDbCommand, IOrmProvider, object, string> BuildCommandInitializer(Type entityType, Type parameterType, bool isFixSetSql)
    {
        int cacheKey = 0;
        ConcurrentDictionary<int, object> commandInitializerCache = null;
        if (this.setFields == null)
        {
            cacheKey = HashCode.Combine("Update", connection.OrmProvider, entityType, parameterType);
            commandInitializerCache = objCommandInitializerCache;
        }
        else
        {
            cacheKey = this.GetCacheKey("Update", connection.OrmProvider, entityType, parameterType, isFixSetSql, this.setFields);
            commandInitializerCache = setFieldsCommandInitializerCache;
        }
        if (!commandInitializerCache.TryGetValue(cacheKey, out var commandInitializerDelegate))
        {
            int columnIndex = 0;
            var entityMapper = dbFactory.GetEntityMap(entityType);
            var parameterMapper = dbFactory.GetEntityMap(parameterType);
            var ormProvider = this.connection.OrmProvider;
            var commandExpr = Expression.Parameter(typeof(IDbCommand), "cmd");
            var ormProviderExpr = Expression.Parameter(typeof(IOrmProvider), "ormProvider");
            var parameterExpr = Expression.Parameter(typeof(object), "parameter");
            var typedParameterExpr = Expression.Parameter(parameterType, "typedParameter");

            var blockParameters = new List<ParameterExpression>();
            var blockBodies = new List<Expression>();
            blockParameters.Add(typedParameterExpr);
            blockBodies.Add(Expression.Assign(typedParameterExpr, Expression.Convert(parameterExpr, parameterType)));

            var methodInfo1 = typeof(StringBuilder).GetMethod(nameof(StringBuilder.Append), new Type[] { typeof(char) });
            var methodInfo2 = typeof(StringBuilder).GetMethod(nameof(StringBuilder.Append), new Type[] { typeof(string) });
            var methodInfo3 = typeof(string).GetMethod(nameof(string.Concat), new Type[] { typeof(string), typeof(string) });

            var sqlBuilder = new StringBuilder();
            if (!isFixSetSql)
                sqlBuilder.Append($"UPDATE {ormProvider.GetTableName(entityMapper.TableName)} SET ");
            foreach (var parameterMemberMapper in parameterMapper.MemberMaps)
            {
                if (!entityMapper.TryGetMemberMap(parameterMemberMapper.MemberName, out var propMapper)
                    || propMapper.IsIgnore || propMapper.IsNavigation || propMapper.MemberType.IsEntityType())
                    continue;

                if (this.setFields != null)
                {
                    var setField = this.setFields.Find(f => f.MemberName == parameterMemberMapper.MemberName);
                    if (setField == null) continue;
                    if (!string.IsNullOrEmpty(setField.Value)) continue;
                }
                else
                {
                    if (propMapper.IsKey) continue;
                }

                if (isFixSetSql || columnIndex > 0)
                    sqlBuilder.Append(',');
                var parameterName = ormProvider.ParameterPrefix + propMapper.MemberName;
                sqlBuilder.Append($"{ormProvider.GetFieldName(propMapper.FieldName)}={parameterName}");
                var parameterNameExpr = Expression.Constant(parameterName);
                RepositoryHelper.AddParameter(commandExpr, ormProviderExpr, parameterNameExpr, typedParameterExpr, parameterMemberMapper.MemberName, propMapper.NativeDbType, blockBodies);
                columnIndex++;
            }
            columnIndex = 0;
            sqlBuilder.Append(" WHERE ");
            foreach (var keyMapper in entityMapper.KeyMembers)
            {
                if (!parameterMapper.TryGetMemberMap(keyMapper.MemberName, out var parameterMemberMapper))
                    throw new ArgumentNullException($"参数类型{parameterType.FullName}缺少主键字段{keyMapper.MemberName}", "parameters");

                if (columnIndex > 0)
                    sqlBuilder.Append(" AND ");
                var parameterName = ormProvider.ParameterPrefix + "k" + keyMapper.MemberName;
                sqlBuilder.Append($"{ormProvider.GetFieldName(keyMapper.FieldName)}={parameterName}");
                var parameterNameExpr = Expression.Constant(parameterName);
                RepositoryHelper.AddParameter(commandExpr, ormProviderExpr, parameterNameExpr, typedParameterExpr, parameterMemberMapper.MemberName, keyMapper.NativeDbType, blockBodies);
                columnIndex++;
            }
            var resultLabelExpr = Expression.Label(typeof(string));
            var returnExpr = Expression.Constant(sqlBuilder.ToString());
            blockBodies.Add(Expression.Return(resultLabelExpr, returnExpr));
            blockBodies.Add(Expression.Label(resultLabelExpr, Expression.Constant(null, typeof(string))));

            commandInitializerDelegate = Expression.Lambda<Func<IDbCommand, IOrmProvider, object, string>>(Expression.Block(blockParameters, blockBodies), commandExpr, ormProviderExpr, parameterExpr).Compile();
            commandInitializerCache.TryAdd(cacheKey, commandInitializerDelegate);
        }
        return (Func<IDbCommand, IOrmProvider, object, string>)commandInitializerDelegate;
    }
    private Action<IDbCommand, IOrmProvider, object> BuildCommandInitializer(string sql, Type entityType, Type parameterType)
    {
        var cacheKey = HashCode.Combine("Update", connection.OrmProvider, sql, entityType, parameterType);
        if (!sqlCommandInitializerCache.TryGetValue(cacheKey, out var commandInitializerDelegate))
        {
            var parameterMapper = dbFactory.GetEntityMap(parameterType);
            var ormProvider = this.connection.OrmProvider;
            var commandExpr = Expression.Parameter(typeof(IDbCommand), "cmd");
            var ormProviderExpr = Expression.Parameter(typeof(IOrmProvider), "ormProvider");
            var parameterExpr = Expression.Parameter(typeof(object), "parameter");
            var typedParameterExpr = Expression.Parameter(parameterType, "typedParameter");

            var blockParameters = new List<ParameterExpression>();
            var blockBodies = new List<Expression>();
            blockParameters.Add(typedParameterExpr);
            blockBodies.Add(Expression.Assign(typedParameterExpr, Expression.Convert(parameterExpr, parameterType)));

            foreach (var parameterMemberMapper in parameterMapper.MemberMaps)
            {
                var parameterName = ormProvider.ParameterPrefix + parameterMemberMapper.MemberName;
                if (!Regex.IsMatch(sql, parameterName + @"([^\p{L}\p{N}_]+|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant))
                    continue;
                var parameterNameExpr = Expression.Constant(parameterName);
                RepositoryHelper.AddParameter(commandExpr, ormProviderExpr, parameterNameExpr, typedParameterExpr, parameterMemberMapper.MemberName, null, blockBodies);
            }
            commandInitializerDelegate = Expression.Lambda<Action<IDbCommand, IOrmProvider, object>>(Expression.Block(blockParameters, blockBodies), commandExpr, ormProviderExpr, parameterExpr).Compile();
            sqlCommandInitializerCache.TryAdd(cacheKey, commandInitializerDelegate);
        }
        return (Action<IDbCommand, IOrmProvider, object>)commandInitializerDelegate;
    }
    private Action<IDbCommand, IOrmProvider, StringBuilder, int, object> BuildBatchCommandInitializer(Type entityType, bool isFixSetSql)
    {
        return (command, ormProvider, builder, index, parameter) =>
        {
            int updateIndex = 0;
            var dict = parameter as Dictionary<string, object>;
            var entityMapper = dbFactory.GetEntityMap(entityType);

            if (!isFixSetSql)
                builder.Append($"UPDATE {ormProvider.GetTableName(entityMapper.TableName)} SET ");

            foreach (var item in dict)
            {
                if (!entityMapper.TryGetMemberMap(item.Key, out var propMapper)
                    || propMapper.IsIgnore || propMapper.IsNavigation || propMapper.MemberType.IsEntityType())
                    continue;

                if (setFields != null)
                {
                    var setField = setFields.Find(f => f.MemberName == item.Key);
                    if (setField == null) continue;
                    if (!string.IsNullOrEmpty(setField.Value))
                        continue;
                }
                else
                {
                    if (propMapper.IsKey) continue;
                }

                if (isFixSetSql || updateIndex > 0)
                    builder.Append(',');

                var parameterName = ormProvider.ParameterPrefix + item.Key + index.ToString();
                builder.Append($"{ormProvider.GetFieldName(propMapper.FieldName)}={parameterName}");

                if (propMapper.NativeDbType.HasValue)
                    command.Parameters.Add(ormProvider.CreateParameter(parameterName, propMapper.NativeDbType.Value, item.Value));
                else command.Parameters.Add(ormProvider.CreateParameter(parameterName, item.Value));
                updateIndex++;
            }
            updateIndex = 0;
            builder.Append(" WHERE ");
            foreach (var keyMapper in entityMapper.KeyMembers)
            {
                if (!dict.ContainsKey(keyMapper.MemberName))
                    throw new ArgumentNullException($"字典参数中缺少主键字段{keyMapper.MemberName}", "parameters");

                if (updateIndex > 0)
                    builder.Append(',');
                var parameterName = ormProvider.ParameterPrefix + "k" + keyMapper.MemberName + index.ToString();
                builder.Append($"{ormProvider.GetFieldName(keyMapper.FieldName)}={parameterName}");

                if (keyMapper.NativeDbType.HasValue)
                    command.Parameters.Add(ormProvider.CreateParameter(parameterName, keyMapper.NativeDbType.Value, dict[keyMapper.MemberName]));
                else command.Parameters.Add(ormProvider.CreateParameter(parameterName, dict[keyMapper.MemberName]));
                updateIndex++;
            }
        };
    }
    private Func<IDbCommand, IOrmProvider, object, string> BuildCommandInitializer(Type entityType, bool isFixSetSql)
    {
        return (command, ormProvider, parameter) =>
        {
            int index = 0;
            var dict = parameter as Dictionary<string, object>;
            var entityMapper = dbFactory.GetEntityMap(entityType);
            var sqlBuilder = new StringBuilder();
            if (!isFixSetSql)
                sqlBuilder.Append($"UPDATE {ormProvider.GetTableName(entityMapper.TableName)} SET ");

            foreach (var item in dict)
            {
                if (!entityMapper.TryGetMemberMap(item.Key, out var propMapper)
                    || propMapper.IsIgnore || propMapper.IsNavigation || propMapper.MemberType.IsEntityType())
                    continue;

                if (setFields != null)
                {
                    var setField = setFields.Find(f => f.MemberName == item.Key);
                    if (setField == null) continue;
                    if (!string.IsNullOrEmpty(setField.Value))
                        continue;
                }
                else
                {
                    if (propMapper.IsKey) continue;
                }

                if (isFixSetSql || index > 0)
                    sqlBuilder.Append(',');

                var parameterName = ormProvider.ParameterPrefix + item.Key;
                sqlBuilder.Append($"{ormProvider.GetFieldName(propMapper.FieldName)}={parameterName}");

                if (propMapper.NativeDbType.HasValue)
                    command.Parameters.Add(ormProvider.CreateParameter(parameterName, propMapper.NativeDbType.Value, item.Value));
                else command.Parameters.Add(ormProvider.CreateParameter(parameterName, item.Value));
                index++;
            }

            index = 0;
            sqlBuilder.Append(" WHERE ");
            foreach (var keyMapper in entityMapper.KeyMembers)
            {
                if (!dict.ContainsKey(keyMapper.MemberName))
                    throw new ArgumentNullException($"字典参数中缺少主键字段{keyMapper.MemberName}", "parameters");

                if (index > 0)
                    sqlBuilder.Append(',');
                var parameterName = ormProvider.ParameterPrefix + "k" + keyMapper.MemberName;
                sqlBuilder.Append($"{ormProvider.GetFieldName(keyMapper.FieldName)}={parameterName}");

                if (keyMapper.NativeDbType.HasValue)
                    command.Parameters.Add(ormProvider.CreateParameter(parameterName, keyMapper.NativeDbType.Value, dict[keyMapper.MemberName]));
                else command.Parameters.Add(ormProvider.CreateParameter(parameterName, dict[keyMapper.MemberName]));
                index++;
            }
            return sqlBuilder.ToString();
        };
    }
    private Action<IDbCommand, IOrmProvider, object> BuildCommandInitializer(string sql)
    {
        return (command, ormProvider, parameter) =>
        {
            var dict = parameter as Dictionary<string, object>;
            foreach (var item in dict)
            {
                var parameterName = ormProvider.ParameterPrefix + item.Key;
                if (!Regex.IsMatch(sql, parameterName + @"([^\p{L}\p{N}_]+|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant))
                    continue;
                var dbParameter = ormProvider.CreateParameter(parameterName, dict[item.Key]);
                command.Parameters.Add(dbParameter);
            }
        };
    }
    private int GetCacheKey(string category, IOrmProvider ormProvider, Type entityType, Type parameterType, bool isFixSetSql, List<SetField> setFields)
    {
        var hashCode = new HashCode();
        hashCode.Add(category);
        hashCode.Add(ormProvider);
        hashCode.Add(entityType);
        hashCode.Add(parameterType);
        hashCode.Add(isFixSetSql);
        hashCode.Add(setFields.Count);
        foreach (var setField in setFields)
        {
            if (!string.IsNullOrEmpty(setField.Value))
                continue;
            hashCode.Add(setField.MemberName);
        }
        return hashCode.ToHashCode();
    }
}
class UpdateSetting<TEntity> : IUpdateSetting<TEntity>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly UpdateVisitor visitor;

    public UpdateSetting(UpdateVisitor visitor)
    {
        this.visitor = visitor;
        this.connection = visitor.connection;
        this.transaction = visitor.transaction;
    }
    public IUpdateSetting<TEntity> Set<TFields>(Expression<Func<TEntity, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateSetting<TEntity> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateSetting<TEntity> Set<TFields>(bool condition, Expression<Func<TEntity, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        if (condition)
            this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateSetting<TEntity> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        if (condition)
            this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateSetting<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        this.visitor.Where(predicate);
        return this;
    }
    public IUpdateSetting<TEntity> And(bool condition, Expression<Func<TEntity, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.And(predicate);
        return this;
    }
    public int Execute()
    {
        using var command = this.connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.Transaction = this.transaction;
        var sql = this.visitor.BuildSql(out var dbParameters);
        command.CommandText = sql;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => command.Parameters.Add(f));
        this.connection.Open();
        var result = command.ExecuteNonQuery();
        command.Dispose();
        return result;
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var cmd = this.connection.CreateCommand();
        cmd.CommandType = CommandType.Text;
        cmd.Transaction = this.transaction;
        var sql = this.visitor.BuildSql(out var dbParameters);
        cmd.CommandText = sql;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => cmd.Parameters.Add(f));
        if (cmd is not DbCommand command)
            throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

        await this.connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        await command.DisposeAsync();
        return result;
    }
    public string ToSql(out List<IDbDataParameter> dbParameters) => this.visitor.BuildSql(out dbParameters);
}
class UpdateFrom<TEntity, T1> : IUpdateFrom<TEntity, T1>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly UpdateVisitor visitor = null;

    public UpdateFrom(UpdateVisitor visitor)
    {
        this.visitor = visitor;
        this.connection = visitor.connection;
        this.transaction = visitor.transaction;
    }
    public IUpdateFrom<TEntity, T1> Set<TFields>(Expression<Func<TEntity, T1, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateFrom<TEntity, T1> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateFrom<TEntity, T1> Set<TFields>(bool condition, Expression<Func<TEntity, T1, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        if (condition)
            this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateFrom<TEntity, T1> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        if (condition)
            this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateFrom<TEntity, T1> Where(Expression<Func<TEntity, T1, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        this.visitor.Where(predicate);
        return this;
    }
    public IUpdateFrom<TEntity, T1> And(bool condition, Expression<Func<TEntity, T1, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.Where(predicate);
        return this;
    }
    public int Execute()
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var command = this.connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => command.Parameters.Add(f));
        this.connection.Open();
        var result = command.ExecuteNonQuery();
        command.Dispose();
        return result;
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var cmd = this.connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => cmd.Parameters.Add(f));
        if (cmd is not DbCommand command)
            throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

        await this.connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        await command.DisposeAsync();
        return result;
    }
    public string ToSql(out List<IDbDataParameter> dbParameters) => this.visitor.BuildSql(out dbParameters);
}
class UpdateJoin<TEntity, T1> : IUpdateJoin<TEntity, T1>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly UpdateVisitor visitor = null;

    public UpdateJoin(UpdateVisitor visitor)
    {
        this.visitor = visitor;
        this.connection = visitor.connection;
        this.transaction = visitor.transaction;
    }
    public IUpdateJoin<TEntity, T1, T2> InnerJoin<T2>(Expression<Func<TEntity, T1, T2, bool>> joinOn)
    {
        if (joinOn == null)
            throw new ArgumentNullException(nameof(joinOn));

        this.visitor.Join("INNER JOIN", typeof(T2), joinOn);
        return new UpdateJoin<TEntity, T1, T2>(this.visitor);
    }
    public IUpdateJoin<TEntity, T1, T2> LeftJoin<T2>(Expression<Func<TEntity, T1, T2, bool>> joinOn)
    {
        if (joinOn == null)
            throw new ArgumentNullException(nameof(joinOn));

        this.visitor.Join("LEFT JOIN", typeof(T2), joinOn);
        return new UpdateJoin<TEntity, T1, T2>(this.visitor);
    }
    public IUpdateJoin<TEntity, T1> Set<TFields>(Expression<Func<TEntity, T1, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateJoin<TEntity, T1> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateJoin<TEntity, T1> Set<TFields>(bool condition, Expression<Func<TEntity, T1, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        if (condition)
            this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateJoin<TEntity, T1> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        if (condition)
            this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateJoin<TEntity, T1> Where(Expression<Func<TEntity, T1, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        this.visitor.Where(predicate);
        return this;
    }
    public IUpdateJoin<TEntity, T1> And(bool condition, Expression<Func<TEntity, T1, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.Where(predicate);
        return this;
    }
    public int Execute()
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var command = this.connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => command.Parameters.Add(f));
        this.connection.Open();
        var result = command.ExecuteNonQuery();
        command.Dispose();
        return result;
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var cmd = this.connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => cmd.Parameters.Add(f));
        if (cmd is not DbCommand command)
            throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

        await this.connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        await command.DisposeAsync();
        return result;
    }
    public string ToSql(out List<IDbDataParameter> dbParameters) => this.visitor.BuildSql(out dbParameters);
}
class UpdateFrom<TEntity, T1, T2> : IUpdateFrom<TEntity, T1, T2>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly UpdateVisitor visitor = null;

    public UpdateFrom(UpdateVisitor visitor)
    {
        this.visitor = visitor;
        this.connection = visitor.connection;
        this.transaction = visitor.transaction;
    }
    public IUpdateFrom<TEntity, T1, T2> Set<TFields>(Expression<Func<TEntity, T1, T2, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2> Set<TFields>(bool condition, Expression<Func<TEntity, T1, T2, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        if (condition)
            this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        if (condition)
            this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2> Where(Expression<Func<TEntity, T1, T2, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        this.visitor.Where(predicate);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2> And(bool condition, Expression<Func<TEntity, T1, T2, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.Where(predicate);
        return this;
    }
    public int Execute()
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var command = this.connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => command.Parameters.Add(f));
        this.connection.Open();
        var result = command.ExecuteNonQuery();
        command.Dispose();
        return result;
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var cmd = this.connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => cmd.Parameters.Add(f));
        if (cmd is not DbCommand command)
            throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

        await this.connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        await command.DisposeAsync();
        return result;
    }
    public string ToSql(out List<IDbDataParameter> dbParameters) => this.visitor.BuildSql(out dbParameters);
}
class UpdateJoin<TEntity, T1, T2> : IUpdateJoin<TEntity, T1, T2>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly UpdateVisitor visitor = null;

    public UpdateJoin(UpdateVisitor visitor)
    {
        this.visitor = visitor;
        this.connection = visitor.connection;
        this.transaction = visitor.transaction;
    }
    public IUpdateJoin<TEntity, T1, T2, T3> InnerJoin<T3>(Expression<Func<TEntity, T1, T2, T3, bool>> joinOn)
    {
        if (joinOn == null)
            throw new ArgumentNullException(nameof(joinOn));

        this.visitor.Join("INNER JOIN", typeof(T3), joinOn);
        return new UpdateJoin<TEntity, T1, T2, T3>(this.visitor);
    }
    public IUpdateJoin<TEntity, T1, T2, T3> LeftJoin<T3>(Expression<Func<TEntity, T1, T2, T3, bool>> joinOn)
    {
        if (joinOn == null)
            throw new ArgumentNullException(nameof(joinOn));

        this.visitor.Join("LEFT JOIN", typeof(T3), joinOn);
        return new UpdateJoin<TEntity, T1, T2, T3>(this.visitor);
    }
    public IUpdateJoin<TEntity, T1, T2> Set<TFields>(Expression<Func<TEntity, T1, T2, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2> Set<TFields>(bool condition, Expression<Func<TEntity, T1, T2, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        if (condition)
            this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        if (condition)
            this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2> Where(Expression<Func<TEntity, T1, T2, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        this.visitor.Where(predicate);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2> And(bool condition, Expression<Func<TEntity, T1, T2, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.Where(predicate);
        return this;
    }
    public int Execute()
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var command = this.connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => command.Parameters.Add(f));
        this.connection.Open();
        var result = command.ExecuteNonQuery();
        command.Dispose();
        return result;
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var cmd = this.connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => cmd.Parameters.Add(f));
        if (cmd is not DbCommand command)
            throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

        await this.connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        await command.DisposeAsync();
        return result;
    }
    public string ToSql(out List<IDbDataParameter> dbParameters) => this.visitor.BuildSql(out dbParameters);
}
class UpdateFrom<TEntity, T1, T2, T3> : IUpdateFrom<TEntity, T1, T2, T3>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly UpdateVisitor visitor = null;

    public UpdateFrom(UpdateVisitor visitor)
    {
        this.visitor = visitor;
        this.connection = visitor.connection;
        this.transaction = visitor.transaction;
    }
    public IUpdateFrom<TEntity, T1, T2, T3> Set<TFields>(Expression<Func<TEntity, T1, T2, T3, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3> Set<TFields>(bool condition, Expression<Func<TEntity, T1, T2, T3, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        if (condition)
            this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        if (condition)
            this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3> Where(Expression<Func<TEntity, T1, T2, T3, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        this.visitor.Where(predicate);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3> And(bool condition, Expression<Func<TEntity, T1, T2, T3, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.Where(predicate);
        return this;
    }
    public int Execute()
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var command = this.connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => command.Parameters.Add(f));
        this.connection.Open();
        var result = command.ExecuteNonQuery();
        command.Dispose();
        return result;
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var cmd = this.connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => cmd.Parameters.Add(f));
        if (cmd is not DbCommand command)
            throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

        await this.connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        await command.DisposeAsync();
        return result;
    }
    public string ToSql(out List<IDbDataParameter> dbParameters) => this.visitor.BuildSql(out dbParameters);
}
class UpdateJoin<TEntity, T1, T2, T3> : IUpdateJoin<TEntity, T1, T2, T3>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly UpdateVisitor visitor = null;

    public UpdateJoin(UpdateVisitor visitor)
    {
        this.visitor = visitor;
        this.connection = visitor.connection;
        this.transaction = visitor.transaction;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4> InnerJoin<T4>(Expression<Func<TEntity, T1, T2, T3, T4, bool>> joinOn)
    {
        if (joinOn == null)
            throw new ArgumentNullException(nameof(joinOn));

        this.visitor.Join("INNER JOIN", typeof(T4), joinOn);
        return new UpdateJoin<TEntity, T1, T2, T3, T4>(this.visitor);
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4> LeftJoin<T4>(Expression<Func<TEntity, T1, T2, T3, T4, bool>> joinOn)
    {
        if (joinOn == null)
            throw new ArgumentNullException(nameof(joinOn));

        this.visitor.Join("LEFT JOIN", typeof(T4), joinOn);
        return new UpdateJoin<TEntity, T1, T2, T3, T4>(this.visitor);
    }
    public IUpdateJoin<TEntity, T1, T2, T3> Set<TFields>(Expression<Func<TEntity, T1, T2, T3, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3> Set<TFields>(bool condition, Expression<Func<TEntity, T1, T2, T3, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        if (condition)
            this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        if (condition)
            this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3> Where(Expression<Func<TEntity, T1, T2, T3, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        this.visitor.Where(predicate);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3> And(bool condition, Expression<Func<TEntity, T1, T2, T3, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.Where(predicate);
        return this;
    }
    public int Execute()
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var command = this.connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => command.Parameters.Add(f));
        this.connection.Open();
        var result = command.ExecuteNonQuery();
        command.Dispose();
        return result;
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var cmd = this.connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => cmd.Parameters.Add(f));
        if (cmd is not DbCommand command)
            throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

        await this.connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        await command.DisposeAsync();
        return result;
    }
    public string ToSql(out List<IDbDataParameter> dbParameters) => this.visitor.BuildSql(out dbParameters);
}
class UpdateFrom<TEntity, T1, T2, T3, T4> : IUpdateFrom<TEntity, T1, T2, T3, T4>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly UpdateVisitor visitor = null;

    public UpdateFrom(UpdateVisitor visitor)
    {
        this.visitor = visitor;
        this.connection = visitor.connection;
        this.transaction = visitor.transaction;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4> Set<TFields>(Expression<Func<TEntity, T1, T2, T3, T4, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4> Set<TFields>(bool condition, Expression<Func<TEntity, T1, T2, T3, T4, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        if (condition)
            this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        if (condition)
            this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4> Where(Expression<Func<TEntity, T1, T2, T3, T4, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        this.visitor.Where(predicate);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4> And(bool condition, Expression<Func<TEntity, T1, T2, T3, T4, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.Where(predicate);
        return this;
    }
    public int Execute()
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var command = this.connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => command.Parameters.Add(f));
        this.connection.Open();
        var result = command.ExecuteNonQuery();
        command.Dispose();
        return result;
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var cmd = this.connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => cmd.Parameters.Add(f));
        if (cmd is not DbCommand command)
            throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

        await this.connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        await command.DisposeAsync();
        return result;
    }
    public string ToSql(out List<IDbDataParameter> dbParameters) => this.visitor.BuildSql(out dbParameters);
}
class UpdateJoin<TEntity, T1, T2, T3, T4> : IUpdateJoin<TEntity, T1, T2, T3, T4>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly UpdateVisitor visitor = null;

    public UpdateJoin(UpdateVisitor visitor)
    {
        this.visitor = visitor;
        this.connection = visitor.connection;
        this.transaction = visitor.transaction;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4, T5> InnerJoin<T5>(Expression<Func<TEntity, T1, T2, T3, T4, T5, bool>> joinOn)
    {
        if (joinOn == null)
            throw new ArgumentNullException(nameof(joinOn));

        this.visitor.Join("INNER JOIN", typeof(T5), joinOn);
        return new UpdateJoin<TEntity, T1, T2, T3, T4, T5>(this.visitor);
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4, T5> LeftJoin<T5>(Expression<Func<TEntity, T1, T2, T3, T4, T5, bool>> joinOn)
    {
        if (joinOn == null)
            throw new ArgumentNullException(nameof(joinOn));

        this.visitor.Join("LEFT JOIN", typeof(T5), joinOn);
        return new UpdateJoin<TEntity, T1, T2, T3, T4, T5>(this.visitor);
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4> Set<TFields>(Expression<Func<TEntity, T1, T2, T3, T4, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4> Set<TFields>(bool condition, Expression<Func<TEntity, T1, T2, T3, T4, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        if (condition)
            this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        if (condition)
            this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4> Where(Expression<Func<TEntity, T1, T2, T3, T4, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        this.visitor.Where(predicate);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4> And(bool condition, Expression<Func<TEntity, T1, T2, T3, T4, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.Where(predicate);
        return this;
    }
    public int Execute()
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var command = this.connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => command.Parameters.Add(f));
        this.connection.Open();
        var result = command.ExecuteNonQuery();
        command.Dispose();
        return result;
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var cmd = this.connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => cmd.Parameters.Add(f));
        if (cmd is not DbCommand command)
            throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

        await this.connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        await command.DisposeAsync();
        return result;
    }
    public string ToSql(out List<IDbDataParameter> dbParameters) => this.visitor.BuildSql(out dbParameters);
}
class UpdateFrom<TEntity, T1, T2, T3, T4, T5> : IUpdateFrom<TEntity, T1, T2, T3, T4, T5>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly UpdateVisitor visitor = null;

    public UpdateFrom(UpdateVisitor visitor)
    {
        this.visitor = visitor;
        this.connection = visitor.connection;
        this.transaction = visitor.transaction;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4, T5> Set<TFields>(Expression<Func<TEntity, T1, T2, T3, T4, T5, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4, T5> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4, T5> Set<TFields>(bool condition, Expression<Func<TEntity, T1, T2, T3, T4, T5, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        if (condition)
            this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4, T5> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        if (condition)
            this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4, T5> Where(Expression<Func<TEntity, T1, T2, T3, T4, T5, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        this.visitor.Where(predicate);
        return this;
    }
    public IUpdateFrom<TEntity, T1, T2, T3, T4, T5> And(bool condition, Expression<Func<TEntity, T1, T2, T3, T4, T5, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.Where(predicate);
        return this;
    }
    public int Execute()
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var command = this.connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => command.Parameters.Add(f));
        this.connection.Open();
        var result = command.ExecuteNonQuery();
        command.Dispose();
        return result;
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var cmd = this.connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => cmd.Parameters.Add(f));
        if (cmd is not DbCommand command)
            throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

        await this.connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        await command.DisposeAsync();
        return result;
    }
    public string ToSql(out List<IDbDataParameter> dbParameters) => this.visitor.BuildSql(out dbParameters);
}
class UpdateJoin<TEntity, T1, T2, T3, T4, T5> : IUpdateJoin<TEntity, T1, T2, T3, T4, T5>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly UpdateVisitor visitor = null;

    public UpdateJoin(UpdateVisitor visitor)
    {
        this.visitor = visitor;
        this.connection = visitor.connection;
        this.transaction = visitor.transaction;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4, T5> Set<TFields>(Expression<Func<TEntity, T1, T2, T3, T4, T5, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4, T5> Set<TField>(Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4, T5> Set<TFields>(bool condition, Expression<Func<TEntity, T1, T2, T3, T4, T5, TFields>> fieldsExpr)
    {
        if (fieldsExpr == null)
            throw new ArgumentNullException(nameof(fieldsExpr));
        if (fieldsExpr.Body.NodeType != ExpressionType.New && fieldsExpr.Body.NodeType != ExpressionType.MemberInit)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldsExpr)},只支持New或MemberInit类型表达式");

        if (condition)
            this.visitor.Set(fieldsExpr);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4, T5> Set<TField>(bool condition, Expression<Func<TEntity, TField>> fieldExpr, TField fieldValue)
    {
        if (fieldExpr == null)
            throw new ArgumentNullException(nameof(fieldExpr));
        if (fieldValue == null)
            throw new ArgumentNullException(nameof(fieldValue));
        if (fieldExpr.Body.NodeType != ExpressionType.MemberAccess)
            throw new NotSupportedException($"不支持的表达式{nameof(fieldExpr)},只支持MemberAccess类型表达式");

        if (condition)
            this.visitor.Set(fieldExpr, fieldValue);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4, T5> Where(Expression<Func<TEntity, T1, T2, T3, T4, T5, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        this.visitor.Where(predicate);
        return this;
    }
    public IUpdateJoin<TEntity, T1, T2, T3, T4, T5> And(bool condition, Expression<Func<TEntity, T1, T2, T3, T4, T5, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.Where(predicate);
        return this;
    }
    public int Execute()
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var command = this.connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => command.Parameters.Add(f));
        this.connection.Open();
        var result = command.ExecuteNonQuery();
        command.Dispose();
        return result;
    }
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sql = this.visitor.BuildSql(out var dbParameters);
        using var cmd = this.connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        cmd.Transaction = this.transaction;
        if (dbParameters != null && dbParameters.Count > 0)
            dbParameters.ForEach(f => cmd.Parameters.Add(f));
        if (cmd is not DbCommand command)
            throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

        await this.connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteNonQueryAsync(cancellationToken);
        await command.DisposeAsync();
        return result;
    }
    public string ToSql(out List<IDbDataParameter> dbParameters) => this.visitor.BuildSql(out dbParameters);
}
class SetField
{
    public string MemberName { get; set; }
    public string Value { get; set; }
    public List<IDbDataParameter> DbParameters { get; set; }
}