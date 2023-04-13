using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;

public class JsonNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var fromString = reader.GetString();
                if (string.IsNullOrWhiteSpace(fromString))
                    return null;
                if (DateTime.TryParse(fromString, out var result))
                    return result;
                break;
            case JsonTokenType.Null: return null;
        }
        return null;
    }
    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        else writer.WriteNullValue();
    }
}