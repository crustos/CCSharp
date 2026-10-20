// UnityEngine.Debug: the log goes to stdout, as a player's does (a warning or error is prefixed, since there is one stream); the drawing calls
// (DrawLine, DrawRay) draw nothing, there being no editor; isDebugBuild is false, as in a release player.

using System;

namespace UnityEngine
{
    public static class Debug
    {
        public static bool isDebugBuild { get { return false; } }

        static string Text(object message) { return message == null ? "Null" : message.ToString(); }

        public static void Log(object message) { Console.WriteLine(Text(message)); }
        public static void Log(object message, Object context) { Log(message); }
        public static void LogWarning(object message) { Console.WriteLine("Warning: " + Text(message)); }
        public static void LogWarning(object message, Object context) { LogWarning(message); }
        public static void LogError(object message) { Console.WriteLine("Error: " + Text(message)); }
        public static void LogError(object message, Object context) { LogError(message); }
        public static void LogException(Exception exception)
        {
            Console.WriteLine(exception == null ? "Exception: Null" : exception.GetType().Name + ": " + exception.Message);
            if (exception != null && !string.IsNullOrEmpty(exception.StackTrace)) Console.WriteLine(exception.StackTrace);
        }
        /// <summary>With the object it came from, as Unity's console shows it (a script's type and its GameObject).</summary>
        public static void LogException(Exception exception, Object context)
        {
            if (context == null) { LogException(exception); return; }
            Console.WriteLine((exception == null ? "Exception: Null" : exception.GetType().Name + ": " + exception.Message) + " (" + context.GetType().Name + " on " + context.name + ")");
            if (exception != null && !string.IsNullOrEmpty(exception.StackTrace)) Console.WriteLine(exception.StackTrace);
        }
        public static void LogFormat(string format, params object[] args) { Log(string.Format(format, args)); }
        public static void LogWarningFormat(string format, params object[] args) { LogWarning(string.Format(format, args)); }
        public static void LogErrorFormat(string format, params object[] args) { LogError(string.Format(format, args)); }

        public static void Assert(bool condition) { if (!condition) LogError("Assertion failed"); }
        public static void Assert(bool condition, object message) { if (!condition) LogError(message); }

        public static void DrawLine(Vector3 start, Vector3 end) { }
        public static void DrawLine(Vector3 start, Vector3 end, Color color) { }
        public static void DrawLine(Vector3 start, Vector3 end, Color color, float duration) { }
        public static void DrawRay(Vector3 start, Vector3 dir) { }
        public static void DrawRay(Vector3 start, Vector3 dir, Color color) { }
        public static void DrawRay(Vector3 start, Vector3 dir, Color color, float duration) { }
        public static void Break() { }
    }
}
