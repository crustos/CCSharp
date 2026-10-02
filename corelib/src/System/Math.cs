using Crust;

namespace System {
  [CppInclude("\"cs/math.h\"")]
  public static class Math {
    public const double PI = 3.1415926535897931;
    public const double E = 2.7182818284590451;

    [Cpp("cs_abs_i32({0})")]      public static extern int Abs(int value);
    [Cpp("cs_abs_i64({0})")]      public static extern long Abs(long value);
    [Cpp("cs_abs_f64({0})")]      public static extern double Abs(double value);
    [Cpp("cs_max_i32({0}, {1})")] public static extern int Max(int a, int b);
    [Cpp("cs_max_i64({0}, {1})")] public static extern long Max(long a, long b);
    [Cpp("cs_max_f64({0}, {1})")] public static extern double Max(double a, double b);
    [Cpp("cs_min_i32({0}, {1})")] public static extern int Min(int a, int b);
    [Cpp("cs_min_i64({0}, {1})")] public static extern long Min(long a, long b);
    [Cpp("cs_min_f64({0}, {1})")] public static extern double Min(double a, double b);
    [Cpp("cs_sign_i32({0})")]     public static extern int Sign(int value);

    [Cpp("cs_sqrt({0})")]         public static extern double Sqrt(double d);
    [Cpp("cs_floor({0})")]        public static extern double Floor(double d);
    [Cpp("cs_ceil({0})")]         public static extern double Ceiling(double d);
    [Cpp("cs_pow({0}, {1})")]     public static extern double Pow(double x, double y);
    [Cpp("cs_sin({0})")]          public static extern double Sin(double a);
    [Cpp("cs_cos({0})")]          public static extern double Cos(double a);

    public static extern double Round(double a);             // banker's rounding: not implemented yet
    public static extern double Log(double a);
    public static extern double Exp(double a);
    public static extern double Tan(double a);
  }
}
