using System;
using System.Data;

namespace Thea.Orm;

public interface ITypeHandler
{
    void SetValue(IOrmProvider ormProvider, int nativeDbType, object value, out IDbDataParameter parameter);
    void SetValue(IOrmProvider ormProvider, int nativeDbType, object value, out string sqlValue);
    object Parse(IOrmProvider ormProvider, int nativeDbType, Type TargetType, object value);
}
