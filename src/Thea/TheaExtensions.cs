using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using Thea.Json;

namespace Thea;

public static class TheaExtensions
{
    private static ConcurrentDictionary<int, Type> namedImplTyps = new();
    public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services, string name)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddSingleton<TImplementation>();
        var serverType = typeof(TService);
        var implType = typeof(TImplementation);

        var hashKey = HashCode.Combine(serverType, name);
        namedImplTyps.TryAdd(hashKey, implType);
        return services;
    }
    public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services, string name)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddScoped<TImplementation>();
        var serverType = typeof(TService);
        var implType = typeof(TImplementation);

        var hashKey = HashCode.Combine(serverType, name);
        namedImplTyps.TryAdd(hashKey, implType);
        return services;
    }
    public static IServiceCollection AddTransient<TService, TImplementation>(this IServiceCollection services, string name)
       where TService : class
       where TImplementation : class, TService
    {
        services.AddTransient<TImplementation>();
        var serverType = typeof(TService);
        var implType = typeof(TImplementation);

        var hashKey = HashCode.Combine(serverType, name);
        namedImplTyps.TryAdd(hashKey, implType);
        return services;
    }
    public static TService GetService<TService>(this IServiceProvider serviceProvider, string name)
    {
        var serverType = typeof(TService);
        var hashKey = HashCode.Combine(serverType, name);

        if (!namedImplTyps.TryGetValue(hashKey, out var implType))
            throw new Exception($"没有注册命名为{name}的服务类型:{serverType.FullName}");
        return (TService)serviceProvider.GetService(implType);
    }

    public static T JsonTo<T>(this object obj)
    {
        if (obj == null) return default;
        if (obj is JsonElement element)
            return TheaJsonSerializer.Deserialize<T>(element.GetRawText());
        if (obj is string json)
            return TheaJsonSerializer.Deserialize<T>(json);
        return obj.ConvertTo<T>();
    }
    public static T ConvertTo<T>(this object obj)
    {
        if (obj == null) return default;
        var targetType = typeof(T);
        var type = obj.GetType();
        if (targetType.IsAssignableFrom(type))
            return (T)obj;
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        if (underlyingType == null) underlyingType = targetType;
        object result = obj;
        if (underlyingType.IsEnum)
        {
            var enumObj = Convert.ChangeType(result, underlyingType.GetEnumUnderlyingType());
            return (T)Enum.ToObject(underlyingType, enumObj);
        }
        return (T)Convert.ChangeType(result, underlyingType);
    }
    public static string ToJson(this object obj)
    {
        if (obj == null) return null;
        return TheaJsonSerializer.Serialize(obj);
    }
    public static T JsonProperty<T>(this object jsonObj, string propertyName)
    {
        if (jsonObj.TryGetProperty<T>(propertyName, out var value))
            return value;
        return default;
    }
    public static bool TryGetProperty<T>(this object jsonObj, string propertyName, out T value)
    {
        if (jsonObj == null)
        {
            value = default;
            return false;
        }
        if (jsonObj is JsonElement element && element.TryGetProperty(propertyName, out var jsonValue))
        {
            value = jsonValue.JsonTo<T>();
            return true;
        }
        value = default;
        return false;
    }
}
