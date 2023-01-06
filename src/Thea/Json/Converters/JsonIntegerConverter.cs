using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;
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