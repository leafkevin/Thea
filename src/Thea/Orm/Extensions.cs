using System;
using System.Collections;
using System.Linq;

namespace Thea.Orm;

public static class Extensions
{
    private static Type[] valueTypes = new Type[] {typeof(byte),typeof(sbyte),typeof(short),typeof(ushort),
        typeof(int),typeof(uint),typeof(long),typeof(ulong),typeof(float),typeof(double),typeof(decimal),
        typeof(bool),typeof(string),typeof(char),typeof(Guid),typeof(DateTime),typeof(DateTimeOffset),
        typeof(TimeSpan),typeof(TimeOnly),typeof(DateOnly),typeof(byte[]),typeof(byte?),typeof(sbyte?),
        typeof(short?),typeof(ushort?),typeof(int?),typeof(uint?),typeof(long?),typeof(ulong?),typeof(float?),
        typeof(double?),typeof(decimal?),typeof(bool?),typeof(char?),typeof(Guid?) ,typeof(DateTime?),
        typeof(DateTimeOffset?),typeof(TimeSpan?),typeof(TimeOnly?),typeof(DateOnly?)};

    public static bool IsEntityType(this Type type)
    {
        if (type.IsEnum || valueTypes.Contains(type)) return false;
        if (type.FullName == "System.Data.Linq.Binary")
            return false;
        if (type.IsValueType)
        {
            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null && underlyingType.IsEnum)
                return false;
        }
        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            if (valueTypes.Contains(elementType) || elementType.IsEnum) return false;
            if (elementType.IsValueType)
            {
                var underlyingType = Nullable.GetUnderlyingType(elementType);
                if (underlyingType != null && underlyingType.IsEnum)
                    return false;
            }
        }
        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            foreach (var elementType in type.GenericTypeArguments)
            {
                if (elementType.IsEnum || valueTypes.Contains(elementType))
                    return false;
                if (elementType.IsValueType)
                {
                    var underlyingType = Nullable.GetUnderlyingType(elementType);
                    if (underlyingType != null && underlyingType.IsEnum)
                        return false;
                }
            }
        }
        return true;
    }
}
