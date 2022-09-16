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
    public static string Serialize<T>(T obj) => JsonSerializer.Serialize<T>(obj, TheaJsonSerializer.SerializerOptions);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, TheaJsonSerializer.SerializerOptions);
}
public class JsonIntegerConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int value = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out value))
                    return value;
                break;
            case JsonTokenType.String:
                if (int.TryParse(reader.GetString(), out value))
                    return value;
                break;
        }
        return value;
    }
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
public class JsonLongConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        long value = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out value))
                    return value;
                break;
            case JsonTokenType.String:
                if (long.TryParse(reader.GetString(), out value))
                    return value;
                break;
        }
        return value;
    }
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
public class JsonFloatConverter : JsonConverter<float>
{
    public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        float value = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetSingle(out value))
                    return value;
                break;
            case JsonTokenType.String:
                if (float.TryParse(reader.GetString(), out value))
                    return value;
                break;
        }
        return value;
    }
    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
public class JsonDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double value = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetDouble(out value))
                    return value;
                break;
            case JsonTokenType.String:
                if (double.TryParse(reader.GetString(), out value))
                    return value;
                break;
        }
        return value;
    }
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
public class JsonDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        decimal value = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetDecimal(out value))
                    return value;
                break;
            case JsonTokenType.String:
                if (decimal.TryParse(reader.GetString(), out value))
                    return value;
                break;
        }
        return value;
    }
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
public class JsonNullabeDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTime.Parse(reader.GetString());
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("yyyy-MM-dd HH:mm:ss"));
}
public class JsonNullableIntegerConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int value = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out value))
                    return value;
                break;
            case JsonTokenType.String:
                if (int.TryParse(reader.GetString(), out value))
                    return value;
                break;
            case JsonTokenType.Null:
                return null;
        }
        return value;
    }
    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}
public class JsonNullableLongConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        long value = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out value))
                    return value;
                break;
            case JsonTokenType.String:
                if (long.TryParse(reader.GetString(), out value))
                    return value;
                break;
            case JsonTokenType.Null:
                return null;
        }
        return value;
    }
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}
public class JsonNullableFloatConverter : JsonConverter<float?>
{
    public override float? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        float value = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetSingle(out value))
                    return value;
                break;
            case JsonTokenType.String:
                if (float.TryParse(reader.GetString(), out value))
                    return value;
                break;
            case JsonTokenType.Null:
                return null;
        }
        return value;
    }
    public override void Write(Utf8JsonWriter writer, float? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}
public class JsonNullableDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double value = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetDouble(out value))
                    return value;
                break;
            case JsonTokenType.String:
                if (double.TryParse(reader.GetString(), out value))
                    return value;
                break;
            case JsonTokenType.Null:
                return null;
        }
        return value;
    }
    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}
public class JsonNullableDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        decimal value = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetDecimal(out value))
                    return value;
                break;
            case JsonTokenType.String:
                if (decimal.TryParse(reader.GetString(), out value))
                    return value;
                break;
            case JsonTokenType.Null:
                return null;
        }
        return value;
    }
    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}
public class JsonDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTime.Parse(reader.GetString());
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("yyyy-MM-dd HH:mm:ss"));
}
public class JsonNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        DateTime value = DateTime.MinValue;
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                if (DateTime.TryParse(reader.GetString(), out value))
                    return value;
                break;
            case JsonTokenType.Null:
                return null;
        }
        return value;
    }
    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.ToString());
        else writer.WriteNullValue();
    }
}
