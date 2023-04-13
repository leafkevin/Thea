using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;
public class JsonIntegerConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int result = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out result))
                    return result;
                break;
            case JsonTokenType.String:
                if (int.TryParse(reader.GetString(), out result))
                    return result;
                break;
        }
        return result;
    }
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}