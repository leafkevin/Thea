using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;

public class JsonNullableLongConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        long result;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out result))
                    return result;
                break;
            case JsonTokenType.String:
                var fromString = reader.GetString();
                if (string.IsNullOrWhiteSpace(fromString))
                    return null;
                if (long.TryParse(fromString, out result))
                    return result;
                break;
            case JsonTokenType.Null: return null;
        }
        return null;
    }
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}
