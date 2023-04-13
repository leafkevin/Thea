using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;

public class JsonFloatConverter : JsonConverter<float>
{
    public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        float result = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetSingle(out result))
                    return result;
                break;
            case JsonTokenType.String:
                if (float.TryParse(reader.GetString(), out result))
                    return result;
                break;
        }
        return result;
    }
    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}