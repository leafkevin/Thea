using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Thea.Json;

namespace Thea;

public static class TheaExtensions
{
    private static ConcurrentDictionary<Type, Dictionary<object, string>> enumDescriptions = new();
    public static string ToDescription<TEnum>(this object enumObj) where TEnum : struct, Enum
    {
        var enumType = typeof(TEnum);
        object enumValue = null;
        if (enumObj is TEnum typedValue)
            enumValue = typedValue;
        else enumValue = Enum.ToObject(enumType, enumObj);
        if (!enumDescriptions.TryGetValue(enumType, out var descriptions))
        {
            var enumValues = Enum.GetValues(enumType);
            descriptions = new Dictionary<object, string>();
            foreach (var value in enumValues)
            {
                string description = null;
                var enumName = Enum.GetName(enumType, value);
                var fieldInfo = enumType.GetField(enumName);
                if (fieldInfo != null)
                {
                    var descAttr = fieldInfo.GetCustomAttribute<DescriptionAttribute>();
                    if (descAttr != null)
                        description = descAttr.Description;
                }
                descriptions.Add(value, description ?? enumName);
            }
            enumDescriptions.TryAdd(enumType, descriptions);
        }
        return descriptions[enumValue];
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
