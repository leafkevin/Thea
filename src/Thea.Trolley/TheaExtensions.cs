using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Transactions;
using Thea.Orm;

namespace Thea.Trolley;

public static class TheaExtensions
{
    public static IServiceCollection AddTrolley(this IServiceCollection services)
    {
        services.AddSingleton<IOrmDbFactory, OrmDbFactory>();
        return services;
    }
    public static IApplicationBuilder UseTrolley(this IApplicationBuilder app, Action<OrmDbFactoryBuilder> initializer)
    {
        var dbFactory = app.ApplicationServices.GetService<IOrmDbFactory>();
        var builder = new OrmDbFactoryBuilder(dbFactory);
        initializer?.Invoke(builder);
        return app;
    }

    public static void AddEntityMap<TEntity>(this IOrmDbFactory dbFactory)
    {
        var entityType = typeof(TEntity);
        dbFactory.AddEntityMap(entityType, new EntityMap(entityType));
    }
    public static bool IsEntityType(this Type type)
    {
        var typeCode = Type.GetTypeCode(type);
        switch (typeCode)
        {
            case TypeCode.DBNull:
            case TypeCode.Boolean:
            case TypeCode.Char:
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
            case TypeCode.DateTime:
            case TypeCode.String:
                return false;
        }
        if (type.IsClass) return true;
        if (type.IsValueType && !type.IsEnum && !type.IsPrimitive && type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Count(f => f.MemberType == MemberTypes.Field || (f.MemberType == MemberTypes.Property && f is PropertyInfo propertyInfo && propertyInfo.GetIndexParameters().Length == 0)) > 1)
            return true;
        return false;
    }
    public static Type GetMemberType(this MemberInfo member)
    {
        switch (member.MemberType)
        {
            case MemberTypes.Property:
                var propertyInfo = member as PropertyInfo;
                return propertyInfo.PropertyType;
            case MemberTypes.Field:
                var fieldInfo = member as FieldInfo;
                return fieldInfo.FieldType;
        }
        throw new Exception("成员member，不是属性也不是字段");
    }
}

