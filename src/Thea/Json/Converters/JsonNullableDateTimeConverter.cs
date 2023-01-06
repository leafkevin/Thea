using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;

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