using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;

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
