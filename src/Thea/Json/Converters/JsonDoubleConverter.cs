using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;

public class JsonDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double result = 0;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetDouble(out result))
                    return result;
                break;
            case JsonTokenType.String:
                if (double.TryParse(reader.GetString(), out result))
                    return result;
                break;
        }
        return result;
    }
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}