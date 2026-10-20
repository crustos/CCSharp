// CCSharp --unity-engine: a small UnityEngine, written in the C# subset that CCSharp lowers (and that also runs on .NET, so it can be tested against it).
//
// Objects are arena classes ([MaxInstances(N)], see the README): a reference is a pointer, `null` and `==` work, and the classes derive from one another
// (Component -> Transform, MonoBehaviour, SpriteRenderer, Camera).  A script is a class that derives from MonoBehaviour, as in Unity.
//
// A scene is built by code (the packer writes it): `new GameObject("Hero")`, `go.Attach(component)`.  There is no reflection, so the engine does not look
// for a script's `Update` by name: MonoBehaviour declares the messages `virtual`, and a script overrides them.
//
// What the subset cannot take (coroutines, overloads told apart by type, `params`) is under `#if UNITY_MANAGED`: ccs2c defines it when the program uses
// unity-engine-managed (with --dna), and then these classes are managed too.  A program that uses none of it stays all native.

using System;
using System.Collections.Generic;

namespace UnityEngine;

class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }

public enum FindObjectsSortMode { None = 0, InstanceID = 1 }
public enum FindObjectsInactive { Exclude = 0, Include = 1 }

public static class Time
{
    /// <summary>The frame's time, scaled: the host sets unscaledDeltaTime (or, as before, deltaTime itself) and Tick multiplies in timeScale.</summary>
    public static float deltaTime;
    public static float time;
    public static float unscaledDeltaTime;
    public static float unscaledTime;
    public static float timeScale = 1f;
    public static float fixedDeltaTime = 0.02f;
    public static float timeSinceLevelLoad;
    public static float realtimeSinceStartup;
    public static float maximumDeltaTime = 0.3333333f;
    public static int frameCount;
}

/// <summary>The window's size in pixels (the host sets it).</summary>
public static class Screen
{
    public static int width = 800;
    public static int height = 600;
    public static float dpi = 96f;
    public static bool fullScreen;
}

/// <summary>What Destroy and Instantiate work on.  Abstract: its N is only the default for the classes that derive from it.</summary>
[MaxInstances(256)]
public abstract class Object
{
    public bool destroyed;
    public int instanceId;
    public string objectName;

    /// <summary>A component's name is its GameObject's.</summary>
    // ponytail: not virtual (Component overriding it) because CrustEmitter passes a string argument of a vtable call without copying it, so the
    // virtual setter and its caller both release it (a double free); fix that in the emitter's virtual-call path and make this virtual again.
    public string name
    {
        get
        {
            Component c = this as Component;
            if (c != null) return c.gameObject.objectName;
            return objectName;
        }
        set
        {
            Component c = this as Component;
            if (c != null) c.gameObject.objectName = value;
            else objectName = value;
        }
    }

    public int GetInstanceID() { return instanceId; }

#if UNITY_MANAGED
    public long sourceId;               // the fileID the scene or prefab gave it (the packer sets it in a prefab's template): what a reference names

    /// <summary>The component of `root`'s hierarchy that the asset's fileID `id` named (a prefab's template), or null.</summary>
    public static T FindSource<T>(GameObject root, long id) where T : class
    {
        foreach (Component c in root.components)
            if (c.sourceId == id && c is T) return c as T;
        for (int i = 0; i < root.transform.childCount; i++)
        {
            T t = FindSource<T>(root.transform.GetChild(i).gameObject, id);
            if (t != null) return t;
        }
        return null;
    }

    /// <summary>`if (GetComponent<BoxCollider>())`: there, and not destroyed.</summary>
    public static implicit operator bool(Object exists) { return (object)exists != null && !exists.destroyed; }
#endif

    /// <summary>Called when the object is destroyed (a script's OnDestroy).  It is also what gives the hierarchy a type to ask about: `as` needs a virtual.</summary>
    public virtual void OnDestroy() { }

    public static void Destroy(Object o) { Engine.Destroy(o); }

    /// <summary>Destroys the object after t seconds of game time.</summary>
    public static void Destroy(Object o, float t) { Engine.DestroyAfter(o, t); }

    /// <summary>Keeps the object's root GameObject when another scene is loaded.</summary>
    public static void DontDestroyOnLoad(Object o) { Engine.KeepOnLoad(o); }

    /// <summary>The first live object of type T in an active GameObject (or in any, with includeInactive).</summary>
    public static T FindObjectOfType<T>() where T : Object { return Engine.FindFirst<T>(false); }
    public static T FindObjectOfType<T>(bool includeInactive) where T : Object { return Engine.FindFirst<T>(includeInactive); }

    /// <summary>Every live object of type T in an active GameObject (or in any, with includeInactive).</summary>
    public static T[] FindObjectsOfType<T>() where T : Object { return Engine.FindAll<T>(false); }
    public static T[] FindObjectsOfType<T>(bool includeInactive) where T : Object { return Engine.FindAll<T>(includeInactive); }

    public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Object { return Engine.FindAll<T>(false); }
    public static T[] FindObjectsByType<T>(FindObjectsInactive findObjectsInactive, FindObjectsSortMode sortMode) where T : Object
    {
        return Engine.FindAll<T>(findObjectsInactive == FindObjectsInactive.Include);
    }
    public static T FindFirstObjectByType<T>() where T : Object { return Engine.FindFirst<T>(false); }
    public static T FindAnyObjectByType<T>() where T : Object { return Engine.FindFirst<T>(false); }

    /// <summary>A copy of a GameObject (made by its prefab), or of a component's GameObject (then its component of the same place is returned).</summary>
    public static T Instantiate<T>(T original) where T : Object { return Engine.Clone<T>(original); }

    public static T Instantiate<T>(T original, Transform parent) where T : Object
    {
        T copy = Engine.Clone<T>(original);
        Transform t = Engine.TransformOf(copy);
        if (t != null) t.SetParent(parent, false);
        return copy;
    }

    /// <summary>A copy placed at a world position and rotation.</summary>
    public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object
    {
        T copy = Engine.Clone<T>(original);
        Transform t = Engine.TransformOf(copy);
        if (t != null)
        {
            t.position = position;
            t.rotation = rotation;
        }
        return copy;
    }

    /// <summary>A copy under `parent`, at a world position and rotation.</summary>
    public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation, Transform parent) where T : Object
    {
        T copy = Engine.Clone<T>(original);
        Transform t = Engine.TransformOf(copy);
        if (t != null)
        {
            t.SetParent(parent, false);
            t.position = position;
            t.rotation = rotation;
        }
        return copy;
    }

#if UNITY_MANAGED
    public static T Instantiate<T>(T original, Transform parent, bool instantiateInWorldSpace) where T : Object
    {
        T copy = Engine.Clone<T>(original);
        Transform t = Engine.TransformOf(copy);
        if (t != null) t.SetParent(parent, instantiateInWorldSpace);
        return copy;
    }

    /// <summary>Destroys the object now, not at the end of the frame.</summary>
    public static void DestroyImmediate(Object o) { Engine.Destroy(o); Engine.Sweep(); }
    public static void DestroyImmediate(Object o, bool allowDestroyingAssets) { DestroyImmediate(o); }

    public static bool operator true(Object o) { return o != null && !o.destroyed; }
    public static bool operator false(Object o) { return o == null || o.destroyed; }
    public static bool operator !(Object o) { return o == null || o.destroyed; }

    public override string ToString() { return name + " (" + GetType().Name + ")"; }
#endif
}

/// <summary>Gives an arena object's slot back (the compiler writes the call: nothing is freed on .NET, whose GC does it).</summary>
public static class Arena
{
    public static void Free<T>(T o) where T : Object { }
}

/// <summary>Builds a GameObject (with its components) the way the prefab it stands for does.  The packer writes a subclass for each prefab.</summary>
[MaxInstances(64)]
public abstract class Prefab
{
    public abstract GameObject Create();
}

/// <summary>Builds a scene's GameObjects (the packer writes a subclass, and sets Engine.sceneLoader to it).</summary>
[MaxInstances(4)]
public abstract class SceneLoader
{
    public abstract void Load(int buildIndex);
}

/// <summary>Base of everything attached to a GameObject.</summary>
public abstract class Component : Object
{
    public GameObject gameObject;
    public Transform transform;

    public string tag
    {
        get { return gameObject.tag; }
        set { gameObject.tag = value; }
    }

    public bool CompareTag(string t) { return gameObject.tag == t; }

    /// <summary>Called when the component is attached to a GameObject: where a kind of component registers itself with the engine.</summary>
    public virtual void OnAttach() { }

#if UNITY_MANAGED
    public T GetComponent<T>() { return gameObject.GetComponent<T>(); }
#else
    public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
#endif
    public T[] GetComponents<T>()
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
        { return gameObject.GetComponents<T>(); }
    public T GetComponentInChildren<T>()
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
        { return gameObject.GetComponentInChildren<T>(false); }
    public T GetComponentInChildren<T>(bool includeInactive)
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
        { return gameObject.GetComponentInChildren<T>(includeInactive); }
    public T[] GetComponentsInChildren<T>()
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
        { return gameObject.GetComponentsInChildren<T>(false); }
    public T[] GetComponentsInChildren<T>(bool includeInactive)
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
        { return gameObject.GetComponentsInChildren<T>(includeInactive); }
    public T GetComponentInParent<T>()
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
        { return gameObject.GetComponentInParent<T>(); }
}

[MaxInstances(256)]
public class GameObject : Object
{
    public Prefab prefab;               // set on a prefab's template and on what it makes: Instantiate(template) calls prefab.Create()
    public int layer;
    public string tag;
    public bool activeSelf;
    public bool keepOnLoad;             // DontDestroyOnLoad
    public Transform transform;
    public List<Component> components;

    public GameObject()
    {
        Init();
        name = "New Game Object";
    }

    // (the name is assigned here, not passed to Init: a string parameter handed on to another method is released twice in the C)
    public GameObject(string n)
    {
        Init();
        name = n;
    }

    void Init()
    {
        tag = "Untagged";
        activeSelf = true;
        instanceId = Engine.NextId();
        components = new List<Component>();
        transform = new Transform();
        transform.localScale = new Vector3(1f, 1f, 1f);
        transform.localRotation = Quaternion.identity;
        Attach(transform);
        Engine.objects.Add(this);
    }

    public GameObject gameObject { get { return this; } }

    /// <summary>Active, and so are all its parents.</summary>
    public bool activeInHierarchy
    {
        get
        {
            if (!activeSelf) return false;
            Transform p = transform.parent;
            if (p == null) return true;
            return p.gameObject.activeInHierarchy;
        }
    }

    public bool CompareTag(string t) { return tag == t; }

    public void Attach(Component c)
    {
        c.gameObject = this;
        c.transform = transform;
        c.instanceId = Engine.NextId();
        components.Add(c);
#if UNITY_MANAGED
        if (c is MonoBehaviour) SerializedDefaults.CreateFields(c);
#endif
        c.OnAttach();
    }

    /// <summary>The scripts below an object that this activates or deactivates get OnEnable / OnDisable (and Awake, the first time it becomes active).</summary>
    public void SetActive(bool value)
    {
        if (value == activeSelf) return;
        bool was = activeInHierarchy;
        activeSelf = value;
        bool now = activeInHierarchy;
        if (was != now) Engine.ActivityChanged(this, now);
    }

    /// <summary>The first component of type T (or derived from it), else null.  The compiler lowers `c as T` to a type test on the object itself.</summary>
#if UNITY_MANAGED
    /// <summary>Unity's has no constraint: T may be an interface a component implements, or a type parameter of the caller's.</summary>
    public T GetComponent<T>()
    {
        foreach (Component c in components)
        {
            if (c is T) return (T)(object)c;
        }
        return default(T);
    }
#else
    public T GetComponent<T>() where T : Component
    {
        foreach (Component c in components)
        {
            T t = c as T;
            if (t != null) return t;
        }
        return null;
    }
#endif

    public T[] GetComponents<T>()
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
    {
        List<T> found = new List<T>();
        foreach (Component c in components)
        {
            T t = c as T;
            if (t != null) found.Add(t);
        }
        return Engine.ToArray<T>(found);
    }

    public T GetComponentInChildren<T>(bool includeInactive)
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
    {
        if (!includeInactive && !activeInHierarchy) return null;
        T own = GetComponent<T>();
        if (own != null) return own;
        for (int i = 0; i < transform.childCount; i++)
        {
            T t = transform.GetChild(i).gameObject.GetComponentInChildren<T>(includeInactive);
            if (t != null) return t;
        }
        return null;
    }

    public T GetComponentInChildren<T>()
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
        { return GetComponentInChildren<T>(false); }

    public T[] GetComponentsInChildren<T>(bool includeInactive)
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
    {
        List<T> found = new List<T>();
        CollectInChildren<T>(found, includeInactive);
        return Engine.ToArray<T>(found);
    }

    public T[] GetComponentsInChildren<T>()
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
        { return GetComponentsInChildren<T>(false); }

    void CollectInChildren<T>(List<T> found, bool includeInactive)
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
    {
        if (!includeInactive && !activeInHierarchy) return;
        foreach (Component c in components)
        {
            T t = c as T;
            if (t != null) found.Add(t);
        }
        for (int i = 0; i < transform.childCount; i++) transform.GetChild(i).gameObject.CollectInChildren<T>(found, includeInactive);
    }

    /// <summary>The first T on this object or a parent of it.</summary>
    public T GetComponentInParent<T>()
#if UNITY_MANAGED
        where T : class
#else
        where T : Component
#endif
    {
        T own = GetComponent<T>();
        if (own != null) return own;
        Transform p = transform.parent;
        if (p == null) return null;
        return p.gameObject.GetComponentInParent<T>();
    }

#if UNITY_MANAGED
    /// <summary>Gives the object a RectTransform in place of its Transform (a UI object: the packer calls it before anything is attached or parented).</summary>
    public RectTransform MakeRect()
    {
        RectTransform r = transform as RectTransform;
        if (r != null) return r;
        Transform old = transform;
        r = new RectTransform();
        r.localPosition = old.localPosition;
        r.localRotation = old.localRotation;
        r.localScale = old.localScale;
        r.gameObject = this;
        r.instanceId = old.instanceId;
        components[0] = r;
        transform = r;
        foreach (Component c in components) c.transform = r;
        return r;
    }
#endif

#if UNITY_MANAGED
    public T AddComponent<T>() where T : Component
    {
        T c = Activator.CreateInstance<T>();
        Attach(c);
        MonoBehaviour b = c as MonoBehaviour;
        if (b != null) Engine.WakeNow(b);        // as Unity's: Awake and OnEnable before AddComponent returns (on an active object)
        return c;
    }
#else
    public T AddComponent<T>() where T : Component, new()
    {
        T c = new T();
        Attach(c);
        MonoBehaviour b = c as MonoBehaviour;
        if (b != null) Engine.WakeNow(b);        // as Unity's: Awake and OnEnable before AddComponent returns (on an active object)
        return c;
    }
#endif

    /// <summary>The active GameObject of that name (a path "a/b" looks below a root named a).</summary>
    public static GameObject Find(string name) { return Engine.Find(name); }

    public static GameObject FindWithTag(string tag) { return Engine.FindWithTag(tag); }

    public static GameObject FindGameObjectWithTag(string tag) { return Engine.FindWithTag(tag); }

    public static GameObject[] FindGameObjectsWithTag(string tag) { return Engine.FindAllWithTag(tag); }
}

public class Transform : Component
{
    public Vector3 localPosition;
    public Vector3 localScale;
    public Quaternion localRotation;
    Transform parentTransform;
    List<Transform> children;

    public Transform() { children = new List<Transform>(); }

    /// <summary>Setting it keeps the world position, rotation and scale, as Unity's does (SetParent(p, false) keeps the local ones).</summary>
    public Transform parent
    {
        get { return parentTransform; }
        set { SetParent(value, true); }
    }

    public Transform root
    {
        get
        {
            Transform t = this;
            while (t.parentTransform != null) t = t.parentTransform;
            return t;
        }
    }

    public int childCount { get { return children.Count; } }

    public Transform GetChild(int index) { return children[index]; }

    public void SetParent(Transform p) { SetParent(p, true); }

    public void SetParent(Transform p, bool worldPositionStays)
    {
        if (p == parentTransform) return;
        Vector3 wp = position;
        Quaternion wr = rotation;
        Vector3 ws = lossyScale;
        bool was = gameObject.activeInHierarchy;
        if (parentTransform != null) parentTransform.RemoveChild(this);
        parentTransform = p;
        if (p != null) p.children.Add(this);
        if (worldPositionStays)
        {
            rotation = wr;
            position = wp;
            if (p == null) localScale = ws;
            else
            {
                Vector3 ps = p.lossyScale;
                localScale = new Vector3(ps.x == 0f ? ws.x : ws.x / ps.x, ps.y == 0f ? ws.y : ws.y / ps.y, ps.z == 0f ? ws.z : ws.z / ps.z);
            }
        }
        bool now = gameObject.activeInHierarchy;
        if (was != now) Engine.ActivityChanged(gameObject, now);
    }

    void RemoveChild(Transform c)
    {
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] == c) { children.RemoveAt(i); return; }
        }
    }

    /// <summary>Its place among its parent's children (or among the scene's root objects).</summary>
    public int GetSiblingIndex()
    {
        if (parentTransform == null) return Engine.RootIndex(gameObject);
        for (int i = 0; i < parentTransform.children.Count; i++)
        {
            if (parentTransform.children[i] == this) return i;
        }
        return 0;
    }

    public void SetSiblingIndex(int index)
    {
        if (parentTransform == null) return;
        parentTransform.RemoveChild(this);
        if (index < 0) index = 0;
        if (index > parentTransform.children.Count) index = parentTransform.children.Count;
        parentTransform.children.Insert(index, this);
    }

    public void SetAsFirstSibling() { SetSiblingIndex(0); }

    public void SetAsLastSibling() { if (parentTransform != null) SetSiblingIndex(parentTransform.children.Count); }

    public void DetachChildren()
    {
        while (children.Count > 0) children[children.Count - 1].SetParent(null, true);
    }

    /// <summary>The child of that name, or a descendant by path ("a/b").</summary>
    public Transform Find(string n)
    {
        int slash = n.IndexOf("/");
        string first = n;
        if (slash >= 0) first = n.Substring(0, slash);
        for (int i = 0; i < children.Count; i++)
        {
            Transform c = children[i];
            if (c.gameObject.name == first)
            {
                if (slash < 0) return c;
                Transform deeper = c.Find(n.Substring(slash + 1));
                if (deeper != null) return deeper;
            }
        }
        return null;
    }

    public bool IsChildOf(Transform p)
    {
        Transform t = this;
        while (t != null)
        {
            if (t == p) return true;
            t = t.parentTransform;
        }
        return false;
    }

    /// <summary>The scale in the world: the parents' scales multiplied in (not a shear: a rotated parent with a non-uniform scale is approximated).</summary>
    public Vector3 lossyScale
    {
        get
        {
            if (parentTransform == null) return localScale;
            Vector3 ps = parentTransform.lossyScale;
            return new Vector3(ps.x * localScale.x, ps.y * localScale.y, ps.z * localScale.z);
        }
    }

    public Quaternion rotation
    {
        get
        {
            if (parentTransform == null) return localRotation;
            Quaternion pr = parentTransform.rotation;
            return pr * localRotation;
        }
        set
        {
            if (parentTransform == null) localRotation = value;
            else
            {
                Quaternion inv = Quaternion.Inverse(parentTransform.rotation);
                localRotation = inv * value;
            }
        }
    }

    public Vector3 eulerAngles
    {
        get { return rotation.eulerAngles; }
        set { rotation = Quaternion.Euler(value); }
    }

    public Vector3 localEulerAngles
    {
        get { return localRotation.eulerAngles; }
        set { localRotation = Quaternion.Euler(value); }
    }

    /// <summary>The local axes in the world.  Setting one turns the transform the shortest way to it.</summary>
    public Vector3 right
    {
        get { Quaternion q = rotation; return q * new Vector3(1f, 0f, 0f); }
        set { rotation = Quaternion.FromToRotation(new Vector3(1f, 0f, 0f), value); }
    }

    public Vector3 up
    {
        get { Quaternion q = rotation; return q * new Vector3(0f, 1f, 0f); }
        set { rotation = Quaternion.FromToRotation(new Vector3(0f, 1f, 0f), value); }
    }

    public Vector3 forward
    {
        get { Quaternion q = rotation; return q * new Vector3(0f, 0f, 1f); }
        set { rotation = Quaternion.LookRotation(value); }
    }

    /// <summary>A point of this transform's space in the world.</summary>
    public Vector3 TransformPoint(Vector3 p)
    {
        Vector3 s = lossyScale;
        Quaternion q = rotation;
        Vector3 scaled = new Vector3(p.x * s.x, p.y * s.y, p.z * s.z);
        Vector3 r = q * scaled;
        Vector3 origin = position;
        return origin + r;
    }

    /// <summary>A world point in this transform's space.</summary>
    public Vector3 InverseTransformPoint(Vector3 p)
    {
        Vector3 rel = p - position;
        Vector3 d = Quaternion.Inverse(rotation) * rel;
        Vector3 s = lossyScale;
        return new Vector3(s.x == 0f ? 0f : d.x / s.x, s.y == 0f ? 0f : d.y / s.y, s.z == 0f ? 0f : d.z / s.z);
    }

    /// <summary>A direction turned into the world (not scaled).</summary>
    public Vector3 TransformDirection(Vector3 d) { return rotation * d; }

    public Vector3 InverseTransformDirection(Vector3 d) { return Quaternion.Inverse(rotation) * d; }

    /// <summary>A vector turned and scaled into the world (not moved).</summary>
    public Vector3 TransformVector(Vector3 v)
    {
        Vector3 s = lossyScale;
        Quaternion q = rotation;
        return q * new Vector3(v.x * s.x, v.y * s.y, v.z * s.z);
    }

    public Vector3 InverseTransformVector(Vector3 v)
    {
        Vector3 d = Quaternion.Inverse(rotation) * v;
        Vector3 s = lossyScale;
        return new Vector3(s.x == 0f ? 0f : d.x / s.x, s.y == 0f ? 0f : d.y / s.y, s.z == 0f ? 0f : d.z / s.z);
    }

    /// <summary>The world position: the parents' scale, rotation and position applied to the local one.</summary>
    public Vector3 position
    {
        get
        {
            if (parentTransform == null) return localPosition;
            return parentTransform.TransformPoint(localPosition);
        }
        set
        {
            if (parentTransform == null)
            {
                localPosition = value;
                return;
            }
            localPosition = parentTransform.InverseTransformPoint(value);
        }
    }

    public void Translate(Vector3 delta) { position = position + delta; }

    /// <summary>Turns by euler angles in degrees, about the local axes.</summary>
    public void Rotate(Vector3 eulers)
    {
        Quaternion cur = localRotation;
        localRotation = cur * Quaternion.Euler(eulers);
    }

    public void Rotate(float xAngle, float yAngle, float zAngle) { Rotate(new Vector3(xAngle, yAngle, zAngle)); }

    /// <summary>Turns about the z axis, in degrees (the 2D rotation).</summary>
    public void RotateZ(float degrees)
    {
        Quaternion turn = Quaternion.Euler(0f, 0f, degrees);
        Quaternion cur = localRotation;
        localRotation = cur * turn;
    }

    public void LookAt(Vector3 worldPosition)
    {
        Vector3 d = worldPosition - position;
        if (d.x != 0f || d.y != 0f || d.z != 0f) rotation = Quaternion.LookRotation(d);
    }

#if UNITY_MANAGED
    public void LookAt(Transform target) { LookAt(target.position); }

    public void Translate(Vector3 translation, Space relativeTo)
    {
        if (relativeTo == Space.World) position = position + translation;
        else position = position + TransformDirection(translation);
    }

    public void Translate(float x, float y, float z) { Translate(new Vector3(x, y, z)); }

    public void Rotate(Vector3 eulers, Space relativeTo)
    {
        Quaternion q = Quaternion.Euler(eulers);
        if (relativeTo == Space.Self) localRotation = localRotation * q;
        else rotation = q * rotation;
    }

    /// <summary>Turns `angle` degrees about `axis` (local axes: Space.Self).</summary>
    public void Rotate(Vector3 axis, float angle) { Rotate(axis, angle, Space.Self); }

    public void Rotate(Vector3 axis, float angle, Space relativeTo)
    {
        if (relativeTo == Space.Self) rotation = rotation * Quaternion.AngleAxis(angle, axis);
        else rotation = Quaternion.AngleAxis(angle, axis) * rotation;
    }

    public void RotateAround(Vector3 point, Vector3 axis, float angle)
    {
        Quaternion q = Quaternion.AngleAxis(angle, axis);
        position = point + q * (position - point);
        rotation = q * rotation;
    }

    public void SetPositionAndRotation(Vector3 p, Quaternion r) { position = p; rotation = r; }

    public Matrix4x4 localToWorldMatrix { get { return Matrix4x4.TRS(position, rotation, lossyScale); } }
    public Matrix4x4 worldToLocalMatrix { get { return localToWorldMatrix.inverse; } }

    /// <summary>Its children, as `foreach (Transform child in transform)` gives them.</summary>
    public System.Collections.IEnumerator GetEnumerator() { return new List<Transform>(children).GetEnumerator(); }
#endif
}

/// <summary>A component that can be turned off (its `enabled`).</summary>
public abstract class Behaviour : Component
{
    bool isEnabled = true;

    public bool enabled
    {
        get { return isEnabled; }
        set
        {
            if (value == isEnabled) return;
            isEnabled = value;
            OnEnabledChanged(value);
        }
    }

    public bool isActiveAndEnabled { get { return isEnabled && gameObject.activeInHierarchy; } }

    /// <summary>What a kind of behaviour does when enabled changes (a script gets OnEnable / OnDisable).</summary>
    public virtual void OnEnabledChanged(bool value) { }
}

/// <summary>A script.  Unity finds its messages by name; here they are virtual, and the packer writes `override` on the ones a script declares.</summary>
public abstract class MonoBehaviour : Behaviour
{
    public bool awoken;
    public bool started;
    public int executionOrder;          // Unity's Script Execution Order (the packer sets it from the script's .meta): lower goes first
    public int updateMark;              // the tick (or fixed step) this script last had Update, LateUpdate, FixedUpdate in
    public int lateMark;
    public int fixedMark;

    public virtual void Awake() { }
    public virtual void OnEnable() { }
    public virtual void Start() { }
    public virtual void Update() { }
    public virtual void LateUpdate() { }
    public virtual void FixedUpdate() { }
    public virtual void OnDisable() { }
    public virtual void OnCollisionEnter2D(Collision2D c) { }
    public virtual void OnCollisionStay2D(Collision2D c) { }
    public virtual void OnCollisionExit2D(Collision2D c) { }
    public virtual void OnTriggerEnter2D(Collider2D c) { }
    public virtual void OnTriggerStay2D(Collider2D c) { }
    public virtual void OnTriggerExit2D(Collider2D c) { }

    /// <summary>Joins the scripts in execution order (after those of the same order already there).</summary>
    public override void OnAttach()
    {
        int at = Engine.behaviours.Count;
        while (at > 0 && Engine.behaviours[at - 1].executionOrder > executionOrder) at--;
        if (at == Engine.behaviours.Count) Engine.behaviours.Add(this);
        else Engine.behaviours.Insert(at, this);
    }

    /// <summary>OnEnable / OnDisable for a script whose object is active (and awake: a script that has not had Awake gets OnEnable with it).</summary>
    public override void OnEnabledChanged(bool value)
    {
        if (!awoken || !gameObject.activeInHierarchy) return;
        Engine.Send(this, value ? Engine.MsgOnEnable : Engine.MsgOnDisable);
    }

#if UNITY_MANAGED
    public Coroutine StartCoroutine(System.Collections.IEnumerator routine) { return Coroutines.Start(this, routine); }

    public void StopCoroutine(Coroutine routine) { Coroutines.Stop(routine); }

    public void StopCoroutine(System.Collections.IEnumerator routine) { Coroutines.Stop(this, routine); }

    public void StopAllCoroutines() { Coroutines.StopAll(this); }

    /// <summary>Calls the method of that name (no arguments) after `time` seconds, by reflection.</summary>
    public void Invoke(string methodName, float time) { Coroutines.Invoke(this, methodName, time, -1f); }

    public void InvokeRepeating(string methodName, float time, float repeatRate) { Coroutines.Invoke(this, methodName, time, repeatRate); }

    public void CancelInvoke() { Coroutines.CancelInvoke(this, null); }

    public void CancelInvoke(string methodName) { Coroutines.CancelInvoke(this, methodName); }

    public bool IsInvoking(string methodName) { return Coroutines.IsInvoking(this, methodName); }

    public static void print(object message) { Debug.Log(message); }
#endif
}

/// <summary>What draws: whether it does, and where it sorts.  (Each kind sets enabled when attached: the subset has no base.M() call.)</summary>
public abstract class Renderer : Component
{
    public bool enabled;
    public int sortingOrder;
    public int sortingLayer;            // the index of the sorting layer in the project's list

#if UNITY_MANAGED
    Material m_Material;

    public Material sharedMaterial
    {
        get { if (m_Material == null) m_Material = new Material(Shader.Find("Sprites/Default")); return m_Material; }
        set { m_Material = value; }
    }
    public Material material { get { return sharedMaterial; } set { sharedMaterial = value; } }
    public Material[] sharedMaterials { get { return new Material[] { sharedMaterial }; } set { m_Material = value != null && value.Length > 0 ? value[0] : null; } }
    public Material[] materials { get { return sharedMaterials; } set { sharedMaterials = value; } }
    public bool isVisible { get { return enabled && gameObject.activeInHierarchy; } }
    public virtual Bounds bounds { get { return new Bounds(transform.position, Vector3.zero); } }
    public Bounds localBounds { get { Bounds b = bounds; b.center = transform.InverseTransformPoint(b.center); return b; } }
    public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;
    public bool receiveShadows;
    public MotionVectorGenerationMode motionVectorGenerationMode;
    public uint renderingLayerMask = 1;
    public int rendererPriority;

    public int sortingLayerID { get { return SortingLayer.IndexToID(sortingLayer); } set { sortingLayer = SortingLayer.IDToIndex(value); } }
    public string sortingLayerName { get { return SortingLayer.IDToName(sortingLayerID); } set { sortingLayer = SortingLayer.IDToIndex(SortingLayer.NameToID(value)); } }
#endif
}

public class SpriteRenderer : Renderer
{
    public int spriteId;                // the host's texture index
    public float halfW, halfH;          // the sprite's half size in world units at scale 1; the draw multiplies in the transform's scale
    public Color color;
    public bool flipX;
    public bool flipY;

    public override void OnAttach()
    {
        enabled = true;
        Engine.sprites.Add(this);
    }

#if UNITY_MANAGED
    Sprite m_Sprite;

    /// <summary>The sprite drawn: setting it draws its texture (the host's) at its size.</summary>
    public Sprite sprite
    {
        get { return m_Sprite; }
        set
        {
            m_Sprite = value;
            if (value == null) { spriteId = -1; return; }
            spriteId = value.texture != null ? value.texture.hostId : -1;
            halfW = value.rect.width / value.pixelsPerUnit / 2f;
            halfH = value.rect.height / value.pixelsPerUnit / 2f;
        }
    }

    public Vector2 size { get { return new Vector2(halfW * 2f, halfH * 2f); } set { halfW = value.x / 2f; halfH = value.y / 2f; } }
    public SpriteDrawMode drawMode;
    public SpriteMaskInteraction maskInteraction;
    public SpriteSortPoint spriteSortPoint;

    /// <summary>The drawn rectangle in the world (its scale and the sprite's pivot taken in).</summary>
    public override Bounds bounds
    {
        get
        {
            Vector3 s = transform.lossyScale;
            Vector3 center = transform.position;
            if (m_Sprite != null) center = transform.TransformPoint(m_Sprite.bounds.center);
            return new Bounds(center, new Vector3(halfW * 2f * Mathf.Abs(s.x), halfH * 2f * Mathf.Abs(s.y), 0f));
        }
    }
#endif
}

public class Camera : Component
{
    public bool enabled = true;
    float aspectOverride;
    public bool orthographic = true;
    public float orthographicSize;
    public Color background;
    public float nearClipPlane = 0.3f;
    public float farClipPlane = 1000f;

    public override void OnAttach() { Engine.mainCamera = this; }

    /// <summary>The scene's camera (the one the host draws with).</summary>
    public static Camera main { get { return Engine.mainCamera; } }

    public Color backgroundColor
    {
        get { return background; }
        set { background = value; }
    }

    /// <summary>The window's width over its height (unless set; ResetAspect goes back to the window's).</summary>
    public float aspect
    {
        get
        {
            if (aspectOverride > 0f) return aspectOverride;
            return Screen.height == 0 ? 1f : (float)Screen.width / (float)Screen.height;
        }
        set { aspectOverride = value; }
    }

    public void ResetAspect() { aspectOverride = 0f; }

    public int pixelWidth { get { return Screen.width; } }
    public int pixelHeight { get { return Screen.height; } }

    /// <summary>A point of the window (pixels from the bottom left; z is the distance from the camera) in the world.  Orthographic.</summary>
    public Vector3 ScreenToWorldPoint(Vector3 p)
    {
        return ViewportToWorldPoint(new Vector3(p.x / (float)Screen.width, p.y / (float)Screen.height, p.z));
    }

    /// <summary>A point of the view (0..1 across, 0..1 up) in the world.  Orthographic.</summary>
    public Vector3 ViewportToWorldPoint(Vector3 v)
    {
        Vector3 c = transform.position;
        float h = orthographicSize;
        float w = h * aspect;
        return new Vector3(c.x + (v.x * 2f - 1f) * w, c.y + (v.y * 2f - 1f) * h, c.z + v.z);
    }

    public Vector3 WorldToViewportPoint(Vector3 p)
    {
        Vector3 c = transform.position;
        float h = orthographicSize;
        float w = h * aspect;
        return new Vector3(((p.x - c.x) / w + 1f) * 0.5f, ((p.y - c.y) / h + 1f) * 0.5f, p.z - c.z);
    }

    public Vector3 WorldToScreenPoint(Vector3 p)
    {
        Vector3 v = WorldToViewportPoint(p);
        return new Vector3(v.x * (float)Screen.width, v.y * (float)Screen.height, v.z);
    }

    public Vector3 ScreenToViewportPoint(Vector3 p) { return new Vector3(p.x / (float)Screen.width, p.y / (float)Screen.height, p.z); }

    public Vector3 ViewportToScreenPoint(Vector3 v) { return new Vector3(v.x * (float)Screen.width, v.y * (float)Screen.height, v.z); }

#if UNITY_MANAGED
    /// <summary>The part of the window the camera draws to, 0..1.</summary>
    public Rect rect = new Rect(0f, 0f, 1f, 1f);

    public void Render() { }
#endif
}

/// <summary>The scene's objects and the frame loop.</summary>
public static class Engine
{
    public static List<GameObject> objects = new List<GameObject>();
    public static List<MonoBehaviour> behaviours = new List<MonoBehaviour>();

    public const int MsgAwake = 0;
    public const int MsgOnEnable = 1;
    public const int MsgStart = 2;
    public const int MsgUpdate = 3;
    public const int MsgLateUpdate = 4;
    public const int MsgFixedUpdate = 5;
    public const int MsgOnDisable = 6;

    /// <summary>A message to a script.  As Unity's: an exception the script throws is logged, and the frame goes on with the next script (on a managed
    /// engine; the native subset has no try, so there the exception ends the program as before).</summary>
    public static void Send(MonoBehaviour b, int message)
    {
#if UNITY_MANAGED
        try { SendNow(b, message); }
        catch (Exception e) { Debug.LogException(e, b); }
#else
        SendNow(b, message);
#endif
    }

    static void SendNow(MonoBehaviour b, int message)
    {
        if (message == MsgAwake) b.Awake();
        else if (message == MsgOnEnable) b.OnEnable();
        else if (message == MsgStart) b.Start();
        else if (message == MsgUpdate) b.Update();
        else if (message == MsgLateUpdate) b.LateUpdate();
        else if (message == MsgFixedUpdate) b.FixedUpdate();
        else b.OnDisable();
    }

    static void SendDestroy(Object o)
    {
#if UNITY_MANAGED
        try { o.OnDestroy(); }
        catch (Exception e) { Debug.LogException(e, o); }
#else
        o.OnDestroy();
#endif
    }
    static int ticks;
    static int fixedSteps;
    public static List<SpriteRenderer> sprites = new List<SpriteRenderer>();
    public static Camera mainCamera;
    public static List<Object> doomed = new List<Object>();
    public static List<Object> later = new List<Object>();      // Destroy(o, t): the objects, and when each goes
    public static List<float> laterAt = new List<float>();
    public static SceneLoader sceneLoader;
    public static int sceneToLoad = -1;                          // a LoadScene waiting for the end of the frame
    public static int activeScene;
    public static bool quitRequested;
    static int nextId = 1;
    static int nextSceneId;
    public static bool loadingScene;    // the packer's SceneData.Build sets it while it builds the scene

    /// <summary>Unity's instance IDs: an object loaded with a scene has a negative one (counting down as the scene loads), one made at run time a
    /// positive one (counting up).  FindObjectOfType takes the lowest, so of two loaded with the scene it is the one loaded last.</summary>
    public static int NextId()
    {
        if (loadingScene)
        {
            nextSceneId = nextSceneId - 1;
            return nextSceneId;
        }
        nextId = nextId + 1;
        return nextId;
    }

    /// <summary>Awake (once, when the script's object is first active), then OnEnable, for a script that has not had them.</summary>
    public static void WakeNow(MonoBehaviour b) { Wake(b); }

    static void Wake(MonoBehaviour b)
    {
        if (b.awoken || b.destroyed || !b.gameObject.activeInHierarchy) return;
        b.awoken = true;
        Send(b, MsgAwake);
        if (b.enabled && !b.destroyed) Send(b, MsgOnEnable);
    }

    /// <summary>Awake / OnEnable now for the scripts of `g`'s hierarchy that have not had them, in execution order (made at run time: Instantiate,
    /// AddComponent).  An inactive object's wait until it is activated, as everywhere.</summary>
    public static void WakeHierarchy(GameObject g)
    {
        List<MonoBehaviour> found = new List<MonoBehaviour>();
        CollectScripts(g, found);
        while (found.Count > 0)
        {
            // the lowest order first, and of one order the first found (the list is short: one object's scripts)
            int at = 0;
            for (int i = 1; i < found.Count; i++) if (found[i].executionOrder < found[at].executionOrder) at = i;
            MonoBehaviour next = found[at];
            found.RemoveAt(at);
            Wake(next);
        }
    }

    static void CollectScripts(GameObject g, List<MonoBehaviour> found)
    {
        foreach (Component c in g.components)
        {
            MonoBehaviour b = c as MonoBehaviour;
            if (b != null && !b.awoken && !b.destroyed) found.Add(b);
        }
        for (int i = 0; i < g.transform.childCount; i++) CollectScripts(g.transform.GetChild(i).gameObject, found);
    }

    static void StartIfReady(MonoBehaviour b)
    {
        if (b.started || !b.awoken || b.destroyed || !b.enabled || !b.gameObject.activeInHierarchy) return;
        b.started = true;
        Send(b, MsgStart);
    }

    /// <summary>One frame: Awake, OnEnable and Start for the scripts that have not had them, then Update, then LateUpdate.</summary>
    public static void Tick()
    {
        ticks += 1;
        InputState.Init();
        if (Time.unscaledDeltaTime > 0f)
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > Time.maximumDeltaTime) dt = Time.maximumDeltaTime;
            Time.deltaTime = dt * Time.timeScale;
        }
#if UNITY_MANAGED
        UnityEngine.InputSystem.InputSystem.Update();      // devices added or removed, and onEvent for what changed since the last frame
        Canvas.UpdateCanvases();                           // the root canvases sized from the screen
#endif
        // by index: a script may Instantiate during its Update, and the new behaviours join the list (they get Awake and Start before their first Update).
        // One that joins before the index shifts the rest along, so each phase marks what it ran: nothing runs twice in a tick.
        for (int i = 0; i < behaviours.Count; i++) Wake(behaviours[i]);
        for (int i = 0; i < behaviours.Count; i++) StartIfReady(behaviours[i]);
        Physics2D.Update(Time.deltaTime);
        for (int i = 0; i < behaviours.Count; i++)
        {
            MonoBehaviour b = behaviours[i];
            Wake(b);
            StartIfReady(b);
            if (b.updateMark == ticks) continue;
            if (b.started && b.enabled && !b.destroyed && b.gameObject.activeInHierarchy) { b.updateMark = ticks; Send(b, MsgUpdate); }
        }
#if UNITY_MANAGED
        Coroutines.Step(CoroutinePhase.Update);
#endif
        for (int i = 0; i < behaviours.Count; i++)
        {
            MonoBehaviour b = behaviours[i];
            if (b.lateMark == ticks) continue;
            if (b.started && b.enabled && !b.destroyed && b.gameObject.activeInHierarchy) { b.lateMark = ticks; Send(b, MsgLateUpdate); }
        }
#if UNITY_MANAGED
        Coroutines.Step(CoroutinePhase.EndOfFrame);
        PlayerPrefs.Flush();
#endif
        Time.time += Time.deltaTime;
        Time.timeSinceLevelLoad += Time.deltaTime;
        Time.unscaledTime += Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : Time.deltaTime;
        Time.realtimeSinceStartup = Time.unscaledTime;
        Time.frameCount = Time.frameCount + 1;
        InputState.EndFrame();
        DestroyDue();
        Sweep();
        if (sceneToLoad >= 0) LoadPending();
    }

    public static void FixedUpdate()
    {
        fixedSteps += 1;
        for (int i = 0; i < behaviours.Count; i++)
        {
            MonoBehaviour b = behaviours[i];
            if (b.fixedMark == fixedSteps) continue;
            if (b.started && b.enabled && !b.destroyed && b.gameObject.activeInHierarchy) { b.fixedMark = fixedSteps; Send(b, MsgFixedUpdate); }
        }
#if UNITY_MANAGED
        Coroutines.Step(CoroutinePhase.FixedUpdate);
#endif
    }

    /// <summary>A GameObject became active or inactive in the hierarchy: its scripts and its children's get OnEnable (Awake first, if new) or OnDisable.</summary>
    public static void ActivityChanged(GameObject g, bool now)
    {
        foreach (Component c in g.components)
        {
            MonoBehaviour b = c as MonoBehaviour;
            if (b == null || b.destroyed) continue;
            if (now)
            {
                if (!b.awoken) Wake(b);
                else if (b.enabled) Send(b, MsgOnEnable);
            }
            else
            {
                if (b.awoken && b.enabled) Send(b, MsgOnDisable);
#if UNITY_MANAGED
                Coroutines.StopAll(b);          // deactivating an object stops its coroutines (disabling a script does not)
#endif
            }
        }
        Transform t = g.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            GameObject child = t.GetChild(i).gameObject;
            if (child.activeSelf) ActivityChanged(child, now);
        }
    }

    static void ForgetPhysics(Component c)
    {
        Rigidbody2D rb = c as Rigidbody2D;
        if (rb != null) Physics2D.RemoveBody(rb);
        Collider2D col = c as Collider2D;
        if (col != null) Physics2D.RemoveCollider(col);
        Joint2D jn = c as Joint2D;
        if (jn != null) Physics2D.RemoveJoint(jn);
    }

    /// <summary>Destroy takes effect at the end of the frame (as in Unity), and the slots go back to their arenas.  A GameObject takes its children with it.</summary>
    /// <summary>As Unity's: the scripts that go get OnDisable now (so a script that keeps a list in OnEnable / OnDisable sees it shrink at once);
    /// the object itself goes at the end of the frame, with OnDestroy.</summary>
    public static void Destroy(Object o)
    {
        if (o == null || o.destroyed) return;
        o.destroyed = true;
        GameObject g = o as GameObject;
        MonoBehaviour own = o as MonoBehaviour;
        if (own != null && own.awoken && own.enabled && own.gameObject.activeInHierarchy) Send(own, MsgOnDisable);
        if (g != null)
        {
            if (g.activeInHierarchy)
            {
                foreach (Component c in g.components)
                {
                    MonoBehaviour b = c as MonoBehaviour;
                    if (b != null && b.awoken && b.enabled && !b.destroyed) Send(b, MsgOnDisable);
                }
            }
            foreach (Component c in g.components) c.destroyed = true;
            Transform t = g.transform;
            for (int i = 0; i < t.childCount; i++) Destroy(t.GetChild(i).gameObject);
        }
        doomed.Add(o);
    }

    public static void DestroyAfter(Object o, float t)
    {
        if (o == null || o.destroyed) return;
        if (t <= 0f) { Destroy(o); return; }
        later.Add(o);
        laterAt.Add(Time.time + t);
    }

    static void DestroyDue()
    {
        for (int i = later.Count - 1; i >= 0; i--)
        {
            if (laterAt[i] <= Time.time)
            {
                Object o = later[i];
                later.RemoveAt(i);
                laterAt.RemoveAt(i);
                Destroy(o);
            }
        }
    }

    public static void KeepOnLoad(Object o)
    {
        GameObject g = o as GameObject;
        if (g == null)
        {
            Component c = o as Component;
            if (c != null) g = c.gameObject;
        }
        if (g != null) g.transform.root.gameObject.keepOnLoad = true;
    }

    /// <summary>Takes a GameObject out of the scene's lists without destroying it (a prefab's template is made like any GameObject, and is not part of the scene).</summary>
    /// <summary>Takes the object and everything below it out of the scene (a prefab's template): no lookup finds it, no script of it runs.</summary>
    public static void Forget(GameObject g)
    {
        for (int i = 0; i < g.transform.childCount; i++) Forget(g.transform.GetChild(i).gameObject);
        RemoveObject(g);
        foreach (Component c in g.components)
        {
            MonoBehaviour mb = c as MonoBehaviour;
            if (mb != null) RemoveBehaviour(mb);
            SpriteRenderer sr = c as SpriteRenderer;
            if (sr != null) RemoveSprite(sr);
            ForgetPhysics(c);
        }
    }

    static void RemoveObject(GameObject g)
    {
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i] == g) { objects.RemoveAt(i); return; }
        }
    }

    static void RemoveBehaviour(MonoBehaviour b)
    {
        for (int i = 0; i < behaviours.Count; i++)
        {
            if (behaviours[i] == b) { behaviours.RemoveAt(i); return; }
        }
    }

    static void RemoveSprite(SpriteRenderer s)
    {
        for (int i = 0; i < sprites.Count; i++)
        {
            if (sprites[i] == s) { sprites.RemoveAt(i); return; }
        }
    }

    public static void Sweep()
    {
        while (doomed.Count > 0)
        {
            Object o = doomed[doomed.Count - 1];
            doomed.RemoveAt(doomed.Count - 1);
            GameObject g = o as GameObject;
            if (g != null)
            {
                if (g.transform.parent != null) g.transform.SetParent(null, false);
                Forget(g);
                if (mainCamera != null && mainCamera.gameObject == g) mainCamera = null;
                foreach (Component c in g.components)
                {
                    MonoBehaviour b = c as MonoBehaviour;             // (its OnDisable was sent by Destroy)
#if UNITY_MANAGED
                    if (b != null) Coroutines.StopAll(b);
#endif
                    SendDestroy(c);
                    Arena.Free(c);
                }
                Arena.Free(g);
            }
            else
            {
                Component c = o as Component;
                if (c != null && c.gameObject != null)
                {
                    GameObject owner = c.gameObject;
                    for (int i = 0; i < owner.components.Count; i++)
                    {
                        if (owner.components[i] == c) { owner.components.RemoveAt(i); break; }
                    }
                }
                MonoBehaviour mb = o as MonoBehaviour;
                if (mb != null)
                {
#if UNITY_MANAGED
                    Coroutines.StopAll(mb);
#endif
                    RemoveBehaviour(mb);
                }
                SpriteRenderer sr = o as SpriteRenderer;
                if (sr != null) RemoveSprite(sr);
                if (c != null) ForgetPhysics(c);
                SendDestroy(o);
                Arena.Free(o);
            }
        }
    }

    /// <summary>LoadScene: at the end of the frame every root object that is not kept (DontDestroyOnLoad) goes, and the loader builds the scene.</summary>
    public static void LoadScene(int buildIndex) { sceneToLoad = buildIndex; }

    static void LoadPending()
    {
        int index = sceneToLoad;
        sceneToLoad = -1;
        for (int i = 0; i < objects.Count; i++)
        {
            GameObject g = objects[i];
            if (g.transform.parent == null && !g.keepOnLoad) Destroy(g);
        }
        Sweep();
        activeScene = index;
        Time.timeSinceLevelLoad = 0f;
        if (sceneLoader != null) sceneLoader.Load(index);
#if UNITY_MANAGED
        UnityEngine.SceneManagement.SceneManager.Loaded(index);
#endif
    }

    /// <summary>A root object's place among the scene's roots.</summary>
    public static int RootIndex(GameObject g)
    {
        int n = 0;
        for (int i = 0; i < objects.Count; i++)
        {
            GameObject o = objects[i];
            if (o == g) return n;
            if (o.transform.parent == null) n++;
        }
        return 0;
    }

    public static Transform TransformOf(Object o)
    {
        GameObject g = o as GameObject;
        if (g != null) return g.transform;
        Component c = o as Component;
        if (c != null) return c.transform;
        return null;
    }

    /// <summary>A copy of the original (a GameObject, or a component's GameObject) as its prefab makes it; for a component, the copy's component
    /// at the same place in the list.</summary>
    public static T Clone<T>(T original) where T : Object
    {
        if (original == null) return null;
        GameObject g = original as GameObject;
        Component c = original as Component;
        if (g == null && c != null) g = c.gameObject;
        if (g == null || g.prefab == null) return null;
        GameObject copy = g.prefab.Create();
        copy.prefab = g.prefab;
        WakeHierarchy(copy);                // as Unity's Instantiate: the copy's scripts have Awake and OnEnable before it returns
        if (c == null) return copy as T;
        for (int i = 0; i < g.components.Count; i++)
        {
            if (g.components[i] == c && i < copy.components.Count) return copy.components[i] as T;
        }
        return null;
    }

    public static T[] ToArray<T>(List<T> list)
#if UNITY_MANAGED
        where T : class
#else
        where T : Object
#endif
    {
        T[] a = new T[list.Count];
        for (int i = 0; i < list.Count; i++) a[i] = list[i];
        return a;
    }

    /// <summary>The live object of type T with the lowest instance ID (Unity's FindFirstObjectByType, which FindObjectOfType is).</summary>
    public static T FindFirst<T>(bool includeInactive) where T : Object
    {
        T best = null;
        int bestId = 0;
        for (int i = 0; i < objects.Count; i++)
        {
            GameObject g = objects[i];
            if (g.destroyed || (!includeInactive && !g.activeInHierarchy)) continue;
            T own = g as T;
            if (own != null && (best == null || g.instanceId < bestId)) { best = own; bestId = g.instanceId; }
            foreach (Component c in g.components)
            {
                T t = c as T;
                if (t != null && !c.destroyed && (best == null || c.instanceId < bestId)) { best = t; bestId = c.instanceId; }
            }
        }
        return best;
    }

    public static T[] FindAll<T>(bool includeInactive) where T : Object
    {
        List<T> found = new List<T>();
        for (int i = 0; i < objects.Count; i++)
        {
            GameObject g = objects[i];
            if (g.destroyed || (!includeInactive && !g.activeInHierarchy)) continue;
            T own = g as T;
            if (own != null) found.Add(own);
            foreach (Component c in g.components)
            {
                T t = c as T;
                if (t != null && !c.destroyed) found.Add(t);
            }
        }
        return ToArray<T>(found);
    }

    public static GameObject Find(string name)
    {
        int slash = name.IndexOf("/");
        string first = name;
        if (slash >= 0) first = name.Substring(0, slash);
        foreach (GameObject o in objects)
        {
            if (o.destroyed || !o.activeInHierarchy || o.name != first) continue;
            if (slash < 0) return o;
            Transform t = o.transform.Find(name.Substring(slash + 1));
            if (t != null) return t.gameObject;
        }
        return null;
    }

    public static GameObject FindWithTag(string tag)
    {
        foreach (GameObject o in objects)
        {
            if (!o.destroyed && o.activeInHierarchy && o.tag == tag) return o;
        }
        return null;
    }

    public static GameObject[] FindAllWithTag(string tag)
    {
        List<GameObject> found = new List<GameObject>();
        foreach (GameObject o in objects)
        {
            if (!o.destroyed && o.activeInHierarchy && o.tag == tag) found.Add(o);
        }
        return ToArray<GameObject>(found);
    }

    // ---- for the host (a window that draws the frame): a snapshot of what is visible, then flat reads of it ----
    static List<SpriteRenderer> drawList = new List<SpriteRenderer>();

    /// <summary>Fills the draw list: the sprites that draw this frame, back to front (sorting order, then z).  Returns how many.</summary>
    public static int HostCollect()
    {
        drawList.Clear();
        foreach (SpriteRenderer s in sprites)
        {
            if (s.enabled && s.gameObject.activeInHierarchy) drawList.Add(s);
        }
        for (int i = 1; i < drawList.Count; i++)
        {
            SpriteRenderer v = drawList[i];
            int j = i - 1;
            while (j >= 0 && Before(v, drawList[j]))
            {
                drawList[j + 1] = drawList[j];
                j--;
            }
            drawList[j + 1] = v;
        }
        return drawList.Count;
    }

    static bool Before(SpriteRenderer a, SpriteRenderer b)
    {
        if (a.sortingLayer != b.sortingLayer) return a.sortingLayer < b.sortingLayer;
        if (a.sortingOrder != b.sortingOrder) return a.sortingOrder < b.sortingOrder;
        return a.transform.position.z > b.transform.position.z;
    }

    /// <summary>Field k of draw i: 0 x, 1 y, 2 half width, 3 half height (scaled), 4 r, 5 g, 6 b, 7 a, 8 z, 9..12 m00 m01 m10 m11.</summary>
    public static float HostDrawF(int i, int k)
    {
        SpriteRenderer s = drawList[i];
        Vector3 p = s.transform.position;
        if (k == 0) return p.x;
        if (k == 1) return p.y;
        if (k == 2) return s.halfW * MathF.Abs(s.transform.lossyScale.x);
        if (k == 3) return s.halfH * MathF.Abs(s.transform.lossyScale.y);
        if (k == 4) return s.color.r;
        if (k == 5) return s.color.g;
        if (k == 6) return s.color.b;
        if (k == 7) return s.color.a;
        if (k >= 9) {
            Quaternion q = s.transform.rotation;      // local XY -> world XY (the upper-left 2x2 of the rotation)
            if (k == 9) return 1f - 2f * (q.y * q.y + q.z * q.z);
            if (k == 10) return 2f * (q.x * q.y - q.z * q.w);
            if (k == 11) return 2f * (q.x * q.y + q.z * q.w);
            return 1f - 2f * (q.x * q.x + q.z * q.z);
        }
        return p.z;
    }

    /// <summary>Field k of draw i: 0 texture, 1 sorting order, 2 sorting layer, 3 the GameObject's layer.</summary>
    public static int HostDrawI(int i, int k)
    {
        SpriteRenderer s = drawList[i];
        if (k == 0) return s.spriteId;
        if (k == 1) return s.sortingOrder;
        if (k == 2) return s.sortingLayer;
        return s.gameObject.layer;
    }

    /// <summary>Field k of the main camera: 0 x, 1 y, 2 half height, 3 r, 4 g, 5 b.  False when there is no camera.</summary>
    public static float HostCameraF(int k)
    {
        Vector3 p = mainCamera.transform.position;
        if (k == 0) return p.x;
        if (k == 1) return p.y;
        if (k == 2) return mainCamera.orthographicSize;
        if (k == 3) return mainCamera.background.r;
        if (k == 4) return mainCamera.background.g;
        return mainCamera.background.b;
    }

    public static int HostHasCamera() { return mainCamera == null ? 0 : 1; }

    /// <summary>The window's size, for Screen (and the camera's aspect).</summary>
    public static void HostSetScreen(int w, int h)
    {
        Screen.width = w;
        Screen.height = h;
    }

    public static int HostWantsQuit() { return quitRequested ? 1 : 0; }

    /// <summary>The frame's real time (Time.unscaledDeltaTime), from the host: a method, so a host can call it on a managed engine too.</summary>
    public static void HostSetDeltaTime(float dt) { Time.unscaledDeltaTime = dt; }

    /// <summary>How many sprites the frame draws.</summary>
    public static int CountDraws()
    {
        int n = 0;
        foreach (SpriteRenderer s in sprites)
        {
            if (s.enabled && s.gameObject.activeInHierarchy) n++;
        }
        return n;
    }
}
