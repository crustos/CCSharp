// oracle: dotnet
// unity-engine
// Script Execution Order: scripts run Awake, Start and Update lowest order first (attach order within one order), and a script attached during an
// Update, ahead of the one running, does not make any script update twice in that frame.
using System;
using UnityEngine;

class Log
{
    public static string text;
}

class Named : MonoBehaviour
{
    public int tag;
    public override void Awake() { Log.text = Log.text + "a" + tag; }
    public override void Update() { Log.text = Log.text + "u" + tag; }
}

class Spawner : MonoBehaviour
{
    public bool spawned;
    public override void Update()
    {
        Log.text = Log.text + "uS";
        if (spawned) return;
        spawned = true;
        GameObject g = new GameObject("late");
        Named n = new Named();
        n.tag = 9;
        n.executionOrder = -100;
        g.Attach(n);
    }
}

class Program
{
    static Named Add(GameObject g, int tag, int order)
    {
        Named n = new Named();
        n.tag = tag;
        n.executionOrder = order;
        g.Attach(n);
        return n;
    }

    static int Main()
    {
        GameObject g = new GameObject("g");
        Add(g, 1, 5);
        Add(g, 2, -5);
        Add(g, 3, 0);
        Add(g, 4, -5);
        Spawner s = new Spawner();
        s.executionOrder = 10;
        g.Attach(s);
        Time.deltaTime = 0.02f;
        Engine.Tick();
        Console.WriteLine(Log.text);      // a2a4a3a1u2u4u3u1uS: the late one (9) joins ahead, and nothing updates twice
        Log.text = "";
        Engine.Tick();
        Console.WriteLine(Log.text);      // a9u9u2u4u3u1uS
        return 0;
    }
}
