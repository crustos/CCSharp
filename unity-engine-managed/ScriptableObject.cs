// UnityEngine.ScriptableObject: a data object that is not attached to a GameObject.  CreateInstance makes one and calls Awake and OnEnable, as Unity's does.
// (An asset a script field refers to is not loaded: such a field is null.)

using System;

namespace UnityEngine
{
    public class ScriptableObject : Object
    {
        public virtual void Awake() { }
        public virtual void OnEnable() { }
        public virtual void OnDisable() { }
        public virtual void OnValidate() { }
        public virtual void Reset() { }

        public static T CreateInstance<T>() where T : ScriptableObject, new()
        {
            T o = new T();
            o.name = "";
            SerializedDefaults.CreateFields(o);
            o.Awake();
            o.OnEnable();
            return o;
        }

        public static ScriptableObject CreateInstance(Type type)
        {
            ScriptableObject o = (ScriptableObject)Activator.CreateInstance(type);
            o.name = "";
            SerializedDefaults.CreateFields(o);
            o.Awake();
            o.OnEnable();
            return o;
        }
    }
}
