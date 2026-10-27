// unity-engine
// expect-rc: 0
// Trigger messages with several pairs on one collider: Enter on the first step, Stay while they overlap, Exit for exactly the pairs that end.
// The .NET reference runs the same engine, so the test checks its own counts (rc 1 when they are wrong).
using System;
using UnityEngine;

public class Counter : MonoBehaviour
{
    public int enters;
    public int stays;
    public int exits;

    public override void OnTriggerEnter2D(Collider2D other) { enters++; }
    public override void OnTriggerStay2D(Collider2D other) { stays++; }
    public override void OnTriggerExit2D(Collider2D other) { exits++; }

    public int Code() { return enters * 100 + stays * 10 + exits; }
}

class Program
{
    static Counter Make(string name, float x, float size, bool trigger, bool body)
    {
        GameObject g = new GameObject(name);
        g.transform.position = new Vector3(x, 0f, 0f);
        if (body)
        {
            Rigidbody2D r = new Rigidbody2D();
            r.bodyType = RigidbodyType2D.Kinematic;
            r.gravityScale = 0f;
            g.Attach(r);
        }
        BoxCollider2D c = new BoxCollider2D();
        c.sizeX = size;
        c.sizeY = size;
        c.isTrigger = trigger;
        g.Attach(c);
        Counter k = new Counter();
        g.Attach(k);
        return k;
    }

    static int Main()
    {
        Time.deltaTime = 0.02f;
        Counter zone = Make("Zone", 0f, 4f, true, false);        // overlaps both bodies
        Counter zone2 = Make("Zone2", 1f, 1f, true, false);      // overlaps B only
        Counter a = Make("A", -1f, 1f, false, true);
        Counter b = Make("B", 1f, 1f, false, true);
        Engine.Tick();                                           // Enter: Zone-A, Zone-B, Zone2-B
        Engine.Tick();                                           // Stay for all three
        Engine.Tick();
        b.transform.position = new Vector3(10f, 0f, 0f);
        Engine.Tick();                                           // Exit: Zone-B, Zone2-B; Stay: Zone-A
        Console.WriteLine(zone.Code() + " " + zone2.Code() + " " + a.Code() + " " + b.Code());   // 251 121 130 242
        return zone.Code() == 251 && zone2.Code() == 121 && a.Code() == 130 && b.Code() == 242 ? 0 : 1;
    }
}
