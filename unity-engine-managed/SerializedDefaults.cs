// CCSharp --unity-engine, managed half: what Unity's serializer gives a script before its Awake.  Unity never leaves a serialized field null: a
// [Serializable] class gets an object (whose own serialized fields get the same, to Unity's depth of 10), an array an empty one, a List<T> an empty
// list, a string "".  A scene value set on the field (the packer writes them) is kept.  A field is serialized when it is public or [SerializeField],
// and not static, readonly, const or [NonSerialized]; the engine's own classes (UnityEngine.*) are not walked.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public static class SerializedDefaults
    {
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static void CreateFields(object o) { Create(o, 0); }

        static void Create(object o, int depth)
        {
            if (depth > 10) return;
            for (Type t = o.GetType(); t != null && t != typeof(object) && !IsEngine(t); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(Declared))
                {
                    if (f.IsStatic || f.IsLiteral || f.IsInitOnly || f.IsNotSerialized) continue;
                    if (!f.IsPublic && !f.IsDefined(typeof(SerializeField), false)) continue;
                    object v = f.GetValue(o);
                    if (v == null)
                    {
                        v = New(f.FieldType);
                        if (v == null) continue;
                        f.SetValue(o, v);
                    }
                    if (IsSerializableClass(f.FieldType)) Create(v, depth + 1);
                }
            }
        }

        static bool IsEngine(Type t)
        {
            string ns = t.Namespace;
            return ns != null && (ns == "UnityEngine" || ns.StartsWith("UnityEngine.") || ns == "TMPro");
        }

        static bool IsSerializableClass(Type t)
        {
            return t.IsClass && !t.IsAbstract && !t.IsArray && !t.IsGenericType && t.IsSerializable && t != typeof(string)
                && !t.IsSubclassOf(typeof(Object)) && !IsEngine(t);
        }

        static object New(Type t)
        {
            if (t == typeof(string)) return "";
            if (t.IsArray) return Array.CreateInstance(t.GetElementType(), 0);
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return Activator.CreateInstance(t);
            if (!IsSerializableClass(t)) return null;
            try { return Activator.CreateInstance(t); }
            catch (MissingMethodException) { return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t); }
        }
    }
}
