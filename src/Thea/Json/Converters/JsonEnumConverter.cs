using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thea.Json;

public sealed class JsonEnumConverter : JsonConverterFactory
{
    public override bool CanConvert(Type type) => type.IsEnum;
    public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
    {
        var converter = (JsonConverter)Activator.CreateInstance(typeof(JsonEnumConverter<>).MakeGenericType(type),
            BindingFlags.Instance | BindingFlags.Public, binder: null, args: null, culture: null);
        return converter;
    }
}
class JsonEnumConverter<T> : JsonConverter<T>
     where T : Enum
{
    public override bool CanConvert(Type type) => type.IsEnum;
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var type = typeof(T);
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                var enumUnderlyingType = type.GetEnumUnderlyingType();
                object underlyingValue = null;
                switch (Type.GetTypeCode(enumUnderlyingType))
                {
                    case TypeCode.SByte:
                        underlyingValue = reader.GetSByte();
                        break;
                    case TypeCode.Byte:
                        underlyingValue = reader.GetByte();
                        break;
                    case TypeCode.Int16:
                        underlyingValue = reader.GetInt16();
                        break;
                    case TypeCode.UInt16:
                        underlyingValue = reader.GetUInt16();
                        break;
                    case TypeCode.Int32:
                        underlyingValue = reader.GetInt32();
                        break;
                    case TypeCode.UInt32:
                        underlyingValue = reader.GetUInt32();
                        break;
                    case TypeCode.Int64:
                        underlyingValue = reader.GetInt64();
                        break;
                    case TypeCode.UInt64:
                        underlyingValue = reader.GetUInt64();
                        break;
                    case TypeCode.Single:
                        underlyingValue = reader.GetSingle();
                        break;
                    case TypeCode.Double:
                        underlyingValue = reader.GetDouble();
                        break;
                    case TypeCode.Decimal:
                        underlyingValue = reader.GetDecimal();
                        break;
                }
                return (T)Enum.ToObject(underlyingType, underlyingValue);
            case JsonTokenType.String:
                var enumString = reader.GetString();
                if (Enum.TryParse(underlyingType, enumString, out var value))
                    return (T)value;
                if (Enum.TryParse(underlyingType, enumString, true, out value))
                    return (T)value;
                break;
            case JsonTokenType.Null:
                return default(T);
        }
        return default;
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var type = typeof(T);
        var underlyingType = Nullable.GetUnderlyingType(type);
        bool isNullable = underlyingType == null;
        if (isNullable && value == null)
            writer.WriteNullValue();

        if (isNullable) underlyingType = type;
        underlyingType = underlyingType.GetEnumUnderlyingType();
        switch (Type.GetTypeCode(underlyingType))
        {
            case TypeCode.Int32:
                writer.WriteNumberValue(Unsafe.As<T, int>(ref value));
                break;
            case TypeCode.UInt32:
                writer.WriteNumberValue(Unsafe.As<T, uint>(ref value));
                break;
            case TypeCode.UInt64:
                writer.WriteNumberValue(Unsafe.As<T, ulong>(ref value));
                break;
            case TypeCode.Int64:
                writer.WriteNumberValue(Unsafe.As<T, long>(ref value));
                break;
            case TypeCode.Int16:
                writer.WriteNumberValue(Unsafe.As<T, short>(ref value));
                break;
            case TypeCode.UInt16:
                writer.WriteNumberValue(Unsafe.As<T, ushort>(ref value));
                break;
            case TypeCode.Byte:
                writer.WriteNumberValue(Unsafe.As<T, byte>(ref value));
                break;
            case TypeCode.SByte:
                writer.WriteNumberValue(Unsafe.As<T, sbyte>(ref value));
                break;
        }
    }
}