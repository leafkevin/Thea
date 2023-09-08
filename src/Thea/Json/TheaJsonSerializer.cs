using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;

public class TheaJsonSerializer
{
    public static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };
    static TheaJsonSerializer()
    {
        SerializerOptions.Converters.Add(new JsonIntegerConverter());
        SerializerOptions.Converters.Add(new JsonLongConverter());
        SerializerOptions.Converters.Add(new JsonFloatConverter());
        SerializerOptions.Converters.Add(new JsonDoubleConverter());
        SerializerOptions.Converters.Add(new JsonDecimalConverter());
        SerializerOptions.Converters.Add(new JsonDateTimeConverter());
        SerializerOptions.Converters.Add(new JsonNullableIntegerConverter());
        SerializerOptions.Converters.Add(new JsonNullableLongConverter());
        SerializerOptions.Converters.Add(new JsonNullableFloatConverter());
        SerializerOptions.Converters.Add(new JsonNullableDoubleConverter());
        SerializerOptions.Converters.Add(new JsonNullableDecimalConverter());
        SerializerOptions.Converters.Add(new JsonNullableDateTimeConverter());
    }
    public static string Serialize(object obj) => JsonSerializer.Serialize(obj, SerializerOptions);
    public static string Serialize<T>(T obj) => JsonSerializer.Serialize<T>(obj, SerializerOptions);
    public static object Deserialize(string json, Type type) => JsonSerializer.Deserialize(json, type, SerializerOptions);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, SerializerOptions);
}