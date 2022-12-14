using System;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Thea.Orm;

namespace Thea.Trolley;

public delegate IDbConnection CreateNativeDbConnectionDelegate(string connectionString);
public delegate IDbDataParameter CreateDefaultNativeParameterDelegate(string name, object value);
public delegate IDbDataParameter CreateNativeParameterDelegate(string name, int nativeDbType, object value);
public abstract class BaseOrmProvider : IOrmProvider
{
    public abstract DatabaseType DatabaseType { get; }
    public virtual string ParameterPrefix => "@";
    public virtual string SelectIdentitySql => ";SELECT @@IDENTITY";

    public abstract IDbConnection CreateConnection(string connectionString);
    public abstract IDbDataParameter CreateParameter(string parameterName, object value);
    public abstract IDbDataParameter CreateParameter(string parameterName, int nativeDbType, object value);
    public virtual string GetTableName(string entityName) => entityName;
    public virtual string GetFieldName(string propertyName) => propertyName;
    public virtual string GetPagingTemplate(int skip, int? limit, string orderBy = null)
    {
        var builder = new StringBuilder("SELECT /**fields**/ FROM /**tables**/ /**others**/");
        if (!String.IsNullOrEmpty(orderBy)) builder.Append($" {orderBy}");
        if (limit.HasValue) builder.Append($" LIMIT {limit}");
        builder.Append($" OFFSET {skip}");
        return builder.ToString();
    }
    public abstract int GetNativeDbType(Type type);
    public abstract string CastTo(Type type);
    public virtual string GetQuotedValue(Type fieldType, object value)
    {
        if (fieldType == typeof(bool))
            return (bool)value ? "1" : "0";
        if (fieldType == typeof(string))
            return "'" + value.ToString().Replace("\\", "\\\\").Replace("'", @"\'") + "'";
        if (fieldType == typeof(DateTime))
            return $"'{value:yyyy-MM-dd HH:mm:ss}'";
        if (value is SqlSegment sqlSegment)
        {
            if (!sqlSegment.IsConstantValue)
                return sqlSegment.Value.ToString();
            return this.GetQuotedValue(sqlSegment.Value);
        }
        return value.ToString();
    }
    public abstract bool TryGetMemberAccessSqlFormatter(MemberInfo memberInfo, out MemberAccessSqlFormatter formatter);
    public abstract bool TryGetMethodCallSqlFormatter(MethodInfo methodInfo, out MethodCallSqlFormatter formatter);
    protected virtual CreateNativeDbConnectionDelegate CreateConnectionDelegate(Type connectionType)
    {
        var constructor = connectionType.GetConstructor(new Type[] { typeof(string) });
        var connStringExpr = Expression.Parameter(typeof(string), "connectionString");
        var instanceExpr = Expression.New(constructor, connStringExpr);
        return Expression.Lambda<CreateNativeDbConnectionDelegate>(
             Expression.Convert(instanceExpr, typeof(IDbConnection))
             , connStringExpr).Compile();
    }
    protected virtual CreateDefaultNativeParameterDelegate CreateDefaultParameterDelegate(Type dbParameterType)
    {
        var constructor = dbParameterType.GetConstructor(new Type[] { typeof(string), typeof(object) });
        var parametersExpr = new ParameterExpression[] {
            Expression.Parameter(typeof(string), "name"),
            Expression.Parameter(typeof(object), "value") };
        var instanceExpr = Expression.New(constructor, parametersExpr[0], parametersExpr[1]);
        var convertExpr = Expression.Convert(instanceExpr, typeof(IDbDataParameter));
        return Expression.Lambda<CreateDefaultNativeParameterDelegate>(convertExpr, parametersExpr).Compile();
    }
    protected virtual CreateNativeParameterDelegate CreateParameterDelegate(Type dbTypeType, Type dbParameterType, PropertyInfo dbTypePropertyInfo)
    {
        var constructor = dbParameterType.GetConstructor(new Type[] { typeof(string), typeof(object) });
        var parametersExpr = new ParameterExpression[] {
            Expression.Parameter(typeof(string), "name"),
            Expression.Parameter(typeof(int), "dbType"),
            Expression.Parameter(typeof(object), "value") };

        var returnLabel = Expression.Label(typeof(IDbDataParameter));
        var instanceExpr = Expression.New(constructor, parametersExpr[0], parametersExpr[2]);
        var dbTypeExpr = Expression.Convert(parametersExpr[1], dbTypeType);
        return Expression.Lambda<CreateNativeParameterDelegate>(
            Expression.Block(
                Expression.Call(instanceExpr, dbTypePropertyInfo.GetSetMethod(), dbTypeExpr),
                Expression.Return(returnLabel, Expression.Convert(instanceExpr, typeof(IDbDataParameter))),
                Expression.Label(returnLabel, Expression.Default(typeof(IDbDataParameter))))
            , parametersExpr).Compile();
    }
}
