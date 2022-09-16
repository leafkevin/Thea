using System;

namespace Thea.Orm;

public static class TheaOrmExtensions
{
    public static string GetQuotedValue(this IOrmProvider ormProvider, object value)
    {
        if (value == null) return "null";
        return ormProvider.GetQuotedValue(value.GetType(), value);
    }
    public static EntityMap GetEntityMap(this IOrmDbFactory dbFactory, Type entityType)
    {
        if (!dbFactory.TryGetEntityMap(entityType, out var mapper))
            mapper = EntityMap.CreateDefaultMap(entityType);

        return mapper;
    }
}
