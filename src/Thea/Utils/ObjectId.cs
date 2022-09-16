using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;

namespace Thea;

[StructLayout(LayoutKind.Sequential)]
public struct ObjectId : IComparable<ObjectId>, IEquatable<ObjectId>, IConvertible
{
    private static readonly ObjectId __emptyInstance;
    private static readonly int __staticMachine;
    private static readonly short __staticPid;
    private static int __staticIncrement;
    private readonly int _a;
    private readonly int _b;
    private readonly int _c;
    private static DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static string NewId() => GenerateNewId().ToString();
    public ObjectId(byte[] bytes)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException("bytes");
        }
        if (bytes.Length != 12)
        {
            throw new ArgumentException("Byte array must be 12 bytes long", "bytes");
        }
        FromByteArray(bytes, 0, out this._a, out this._b, out this._c);
    }
    static ObjectId()
    {
        __emptyInstance = new ObjectId();
        __staticMachine = (GetMachineHash() + GetAppDomainId()) & 0xffffff;
        __staticPid = GetPid();
        __staticIncrement = new Random().Next();
    }
    internal ObjectId(byte[] bytes, int index)
    {
        FromByteArray(bytes, index, out this._a, out this._b, out this._c);
    }
    public ObjectId(DateTime timestamp, int machine, short pid, int increment) : this(GetTimestampFromDateTime(timestamp), machine, pid, increment)
    {
    }
    public ObjectId(int timestamp, int machine, short pid, int increment)
    {
        if ((machine & 0xff000000L) != 0)
        {
            throw new ArgumentOutOfRangeException("machine", "The machine value must be between 0 and 16777215 (it must fit in 3 bytes).");
        }
        if ((increment & 0xff000000L) != 0)
        {
            throw new ArgumentOutOfRangeException("increment", "The increment value must be between 0 and 16777215 (it must fit in 3 bytes).");
        }
        this._a = timestamp;
        this._b = (machine << 8) | ((pid >> 8) & 0xff);
        this._c = (pid << 0x18) | increment;
    }
    public ObjectId(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException("value");
        }
        FromByteArray(ParseHexString(value), 0, out this._a, out this._b, out this._c);
    }
    public static ObjectId Empty => __emptyInstance;
    public int Timestamp => this._a;
    public int Machine => ((this._b >> 8) & 0xffffff);
    public short Pid => ((short)(((this._b << 8) & 0xff00) | ((this._c >> 0x18) & 0xff)));
    public int Increment => (this._c & 0xffffff);
    public DateTime CreationTime => UnixEpoch.AddSeconds((double)this.Timestamp);
    public static bool operator <(ObjectId lhs, ObjectId other) => (lhs.CompareTo(other) < 0);
    public static bool operator <=(ObjectId lhs, ObjectId other) => (lhs.CompareTo(other) <= 0);
    public static bool operator ==(ObjectId lhs, ObjectId other) => lhs.Equals(other);
    public static bool operator !=(ObjectId lhs, ObjectId other) => !(lhs == other);
    public static bool operator >=(ObjectId lhs, ObjectId other) => (lhs.CompareTo(other) >= 0);
    public static bool operator >(ObjectId lhs, ObjectId other) => (lhs.CompareTo(other) > 0);
    public static ObjectId GenerateNewId() => GenerateNewId(GetTimestampFromDateTime(DateTime.UtcNow));
    public static ObjectId GenerateNewId(DateTime timestamp) => GenerateNewId(GetTimestampFromDateTime(timestamp));
    public static ObjectId GenerateNewId(int timestamp) => new ObjectId(timestamp, __staticMachine, __staticPid, Interlocked.Increment(ref __staticIncrement) & 0xffffff);
    public static byte[] Pack(int timestamp, int machine, short pid, int increment)
    {
        if ((machine & 0xff000000L) != 0)
        {
            throw new ArgumentOutOfRangeException("machine", "The machine value must be between 0 and 16777215 (it must fit in 3 bytes).");
        }
        if ((increment & 0xff000000L) != 0)
        {
            throw new ArgumentOutOfRangeException("increment", "The increment value must be between 0 and 16777215 (it must fit in 3 bytes).");
        }
        return new byte[] { ((byte)(timestamp >> 0x18)), ((byte)(timestamp >> 0x10)), ((byte)(timestamp >> 8)), ((byte)timestamp), ((byte)(machine >> 0x10)), ((byte)(machine >> 8)), ((byte)machine), ((byte)(pid >> 8)), ((byte)pid), ((byte)(increment >> 0x10)), ((byte)(increment >> 8)), ((byte)increment) };
    }
    public static ObjectId Parse(string s)
    {
        ObjectId id;
        if (s == null)
        {
            throw new ArgumentNullException("s");
        }
        if (!TryParse(s, out id))
        {
            throw new FormatException($"'{s}' is not a valid 24 digit hex string.");
        }
        return id;
    }
    public static bool TryParse(string s, out ObjectId objectId)
    {
        byte[] buffer;
        if (((s != null) && (s.Length == 0x18)) && TryParseHexString(s, out buffer))
        {
            objectId = new ObjectId(buffer);
            return true;
        }
        objectId = new ObjectId();
        return false;
    }
    public static void Unpack(byte[] bytes, out int timestamp, out int machine, out short pid, out int increment)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException("bytes");
        }
        if (bytes.Length != 12)
        {
            throw new ArgumentOutOfRangeException("bytes", "Byte array must be 12 bytes long.");
        }
        timestamp = (((bytes[0] << 0x18) + (bytes[1] << 0x10)) + (bytes[2] << 8)) + bytes[3];
        machine = ((bytes[4] << 0x10) + (bytes[5] << 8)) + bytes[6];
        pid = (short)((bytes[7] << 8) + bytes[8]);
        increment = ((bytes[9] << 0x10) + (bytes[10] << 8)) + bytes[11];
    }
    private static int GetAppDomainId() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int GetCurrentProcessId() => Process.GetCurrentProcess().Id;
    private static int GetMachineHash()
    {
        string machineName = GetMachineName();
        return (0xffffff & machineName.GetHashCode());
    }
    private static string GetMachineName() => Environment.MachineName;
    private static short GetPid()
    {
        try
        {
            return (short)GetCurrentProcessId();
        }
        catch (SecurityException)
        {
            return 0;
        }
    }
    private static int GetTimestampFromDateTime(DateTime timestamp)
    {
        TimeSpan span = (TimeSpan)(ToUniversalTime(timestamp) - UnixEpoch);
        long num = (long)Math.Floor(span.TotalSeconds);
        if ((num < -2147483648L) || (num > 0x7fffffffL))
        {
            throw new ArgumentOutOfRangeException("timestamp");
        }
        return (int)num;
    }
    private static void FromByteArray(byte[] bytes, int offset, out int a, out int b, out int c)
    {
        a = (((bytes[offset] << 0x18) | (bytes[offset + 1] << 0x10)) | (bytes[offset + 2] << 8)) | bytes[offset + 3];
        b = (((bytes[offset + 4] << 0x18) | (bytes[offset + 5] << 0x10)) | (bytes[offset + 6] << 8)) | bytes[offset + 7];
        c = (((bytes[offset + 8] << 0x18) | (bytes[offset + 9] << 0x10)) | (bytes[offset + 10] << 8)) | bytes[offset + 11];
    }
    public int CompareTo(ObjectId other)
    {
        int num = ((uint)this._a).CompareTo((uint)other._a);
        if (num != 0)
        {
            return num;
        }
        num = ((uint)this._b).CompareTo((uint)other._b);
        if (num != 0)
        {
            return num;
        }
        return ((uint)this._c).CompareTo((uint)other._c);
    }
    public bool Equals(ObjectId rhs) => (((this._a == rhs._a) && (this._b == rhs._b)) && (this._c == rhs._c));
    public override bool Equals(object obj) => ((obj is ObjectId) && this.Equals((ObjectId)obj));
    public override int GetHashCode()
    {
        int num = 0x11;
        num = (0x25 * num) + ((int)this._a).GetHashCode();
        num = (0x25 * num) + ((int)this._b).GetHashCode();
        return ((0x25 * num) + ((int)this._c).GetHashCode());
    }
    public byte[] ToByteArray()
    {
        byte[] destination = new byte[12];
        this.ToByteArray(destination, 0);
        return destination;
    }
    public void ToByteArray(byte[] destination, int offset)
    {
        if (destination == null)
        {
            throw new ArgumentNullException("destination");
        }
        if ((offset + 12) > destination.Length)
        {
            throw new ArgumentException("Not enough room in destination buffer.", "offset");
        }
        destination[offset] = (byte)(this._a >> 0x18);
        destination[offset + 1] = (byte)(this._a >> 0x10);
        destination[offset + 2] = (byte)(this._a >> 8);
        destination[offset + 3] = (byte)this._a;
        destination[offset + 4] = (byte)(this._b >> 0x18);
        destination[offset + 5] = (byte)(this._b >> 0x10);
        destination[offset + 6] = (byte)(this._b >> 8);
        destination[offset + 7] = (byte)this._b;
        destination[offset + 8] = (byte)(this._c >> 0x18);
        destination[offset + 9] = (byte)(this._c >> 0x10);
        destination[offset + 10] = (byte)(this._c >> 8);
        destination[offset + 11] = (byte)this._c;
    }
    public override string ToString() => ToHexString(this.ToByteArray());
    TypeCode IConvertible.GetTypeCode() => TypeCode.Object;
    bool IConvertible.ToBoolean(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    byte IConvertible.ToByte(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    char IConvertible.ToChar(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    DateTime IConvertible.ToDateTime(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    decimal IConvertible.ToDecimal(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    double IConvertible.ToDouble(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    short IConvertible.ToInt16(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    int IConvertible.ToInt32(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    long IConvertible.ToInt64(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    sbyte IConvertible.ToSByte(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    float IConvertible.ToSingle(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    string IConvertible.ToString(IFormatProvider provider) => this.ToString();
    object IConvertible.ToType(Type conversionType, IFormatProvider provider)
    {
        TypeCode typeCode = Type.GetTypeCode(conversionType);
        if (typeCode != TypeCode.Object)
        {
            if (typeCode == TypeCode.String)
            {
                return ((IConvertible)this).ToString(provider);
            }
        }
        else
        {
            if ((conversionType == typeof(object)) || (conversionType == typeof(ObjectId)))
            {
                return this;
            }
        }
        throw new InvalidCastException();
    }
    ushort IConvertible.ToUInt16(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    uint IConvertible.ToUInt32(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    ulong IConvertible.ToUInt64(IFormatProvider provider)
    {
        throw new InvalidCastException();
    }
    public static bool TryParseHexString(string s, out byte[] bytes)
    {
        try
        {
            bytes = ParseHexString(s);
        }
        catch
        {
            bytes = null;
            return false;
        }
        return true;
    }
    public static byte[] ParseHexString(string s)
    {
        if (s == null)
        {
            throw new ArgumentNullException("s");
        }
        if ((s.Length & 1) != 0)
        {
            s = "0" + s;
        }
        byte[] buffer = new byte[s.Length / 2];
        for (int i = 0; i < buffer.Length; i++)
        {
            string str = s.Substring(2 * i, 2);
            try
            {
                buffer[i] = Convert.ToByte(str, 0x10);
            }
            catch (FormatException exception)
            {
                throw new FormatException($"Invalid hex string {s}. Problem with substring {str} starting at position {(int)(2 * i)}", (Exception)exception);
            }
        }
        return buffer;
    }
    public static DateTime ToUniversalTime(DateTime dateTime)
    {
        if (dateTime == DateTime.MinValue)
        {
            return DateTime.SpecifyKind(DateTime.MinValue, (DateTimeKind)DateTimeKind.Utc);
        }
        if (dateTime == DateTime.MaxValue)
        {
            return DateTime.SpecifyKind(DateTime.MaxValue, (DateTimeKind)DateTimeKind.Utc);
        }
        return dateTime.ToUniversalTime();
    }
    public static string ToHexString(byte[] bytes)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException("bytes");
        }
        StringBuilder builder = new StringBuilder(bytes.Length * 2);
        foreach (byte num2 in bytes)
        {
            builder.AppendFormat("{0:x2}", (byte)num2);
        }
        return builder.ToString();
    }
}
