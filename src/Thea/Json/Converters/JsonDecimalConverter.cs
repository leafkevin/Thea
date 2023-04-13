using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;

public class JsonDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        decimal result = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetDecimal(out result))
                    return result;
                break;
            case JsonTokenType.String:
                if (decimal.TryParse(reader.GetString(), out result))
                    return result;
                break;
        }
        return result;
    }
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}