using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;

namespace Thea.Orm;

public delegate string MemberAccessSqlFormatter(object target);
public delegate string MethodCallSqlFormatter(object target, Stack<DeferredExpr> deferredExprs, params object[] arguments);
public enum DatabaseType
{
    MySql = 1,
    SqlServer = 2,
    Oracle = 3,
    Postgresql = 4
}
public interface IOrmProvider
{
    DatabaseType DatabaseType { get; }
    string ParameterPrefix { get; }
    string SelectIdentitySql { get; }
    IDbConnection CreateConnection(string connectionString);
    IDbDataParameter CreateParameter(string parameterName, object value);
    IDbDataParameter CreateParameter(string parameterName, int nativeDbType, object value);
    string GetTableName(string entityName);
    string GetFieldName(string propertyName);
    string GetPagingTemplate(int skip, int? limit, string orderBy = null);
    int GetNativeDbType(Type type);
    string CastTo(Type type);
    string GetQuotedValue(Type fieldType, object value);
    bool TryGetMemberAccessSqlFormatter(MemberInfo memberInfo, out MemberAccessSqlFormatter formatter);
    bool TryGetMethodCallSqlFormatter(MethodInfo methodInfo, out MethodCallSqlFormatter formatter);
}