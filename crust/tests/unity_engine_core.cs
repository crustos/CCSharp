// unity-engine
// The engine's lifecycle and hierarchy: Awake, OnEnable, Start in order (an inactive object's script waits to be activated), OnDisable,
// SetParent keeping the world or the local position, activity through parents, Find by path, FindObjectsOfType, a prefab copy of a component,
// and Destroy after a delay.
using System;
using UnityEngine;

public class Probe : MonoBehaviour
{
    public int id;
    // the events, two digits each: the script's id, then 1 Awake, 2 OnEnable, 3 Start, 4 OnDisable (a number, not a string: Crust lowers
    // `log = log + ..` on a static string wrongly from a virtual method)
    public static long log;

    public override void Awake() { log = log * 100 + id * 10 + 1; }
    public override void OnEnable() { log = log * 100 + id * 10 + 2; }
    public override void Start() { log = log * 100 + id * 10 + 3; }
    public override void OnDisable() { log = log * 100 + id * 10 + 4; }
}

public class Bullet : MonoBehaviour { }

public class BulletPrefab : Prefab
{
    public override GameObject Create()
    {
        GameObject g = new GameObject("Bullet");
        g.Attach(new Bullet());
        return g;
    }
}

class Program
{
    static Probe Make(string name, int id)
    {
        GameObject g = new GameObject(name);
        Probe p = new Probe();
        p.id = id;
        g.Attach(p);
        return p;
    }

    static int Main()
    {
        Time.deltaTime = 0.5f;
        Probe.log = 0;
        Probe a = Make("A", 1);
        Probe b = Make("B", 2);
        b.gameObject.SetActive(false);
        Engine.Tick();
        Console.WriteLine(Probe.log);                     // 111213: A1 E1 S1 (B is inactive)
        Probe.log = 0;
        b.gameObject.SetActive(true);
        Engine.Tick();
        Console.WriteLine(Probe.log);                     // 212223: A2 E2 S2
        Probe.log = 0;
        a.enabled = false;
        a.enabled = true;
        a.gameObject.SetActive(false);
        Console.WriteLine(Probe.log);                     // 141214: D1 E1 D1

        GameObject parent = new GameObject("Parent");
        parent.transform.position = new Vector3(10f, 0f, 0f);
        GameObject child = new GameObject("Child");
        child.transform.position = new Vector3(12f, 0f, 0f);
        child.transform.SetParent(parent.transform);
        Console.WriteLine((int)child.transform.localPosition.x + " " + (int)child.transform.position.x);      // 2 12
        GameObject other = new GameObject("Other");
        other.transform.localPosition = new Vector3(3f, 0f, 0f);
        other.transform.SetParent(parent.transform, false);
        Console.WriteLine((int)other.transform.position.x + " " + other.transform.GetSiblingIndex() + " " + parent.transform.childCount);   // 13 1 2

        parent.SetActive(false);
        Console.WriteLine((child.activeSelf ? 1 : 0) + " " + (child.activeInHierarchy ? 1 : 0));            // 1 0
        Console.WriteLine(GameObject.Find("Parent/Child") == null ? 1 : 0);                               // 1 (hidden under an inactive parent)
        parent.SetActive(true);
        Console.WriteLine(GameObject.Find("Parent/Child") == child ? 1 : 0);                              // 1
        Probe[] active = GameObject.FindObjectsOfType<Probe>();
        Probe[] all = GameObject.FindObjectsOfType<Probe>(true);
        Console.WriteLine(active.Length + " " + all.Length);                                                  // 1 2 (A is inactive)

        GameObject template = new GameObject("BulletTemplate");
        template.prefab = new BulletPrefab();
        Bullet original = template.AddComponent<Bullet>();
        Bullet copy = UnityEngine.Object.Instantiate<Bullet>(original, new Vector3(5f, 0f, 0f), Quaternion.identity);
        Console.WriteLine(copy.gameObject.name + " " + (int)copy.transform.position.x + " " + (copy != original ? 1 : 0));   // Bullet 5 1

        UnityEngine.Object.Destroy(copy.gameObject, 1f);
        Engine.Tick();
        Console.WriteLine(copy.gameObject.destroyed ? 1 : 0);                                                   // 0 (half a second)
        Engine.Tick();
        Engine.Tick();
        Console.WriteLine(GameObject.Find("Bullet") == null ? 1 : 0);                                           // 1
        return 0;
    }
}
