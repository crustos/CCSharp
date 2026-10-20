using Crust;

namespace System {
  public static class Environment {
    public const string NewLine = "\n";
    [Cpp("cs_tick_ms()"), CppInclude("\"cs/time.h\"")]  public static extern long TickCount64 { get; }
    public static extern int TickCount { get; }                                      // not implemented natively (managed code has it)
    public static extern string GetEnvironmentVariable(string variable);             // not implemented natively (managed code has it)
    public static extern void SetEnvironmentVariable(string variable, string value); // not implemented natively (managed code has it)
    [Cpp("exit({0})"), CppInclude("<stdlib.h>")]        public static extern void Exit(int exitCode);
  }
}
