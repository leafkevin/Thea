using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;

public class JsonNullableIntegerConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int result;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out result))
                    return result;
                break;
            case JsonTokenType.String:
                var fromString = reader.GetString();
                if (string.IsNullOrWhiteSpace(fromString))
                    return null;
                if (int.TryParse(fromString, out result))
                    return result;
                break;
            case JsonTokenType.Null: return null;
        }
        return null;
    }
    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}