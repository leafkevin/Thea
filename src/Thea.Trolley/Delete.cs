﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Thea.Orm;

namespace Thea.Trolley;

class Delete<TEntity> : IDelete<TEntity>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly IOrmProvider ormProvider;
    private readonly IEntityMapProvider mapProvider;
    private readonly bool isParameterized;

    public Delete(TheaConnection connection, IDbTransaction transaction, IOrmProvider ormProvider, IEntityMapProvider mapProvider, bool isParameterized = false)
    {
        this.connection = connection;
        this.transaction = transaction;
        this.ormProvider = ormProvider;
        this.mapProvider = mapProvider;
        this.isParameterized = isParameterized;
    }

    public IDeleted<TEntity> Where(object keys)
    {
        if (keys == null)
            throw new ArgumentNullException(nameof(keys));

        return new Deleted<TEntity>(this.connection, this.transaction, this.ormProvider, this.mapProvider, keys);
    }
    public IDeleting<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        var visitor = this.ormProvider.NewDeleteVisitor(this.connection.DbKey, this.mapProvider, typeof(TEntity), this.isParameterized);
        visitor.Where(predicate);
        return new Deleting<TEntity>(this.connection, this.transaction, visitor);
    }
}
class Deleted<TEntity> : IDeleted<TEntity>
{
    private static ConcurrentDictionary<int, object> commandInitializerCache = new();
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly IOrmProvider ormProvider;
    private readonly IEntityMapProvider mapProvider;
    private object parameters = null;

    public Deleted(TheaConnection connection, IDbTransaction transaction, IOrmProvider ormProvider, IEntityMapProvider mapProvider, object parameters)
    {
        this.connection = connection;
        this.transaction = transaction;
        this.ormProvider = ormProvider;
        this.mapProvider = mapProvider;
        this.parameters = parameters;
    }
    public int Execute()
    {
        bool isMulti = false;
        bool isDictionary = false;
        var entityType = typeof(TEntity);
        var parameterType = this.parameters.GetType();
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

        if (isMulti)
        {
            Action<IDbCommand, IOrmProvider, StringBuilder, int, object> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildBatchCommandInitializer(entityType);
            else commandInitializer = this.BuildBatchCommandInitializer(entityType, parameterType);

            int index = 0;
            var sqlBuilder = new StringBuilder();
            using var command = this.connection.CreateCommand();
            foreach (var entity in entities)
            {
                commandInitializer.Invoke(command, this.ormProvider, sqlBuilder, index, entity);
                index++;
            }
            command.CommandText = sqlBuilder.ToString();
            command.CommandType = CommandType.Text;
            command.Transaction = this.transaction;
            this.connection.Open();
            var result = command.ExecuteNonQuery();
            command.Dispose();
            return result;
        }
        else
        {
            string sql = null;
            using var command = this.connection.CreateCommand();
            Func<IDbCommand, IOrmProvider, object, string> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildCommandInitializer(entityType);
            else commandInitializer = this.BuildCommandInitializer(entityType, parameterType);
            sql = commandInitializer.Invoke(command, this.ormProvider, this.parameters);

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
        var parameterType = this.parameters.GetType();
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

        if (isMulti)
        {
            Action<IDbCommand, IOrmProvider, StringBuilder, int, object> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildBatchCommandInitializer(entityType);
            else commandInitializer = this.BuildBatchCommandInitializer(entityType, parameterType);

            int index = 0;
            var sqlBuilder = new StringBuilder();
            using var cmd = this.connection.CreateCommand();
            foreach (var entity in entities)
            {
                commandInitializer.Invoke(cmd, this.ormProvider, sqlBuilder, index, entity);
                index++;
            }
            cmd.CommandText = sqlBuilder.ToString();
            cmd.CommandType = CommandType.Text;
            cmd.Transaction = this.transaction;
            if (cmd is not DbCommand command)
                throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

            await this.connection.OpenAsync(cancellationToken);
            var result = await command.ExecuteNonQueryAsync(cancellationToken);
            command.Dispose();
            return result;
        }
        else
        {
            string sql = null;
            using var cmd = this.connection.CreateCommand();
            Func<IDbCommand, IOrmProvider, object, string> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildCommandInitializer(entityType);
            else commandInitializer = this.BuildCommandInitializer(entityType, parameterType);
            sql = commandInitializer.Invoke(cmd, this.ormProvider, this.parameters);

            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            cmd.Transaction = this.transaction;
            if (cmd is not DbCommand command)
                throw new NotSupportedException("当前数据库驱动不支持异步SQL查询");

            await this.connection.OpenAsync(cancellationToken);
            var result = await command.ExecuteNonQueryAsync(cancellationToken);
            command.Dispose();
            return result;
        }
    }
    public string ToSql(out List<IDbDataParameter> dbParameters)
    {
        dbParameters = null;
        bool isDictionary = false;
        bool isMulti = false;
        var entityType = typeof(TEntity);
        var parameterType = this.parameters.GetType();
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

        string sql = null;
        if (isMulti)
        {
            Action<IDbCommand, IOrmProvider, StringBuilder, int, object> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildBatchCommandInitializer(entityType);
            else commandInitializer = this.BuildBatchCommandInitializer(entityType, parameterType);

            int index = 0;
            var sqlBuilder = new StringBuilder();
            using var command = this.connection.CreateCommand();
            foreach (var entity in entities)
            {
                commandInitializer.Invoke(command, this.ormProvider, sqlBuilder, index, entity);
                index++;
            }
            sql = sqlBuilder.ToString();
            if (command.Parameters != null && command.Parameters.Count > 0)
                dbParameters = command.Parameters.Cast<IDbDataParameter>().ToList();
            command.Dispose();
        }
        else
        {
            using var command = this.connection.CreateCommand();
            Func<IDbCommand, IOrmProvider, object, string> commandInitializer = null;
            if (isDictionary)
                commandInitializer = this.BuildCommandInitializer(entityType);
            else commandInitializer = this.BuildCommandInitializer(entityType, parameterType);
            sql = commandInitializer.Invoke(command, this.ormProvider, this.parameters);
            if (command.Parameters != null && command.Parameters.Count > 0)
                dbParameters = command.Parameters.Cast<IDbDataParameter>().ToList();
            command.Dispose();
        }
        return sql;
    }
    private Action<IDbCommand, IOrmProvider, StringBuilder, int, object> BuildBatchCommandInitializer(Type entityType, Type parameterType)
    {
        var cacheKey = HashCode.Combine("DeleteBatch", this.connection, this.ormProvider, string.Empty, entityType, parameterType);
        if (!commandInitializerCache.TryGetValue(cacheKey, out var commandInitializerDelegate))
        {
            var entityMapper = this.mapProvider.GetEntityMap(entityType);
            var parameterMapper = this.mapProvider.GetEntityMap(parameterType);
            var commandExpr = Expression.Parameter(typeof(IDbCommand), "cmd");
            var ormProviderExpr = Expression.Parameter(typeof(IOrmProvider), "ormProvider");
            var builderExpr = Expression.Parameter(typeof(StringBuilder), "builder");
            var indexExpr = Expression.Parameter(typeof(int), "index");
            var parameterExpr = Expression.Parameter(typeof(object), "parameter");

            var blockParameters = new List<ParameterExpression>();
            var blockBodies = new List<Expression>();
            ParameterExpression typedParameterExpr = null;
            var parameterNameExpr = Expression.Variable(typeof(string), "parameterName");
            bool isEntityType = false;

            if (parameterType.IsEntityType())
            {
                isEntityType = true;
                typedParameterExpr = Expression.Variable(parameterType, "typedParameter");
                blockParameters.Add(typedParameterExpr);
                blockBodies.Add(Expression.Assign(typedParameterExpr, Expression.Convert(parameterExpr, parameterType)));
            }
            else
            {
                if (entityMapper.KeyMembers.Count > 1)
                    throw new NotSupportedException($"模型{entityType.FullName}有多个主键字段，不能使用单个值类型{parameterType.FullName}作为参数");
            }
            blockParameters.Add(parameterNameExpr);

            var methodInfo1 = typeof(StringBuilder).GetMethod(nameof(StringBuilder.Append), new Type[] { typeof(char) });
            var methodInfo2 = typeof(StringBuilder).GetMethod(nameof(StringBuilder.Append), new Type[] { typeof(string) });
            var methodInfo3 = typeof(string).GetMethod(nameof(string.Concat), new Type[] { typeof(string), typeof(string) });

            var addCommaExpr = Expression.Call(builderExpr, methodInfo1, Expression.Constant(';'));
            var greatThenExpr = Expression.GreaterThan(indexExpr, Expression.Constant(0, typeof(int)));
            blockBodies.Add(Expression.IfThen(greatThenExpr, addCommaExpr));
            var sql = $"DELETE FROM {this.ormProvider.GetFieldName(entityMapper.TableName)} WHERE ";
            blockBodies.Add(Expression.Call(builderExpr, methodInfo2, Expression.Constant(sql)));

            int index = 0;
            foreach (var keyMapper in entityMapper.KeyMembers)
            {
                if (index > 0)
                    blockBodies.Add(Expression.Call(builderExpr, methodInfo2, Expression.Constant(" AND ")));

                var parameterName = this.ormProvider.ParameterPrefix + keyMapper.MemberName;
                var suffixExpr = Expression.Call(indexExpr, typeof(int).GetMethod(nameof(int.ToString), Type.EmptyTypes));
                var concatExpr = Expression.Call(methodInfo3, Expression.Constant(parameterName), suffixExpr);
                blockBodies.Add(Expression.Assign(parameterNameExpr, concatExpr));

                var constantExpr = Expression.Constant(this.ormProvider.GetFieldName(keyMapper.FieldName) + "=");
                blockBodies.Add(Expression.Call(builderExpr, methodInfo2, constantExpr));
                blockBodies.Add(Expression.Call(builderExpr, methodInfo2, parameterNameExpr));

                if (isEntityType)
                    RepositoryHelper.AddKeyMemberParameter(commandExpr, ormProviderExpr, parameterNameExpr, typedParameterExpr, keyMapper, this.ormProvider, blockBodies);
                else RepositoryHelper.AddKeyValueParameter(commandExpr, ormProviderExpr, parameterNameExpr, parameterExpr, keyMapper, this.ormProvider, blockBodies);
            }
            commandInitializerDelegate = Expression.Lambda<Action<IDbCommand, IOrmProvider, StringBuilder, int, object>>(Expression.Block(blockParameters, blockBodies), commandExpr, ormProviderExpr, builderExpr, indexExpr, parameterExpr).Compile();
            commandInitializerCache.TryAdd(cacheKey, commandInitializerDelegate);
        }
        return (Action<IDbCommand, IOrmProvider, StringBuilder, int, object>)commandInitializerDelegate;
    }
    private Action<IDbCommand, IOrmProvider, StringBuilder, int, object> BuildBatchCommandInitializer(Type entityType)
    {
        var entityMapper = this.mapProvider.GetEntityMap(entityType);
        if (entityMapper.KeyMembers.Count > 1)
        {
            return (command, ormProvider, builder, index, parameter) =>
            {
                var dict = parameter as Dictionary<string, object>;
                if (index > 0) builder.Append(';');
                else builder.Append($"DELETE FROM {ormProvider.GetFieldName(entityMapper.TableName)} WHERE ");
                int keyIndex = 0;
                foreach (var keyMapper in entityMapper.KeyMembers)
                {
                    if (keyIndex > 0) builder.Append(" AND ");
                    var fieldName = ormProvider.GetFieldName(keyMapper.FieldName);
                    string parameterName = ormProvider.ParameterPrefix + keyMapper.MemberName + index.ToString();
                    builder.Append($"{fieldName}={parameterName}");

                    if (keyMapper.NativeDbType != null)
                        command.Parameters.Add(ormProvider.CreateParameter(parameterName, keyMapper.NativeDbType, dict[keyMapper.MemberName]));
                    else command.Parameters.Add(ormProvider.CreateParameter(parameterName, dict[keyMapper.MemberName]));
                }
            };
        }
        else
        {
            return (command, ormProvider, builder, index, parameter) =>
            {
                var dict = parameter as Dictionary<string, object>;
                if (index > 0) builder.Append(',');
                else builder.Append($"DELETE FROM {ormProvider.GetFieldName(entityMapper.TableName)} WHERE {ormProvider.GetFieldName(entityMapper.KeyMembers[0].FieldName)} IN (");
                var keyMapper = entityMapper.KeyMembers[0];
                string parameterName = ormProvider.ParameterPrefix + keyMapper.MemberName + index.ToString();
                builder.Append(parameterName);

                if (keyMapper.NativeDbType != null)
                    command.Parameters.Add(ormProvider.CreateParameter(parameterName, keyMapper.NativeDbType, dict[keyMapper.MemberName]));
                else command.Parameters.Add(ormProvider.CreateParameter(parameterName, dict[keyMapper.MemberName]));
            };
        }
    }
    private Func<IDbCommand, IOrmProvider, object, string> BuildCommandInitializer(Type entityType, Type parameterType)
    {
        var cacheKey = HashCode.Combine("Delete", this.connection, this.ormProvider, string.Empty, entityType, parameterType);
        if (!commandInitializerCache.TryGetValue(cacheKey, out var commandInitializerDelegate))
        {
            int index = 0;
            var entityMapper = this.mapProvider.GetEntityMap(entityType);
            var commandExpr = Expression.Parameter(typeof(IDbCommand), "cmd");
            var ormProviderExpr = Expression.Parameter(typeof(IOrmProvider), "ormProvider");
            var parameterExpr = Expression.Parameter(typeof(object), "parameter");
            var blockParameters = new List<ParameterExpression>();
            var blockBodies = new List<Expression>();
            var localParameters = new Dictionary<string, int>();

            EntityMap parameterMapper = null;
            ParameterExpression typedParameterExpr = null;
            bool isEntityType = false;
            if (parameterType.IsEntityType())
            {
                isEntityType = true;
                parameterMapper = this.mapProvider.GetEntityMap(parameterType);
                typedParameterExpr = Expression.Parameter(parameterType, "typedParameter");
                blockParameters.Add(typedParameterExpr);
                blockBodies.Add(Expression.Assign(typedParameterExpr, Expression.Convert(parameterExpr, parameterType)));
            }
            else
            {
                if (entityMapper.KeyMembers.Count > 1)
                    throw new NotSupportedException($"模型{entityType.FullName}有多个主键字段，不能使用单个值类型{parameterType.FullName}作为参数");
            }

            var methodInfo1 = typeof(StringBuilder).GetMethod(nameof(StringBuilder.Append), new Type[] { typeof(char) });
            var methodInfo2 = typeof(StringBuilder).GetMethod(nameof(StringBuilder.Append), new Type[] { typeof(string) });
            var methodInfo3 = typeof(string).GetMethod(nameof(string.Concat), new Type[] { typeof(string), typeof(string) });

            var builder = new StringBuilder($"DELETE FROM {this.ormProvider.GetTableName(entityMapper.TableName)} WHERE ");
            foreach (var keyMapper in entityMapper.KeyMembers)
            {
                if (isEntityType && !parameterMapper.TryGetMemberMap(keyMapper.MemberName, out var propMapper))
                    throw new ArgumentNullException($"参数类型{parameterType.FullName}缺少主键字段{keyMapper.MemberName}", "keys");

                var parameterName = this.ormProvider.ParameterPrefix + keyMapper.MemberName;
                if (index > 0)
                    builder.Append(" AND ");
                builder.Append($"{this.ormProvider.GetFieldName(keyMapper.FieldName)}={parameterName}");
                var parameterNameExpr = Expression.Constant(parameterName);

                if (isEntityType)
                    RepositoryHelper.AddKeyMemberParameter(commandExpr, ormProviderExpr, parameterNameExpr, typedParameterExpr, keyMapper, this.ormProvider, blockBodies);
                else RepositoryHelper.AddKeyValueParameter(commandExpr, ormProviderExpr, parameterNameExpr, parameterExpr, keyMapper, this.ormProvider, blockBodies);
                index++;
            }
            var resultLabelExpr = Expression.Label(typeof(string));
            var returnExpr = Expression.Constant(builder.ToString());
            blockBodies.Add(Expression.Return(resultLabelExpr, returnExpr));
            blockBodies.Add(Expression.Label(resultLabelExpr, Expression.Constant(null, typeof(string))));

            commandInitializerDelegate = Expression.Lambda<Func<IDbCommand, IOrmProvider, object, string>>(Expression.Block(blockParameters, blockBodies), commandExpr, ormProviderExpr, parameterExpr).Compile();
            commandInitializerCache.TryAdd(cacheKey, commandInitializerDelegate);
        }
        return (Func<IDbCommand, IOrmProvider, object, string>)commandInitializerDelegate;
    }
    private Func<IDbCommand, IOrmProvider, object, string> BuildCommandInitializer(Type entityType)
    {
        return (command, ormProvider, parameter) =>
        {
            int index = 0;
            var dict = parameter as Dictionary<string, object>;
            var entityMapper = this.mapProvider.GetEntityMap(entityType);
            var builder = new StringBuilder($"DELETE FROM {ormProvider.GetTableName(entityMapper.TableName)} WHERE ");

            foreach (var keyMapper in entityMapper.KeyMembers)
            {
                if (!dict.TryGetValue(keyMapper.MemberName, out var fieldValue))
                    throw new ArgumentNullException($"字典参数中缺少主键字段{keyMapper.MemberName}", "keys");

                if (index > 0)
                    builder.Append(',');
                var parameterName = ormProvider.ParameterPrefix + keyMapper.MemberName;

                builder.Append($"{ormProvider.GetFieldName(keyMapper.MemberName)}={parameterName}");

                if (keyMapper.NativeDbType != null)
                    command.Parameters.Add(ormProvider.CreateParameter(parameterName, keyMapper.NativeDbType, fieldValue));
                else command.Parameters.Add(ormProvider.CreateParameter(parameterName, fieldValue));
                index++;
            }
            return builder.ToString();
        };
    }
}
class Deleting<TEntity> : IDeleting<TEntity>
{
    private readonly TheaConnection connection;
    private readonly IDbTransaction transaction;
    private readonly IDeleteVisitor visitor;

    public Deleting(TheaConnection connection, IDbTransaction transaction, IDeleteVisitor visitor)
    {
        this.connection = connection;
        this.transaction = transaction;
        this.visitor = visitor;
    }

    public IDeleting<TEntity> And(bool condition, Expression<Func<TEntity, bool>> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));

        if (condition)
            this.visitor.And(predicate);
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
