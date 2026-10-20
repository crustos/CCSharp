/*
  The core types the C# language itself needs.  This corelib is compiled by Roslyn INSTEAD of the .NET
  reference assemblies, so it is also the whole of what a program can name.

  What was kept from the old (GC) corelib is the shape: the same namespaces, the same names.  What is gone is
  everything that assumed a garbage collector: System.Object as a universal root that every class derives
  from, UTF-16 `char` strings, reflection (Type, Class, Method), Thread and Mutex.
*/
namespace System {

  public class Object {
    public Object() {}
    public extern Type GetType();                                   // no reflection in Crust
    public virtual extern string ToString();
    public virtual extern bool Equals(object other);
    public virtual extern int GetHashCode();
  }

  public abstract class ValueType {}
  public abstract class Enum : ValueType {}
  public struct Void {}
  public struct Nullable<T> where T : struct {                        // `T?` is refused natively: use a bool beside the value (managed code has it)
    public Nullable(T value) {}
    public extern bool HasValue { get; }
    public extern T Value { get; }
    public extern T GetValueOrDefault();
    public extern T GetValueOrDefault(T defaultValue);
  }

  // reflection: not in Crust (refused there); declared for managed code, which DotNetAnywhere runs
  public abstract class Type {
    public extern string Name { get; }
    public extern string FullName { get; }
    public extern System.Reflection.MethodInfo GetMethod(string name);
    public extern string Namespace { get; }
    public extern Type BaseType { get; }
    public extern bool IsValueType { get; }
    public extern bool IsEnum { get; }
    public extern bool IsArray { get; }
    public extern bool IsGenericType { get; }
    public extern Type GetElementType();
    public extern Type[] GetGenericArguments();
    public extern Type GetGenericTypeDefinition();
    public extern Type GetNestedType(string name);
    public extern System.Reflection.FieldInfo GetField(string name);
    public extern System.Reflection.FieldInfo GetField(string name, System.Reflection.BindingFlags bindingAttr);
    public extern System.Reflection.FieldInfo[] GetFields();
    public extern System.Reflection.FieldInfo[] GetFields(System.Reflection.BindingFlags bindingAttr);
    public extern System.Reflection.PropertyInfo[] GetProperties();
    public extern System.Reflection.MethodInfo[] GetMethods();
    public static extern Type GetType(string typeName);
    public extern bool IsSerializable { get; }
    public extern bool IsAbstract { get; }
    public extern bool IsInterface { get; }
    public extern bool IsClass { get; }
    public extern bool IsSealed { get; }
    public extern bool IsSubclassOf(Type c);
  }

  public abstract class Delegate {}
  public abstract class MulticastDelegate : Delegate {}

  public abstract class Array {
    public extern int Length { get; }
    public extern int Rank { get; }
    // not implemented natively (managed code has them, on DotNetAnywhere)
    public extern object GetValue(int index);
    public static extern Array CreateInstance(Type elementType, int length);
    public extern int GetLowerBound(int dimension);
    public extern int GetUpperBound(int dimension);
    public extern void SetValue(object value, int index);
    public extern void CopyTo(Array array, int index);
    public extern object Clone();
    public extern int GetLength(int dimension);
    public static extern T[] Empty<T>();
    public static extern int IndexOf<T>(T[] array, T value);
    public static extern int IndexOf<T>(T[] array, T value, int startIndex);
    public static extern void Resize<T>(ref T[] array, int newSize);
    public static extern void Reverse(Array array);
    public static extern void Clear(Array array, int index, int length);
    public static extern void Copy(Array sourceArray, Array destinationArray, int length);
    public static extern void Copy(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length);
  }

  public interface IDisposable { void Dispose(); }
  public delegate int Comparison<in T>(T x, T y);
  public interface IComparable { int CompareTo(object obj); }
  public interface IComparable<in T> { int CompareTo(T other); }
  public interface IEquatable<T> { bool Equals(T other); }

  public delegate void Action();
  public delegate void Action<in T>(T arg);
  public delegate TResult Func<out TResult>();
  public delegate TResult Func<in T, out TResult>(T arg);
  // delegates are managed (Crust has none): these bind for code that runs on DotNetAnywhere
  public delegate void Action<in T1, in T2>(T1 arg1, T2 arg2);
  public delegate void Action<in T1, in T2, in T3>(T1 arg1, T2 arg2, T3 arg3);
  public delegate void Action<in T1, in T2, in T3, in T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
  public delegate TResult Func<in T1, in T2, out TResult>(T1 arg1, T2 arg2);
  public delegate TResult Func<in T1, in T2, in T3, out TResult>(T1 arg1, T2 arg2, T3 arg3);
  public delegate TResult Func<in T1, in T2, in T3, in T4, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
  public delegate bool Predicate<in T>(T obj);
  public delegate TOutput Converter<in TInput, out TOutput>(TInput input);

  // ---- attributes -------------------------------------------------------------------------------

  public abstract class Attribute {}

  [Flags]
  public enum AttributeTargets {
    Assembly = 1, Module = 2, Class = 4, Struct = 8, Enum = 16, Constructor = 32, Method = 64, Property = 128,
    Field = 256, Event = 512, Interface = 1024, Parameter = 2048, Delegate = 4096, ReturnValue = 8192,
    GenericParameter = 16384, All = 32767
  }

  [AttributeUsage(AttributeTargets.Class)]
  public sealed class AttributeUsageAttribute : Attribute {
    public AttributeUsageAttribute(AttributeTargets validOn) {}
    public bool AllowMultiple { get; set; }
    public bool Inherited { get; set; }
  }

  [AttributeUsage(AttributeTargets.Enum)]
  public sealed class FlagsAttribute : Attribute {}

  [AttributeUsage(AttributeTargets.Parameter)]
  public sealed class ParamArrayAttribute : Attribute {}

  public sealed class ObsoleteAttribute : Attribute {
    public ObsoleteAttribute() {}
    public ObsoleteAttribute(string message) {}
  }

  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate)]
  public sealed class SerializableAttribute : Attribute {}

  [AttributeUsage(AttributeTargets.Field)]
  public sealed class NonSerializedAttribute : Attribute {}

  // ---- exceptions: declared so that user code binds; `throw` / `try` are refused by the emitter ----------

  public class Exception {
    public Exception() {}
    public Exception(string message) {}
    public extern string Message { get; }
    public extern virtual string StackTrace { get; }
  }
  public class SystemException : Exception { public SystemException() {} public SystemException(string message) {} }
  public class ArgumentException : SystemException { public ArgumentException() {} public ArgumentException(string message) {} public ArgumentException(string message, string paramName) {} }
  public class ArgumentNullException : ArgumentException { public ArgumentNullException() {} public ArgumentNullException(string message) {} public ArgumentNullException(string paramName, string message) {} }
  public class ArgumentOutOfRangeException : ArgumentException { public ArgumentOutOfRangeException() {} public ArgumentOutOfRangeException(string message) {} public ArgumentOutOfRangeException(string paramName, string message) {} }
  public class InvalidOperationException : SystemException { public InvalidOperationException() {} public InvalidOperationException(string message) {} }
  public class NotImplementedException : SystemException { public NotImplementedException() {} public NotImplementedException(string message) {} }
  public class NotSupportedException : SystemException { public NotSupportedException() {} public NotSupportedException(string message) {} }
  public class FormatException : SystemException { public FormatException() {} public FormatException(string message) {} }
  public class IndexOutOfRangeException : SystemException { public IndexOutOfRangeException() {} public IndexOutOfRangeException(string message) {} }
  public class MissingMethodException : SystemException { public MissingMethodException() {} public MissingMethodException(string message) {} }
}

namespace System.Collections {
  public interface IEnumerator { bool MoveNext(); object Current { get; } void Reset(); }
  public interface IEnumerable { IEnumerator GetEnumerator(); }
  public interface ICollection : IEnumerable { int Count { get; } void CopyTo(Array array, int index); }
  public interface IList : ICollection {
    object this[int index] { get; set; }
    int Add(object value);
    void Clear();
    bool Contains(object value);
    int IndexOf(object value);
    void Insert(int index, object value);
    void Remove(object value);
    void RemoveAt(int index);
  }
}

namespace System.Collections.ObjectModel {
  /* Not implemented natively (managed code has it, on DotNetAnywhere). */
  public class ReadOnlyCollection<T> : System.Collections.Generic.IList<T> {
    public ReadOnlyCollection(System.Collections.Generic.IList<T> list) {}
    public extern int Count { get; }
    public extern T this[int index] { get; set; }
    public extern bool Contains(T value);
    public extern int IndexOf(T value);
    public extern System.Collections.Generic.IEnumerator<T> GetEnumerator();
    extern System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator();
  }
}

namespace System.Text.RegularExpressions {
  [Flags]
  public enum RegexOptions { None = 0, IgnoreCase = 1, Multiline = 2, ExplicitCapture = 4, Compiled = 8, Singleline = 16, IgnorePatternWhitespace = 32,
                             RightToLeft = 64, ECMAScript = 256, CultureInvariant = 512, NonBacktracking = 1024 }
}

namespace System.Threading {
  /* Not implemented natively (managed code has it, on DotNetAnywhere). */
  public delegate void WaitCallback(object state);
  public static class ThreadPool {
    public static extern bool QueueUserWorkItem(WaitCallback callBack);
    public static extern bool QueueUserWorkItem(WaitCallback callBack, object state);
  }
}

namespace System.Collections.Generic {
  public interface IEnumerator<out T> : IDisposable, System.Collections.IEnumerator { T Current { get; } }
  public interface IEnumerable<out T> : System.Collections.IEnumerable { new IEnumerator<T> GetEnumerator(); }
  public interface ICollection<T> : IEnumerable<T> { int Count { get; } }
  public interface IComparer<in T> { int Compare(T x, T y); }
  public interface IEqualityComparer<in T> { bool Equals(T x, T y); int GetHashCode(T obj); }
  public interface IList<T> : ICollection<T> { T this[int index] { get; set; } }
}

namespace System {
  /* Not implemented natively (managed code has it, on DotNetAnywhere). */
  public static class Activator {
    public static extern object CreateInstance(Type type);
    public static extern T CreateInstance<T>();
  }
}

namespace System.Reflection {
  public abstract class MethodBase {
    public extern string Name { get; }
    public extern object Invoke(object obj, object[] parameters);
  }
  public abstract class MethodInfo : MethodBase {}
  [Flags]
  public enum BindingFlags { Default = 0, IgnoreCase = 1, DeclaredOnly = 2, Instance = 4, Static = 8, Public = 16, NonPublic = 32, FlattenHierarchy = 64 }
  public abstract class PropertyInfo {
    public extern string Name { get; }
    public extern Type PropertyType { get; }
  }
  public sealed class FieldInfo {
    public extern string Name { get; }
    public extern Type FieldType { get; }
    public extern Type DeclaringType { get; }
    public extern bool IsStatic { get; }
    public extern bool IsPublic { get; }
    public extern bool IsPrivate { get; }
    public extern bool IsInitOnly { get; }
    public extern bool IsLiteral { get; }
    public extern bool IsNotSerialized { get; }
    public extern object GetValue(object obj);
    public extern void SetValue(object obj, object value);
    public extern bool IsDefined(Type attributeType, bool inherit);
  }
}

namespace System.Diagnostics {
  public class StackTrace {
    public StackTrace() {}
    public extern virtual int FrameCount { get; }
    public extern virtual StackFrame GetFrame(int index);
  }
  public class StackFrame {
    public extern virtual System.Reflection.MethodBase GetMethod();
  }
}

namespace System {
  public enum StringComparison { CurrentCulture, CurrentCultureIgnoreCase, InvariantCulture, InvariantCultureIgnoreCase, Ordinal, OrdinalIgnoreCase }

  /* Not implemented natively (managed code has them, on DotNetAnywhere). */
  public struct ValueTuple<T1, T2> {
    public T1 Item1;
    public T2 Item2;
    public ValueTuple(T1 item1, T2 item2) { Item1 = item1; Item2 = item2; }
  }
  public struct ValueTuple<T1, T2, T3> {
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public ValueTuple(T1 item1, T2 item2, T3 item3) { Item1 = item1; Item2 = item2; Item3 = item3; }
  }
}

namespace System.Runtime.CompilerServices {
  public static class RuntimeHelpers { public static extern object GetUninitializedObject(Type type); }
  public sealed class TupleElementNamesAttribute : Attribute { public TupleElementNamesAttribute(string[] transformNames) {} }

  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
  public sealed class ExtensionAttribute : Attribute {}
}

namespace System.Runtime.InteropServices {
  public sealed class ComVisibleAttribute : Attribute {
    public ComVisibleAttribute(bool visibility) {}
  }
}
