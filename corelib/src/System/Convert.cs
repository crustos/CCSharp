using Crust;

namespace System {
  public static class Convert {
    [Cpp("cs_parse_i32({0})"), CppInclude("\"cs/core.h\"")]         public static extern int ToInt32(string value);
    [Cpp("cs_parse_i64_or_fail({0})"), CppInclude("\"cs/core.h\"")] public static extern long ToInt64(string value);
    [Cpp("cs_i64_str({0})"), CppInclude("\"cs/core.h\"")]           public static extern string ToString(int value);
    [Cpp("cs_i64_str({0})"), CppInclude("\"cs/core.h\"")]           public static extern string ToString(long value);
    [Cpp("cs_bool_str({0})"), CppInclude("\"cs/core.h\"")]          public static extern string ToString(bool value);
  }
}

namespace System {
  /* Not implemented natively (managed code has it, on DotNetAnywhere). */
  public static class BitConverter {
    public static readonly bool IsLittleEndian;
    public static extern long DoubleToInt64Bits(double value);
    public static extern double Int64BitsToDouble(long value);
    public static extern int SingleToInt32Bits(float value);
    public static extern float Int32BitsToSingle(int value);
    public static extern byte[] GetBytes(bool value);
    public static extern byte[] GetBytes(char value);
    public static extern byte[] GetBytes(short value);
    public static extern byte[] GetBytes(ushort value);
    public static extern byte[] GetBytes(int value);
    public static extern byte[] GetBytes(uint value);
    public static extern byte[] GetBytes(long value);
    public static extern byte[] GetBytes(ulong value);
    public static extern byte[] GetBytes(float value);
    public static extern byte[] GetBytes(double value);
    public static extern bool ToBoolean(byte[] value, int startIndex);
    public static extern char ToChar(byte[] value, int startIndex);
    public static extern short ToInt16(byte[] value, int startIndex);
    public static extern ushort ToUInt16(byte[] value, int startIndex);
    public static extern int ToInt32(byte[] value, int startIndex);
    public static extern uint ToUInt32(byte[] value, int startIndex);
    public static extern long ToInt64(byte[] value, int startIndex);
    public static extern ulong ToUInt64(byte[] value, int startIndex);
    public static extern float ToSingle(byte[] value, int startIndex);
    public static extern double ToDouble(byte[] value, int startIndex);
  }
}
