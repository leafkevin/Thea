using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;

public class JsonBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.False:
                return false;
            case JsonTokenType.True:
                return true;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var iValue))
                    return iValue != 0;
                break;
            case JsonTokenType.String:
                if (int.TryParse(reader.GetString(), out var value1))
                    return value1 != 0;
                if (bool.TryParse(reader.GetString(), out var bValue))
                    return bValue;
                break;
        }
        return false;
    }
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);
}
public class JsonNullableBooleanConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.False:
                return false;
            case JsonTokenType.True:
                return true;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var iValue))
                    return iValue != 0;
                break;
            case JsonTokenType.String:
                if (int.TryParse(reader.GetString(), out var value1))
                    return value1 != 0;
                if (bool.TryParse(reader.GetString(), out var bValue))
                    return bValue;
                break;
            case JsonTokenType.Null: return null;
        }
        return null;
    }
    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteBooleanValue(value.Value);
        else writer.WriteNullValue();
    }
}