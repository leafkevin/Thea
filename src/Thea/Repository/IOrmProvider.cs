using System;
using System.Data;
using System.Reflection;

namespace Thea
{
    public interface IOrmProvider
    {
        /// <summary>
        /// 参数名前导字符，如：@:?
        /// </summary>
        string ParamPrefix { get; }
        string SelectIdentitySql { get; }
        bool IsSupportArrayParameter { get; }
        Type NativeDbParameterType { get; }
        Type NativeDbTypeType { get; }
        PropertyInfo NativeDbTypePropertyOfDbParameter { get; }
        IDbConnection CreateConnection(string connectionString);
        string GetPropertyName(string propertyName);
        string GetTableName(string entityName);
        string GetFieldName(string propertyName);
        string GetPagingTemplate(int skip, int? limit, string orderBy = null);
        int GetArrayNativeDbType(Type itemType);
    }
}