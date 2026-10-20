// dna
// unity-engine
// Physics2D's queries with Unity's meanings, on a small world: rays (an origin inside a collider meets it at 0, triggers as queriesHitTriggers says,
// a rotated box, a concave polygon cut into triangles, an edge), a linecast, box, circle and collider casts, overlaps, touching, a contact's normal,
// ignored pairs, the layer matrix, and a body that is not simulated.
using System;
using System.Collections.Generic;
using UnityEngine;

class Program
{
    static string V(Vector2 v) { return (int)Math.Round(v.x * 100) + "," + (int)Math.Round(v.y * 100); }
    static string H(RaycastHit2D h) { return h ? h.collider.name + " " + (int)Math.Round(h.distance * 100) + " " + V(h.point) + " " + V(h.normal) : "none"; }

    static T Make<T>(string name, float x, float y) where T : Collider2D, new()
    {
        GameObject g = new GameObject(name);
        g.transform.position = new Vector3(x, y, 0f);
        return g.AddComponent<T>();
    }

    static int Main()
    {
        BoxCollider2D ground = Make<BoxCollider2D>("Ground", 0f, 0f);
        ground.size = new Vector2(10f, 1f);
        BoxCollider2D wall = Make<BoxCollider2D>("Wall", 3f, 2f);
        wall.size = new Vector2(1f, 4f);
        CircleCollider2D ball = Make<CircleCollider2D>("Ball", 0f, 3f);
        ball.gameObject.layer = 8;
        BoxCollider2D trigger = Make<BoxCollider2D>("Trigger", -3f, 2f);
        trigger.isTrigger = true;
        BoxCollider2D diamond = Make<BoxCollider2D>("Diamond", -3f, 5f);
        diamond.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
        PolygonCollider2D ell = Make<PolygonCollider2D>("Ell", 6f, 3f);
        ell.points = new Vector2[] { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f), new Vector2(1f, 1f), new Vector2(1f, 2f), new Vector2(0f, 2f) };
        EdgeCollider2D edge = Make<EdgeCollider2D>("Edge", 0f, 8f);
        edge.points = new Vector2[] { new Vector2(-2f, 0f), new Vector2(2f, 0f) };
        int notBall = ~(1 << 8);

        Console.WriteLine(H(Physics2D.Raycast(new Vector2(0f, 3f), Vector2.down, 10f, notBall)));         // Ground 250 0,50 0,100
        Console.WriteLine(H(Physics2D.Raycast(new Vector2(0f, 3f), Vector2.right, 10f, notBall)));        // Wall 250 250,300 -100,0
        Console.WriteLine(H(Physics2D.Raycast(new Vector2(0f, 3f), Vector2.right)));                      // Ball 0 0,300 -100,0
        Console.WriteLine(H(Physics2D.Raycast(new Vector2(-6f, 2f), Vector2.right, 20f, notBall)));       // Trigger 250 -350,200 -100,0
        Physics2D.queriesHitTriggers = false;
        Console.WriteLine(H(Physics2D.Raycast(new Vector2(-6f, 2f), Vector2.right, 20f, notBall)));       // Wall 850 250,200 -100,0
        Console.WriteLine(H(Physics2D.Raycast(new Vector2(-2.9f, 8f), Vector2.down, 10f)));               // Diamond 239 -290,561 71,71
        Console.WriteLine(H(Physics2D.Raycast(new Vector2(7.5f, 10f), Vector2.down, 10f)));               // Ell 600 750,400 0,100 (the notch is empty)
        Console.WriteLine(H(Physics2D.Raycast(new Vector2(0f, 10f), Vector2.down, 1.5f)));                // none (too short)
        Console.WriteLine(H(Physics2D.Raycast(new Vector2(0f, 10f), Vector2.down, 5f)));                  // Edge 200 0,800 0,100
        Console.WriteLine(H(Physics2D.Linecast(new Vector2(-10f, 0f), new Vector2(10f, 0f))));            // Ground 500 -500,0 -100,0
        Console.WriteLine(Physics2D.RaycastAll(new Vector2(-10f, 2f), Vector2.right, 30f).Length);        // 1 (the wall: the trigger is skipped)

        RaycastHit2D box = Physics2D.BoxCast(new Vector2(0f, 2f), new Vector2(1f, 1f), 0f, Vector2.down, 5f, notBall);
        Console.WriteLine(H(box).Split(' ')[0] + " " + (int)Math.Round(box.distance * 100) + " " + V(box.centroid) + " " + V(box.normal));   // Ground 100 0,100 0,100
        RaycastHit2D circle = Physics2D.CircleCast(new Vector2(-6f, 2f), 0.25f, Vector2.right, 20f, notBall);
        Console.WriteLine(circle.collider.name + " " + (int)Math.Round(circle.distance * 100));          // Wall 825
        RaycastHit2D[] casts = new RaycastHit2D[4];
        int n = ball.Cast(Vector2.down, new ContactFilter2D(), casts, 10f);
        Console.WriteLine(n + " " + casts[0].collider.name + " " + (int)Math.Round(casts[0].distance * 100));   // 1 Ground 200

        Console.WriteLine(Physics2D.OverlapCircle(new Vector2(3f, 2f), 0.1f).name);                      // Wall
        Console.WriteLine(Physics2D.OverlapPoint(new Vector2(7.5f, 4.5f)) == null ? "notch" : "filled");  // notch
        Console.WriteLine(Physics2D.OverlapPoint(new Vector2(6.5f, 4.5f)).name);                          // Ell
        Physics2D.queriesHitTriggers = true;
        Console.WriteLine(Physics2D.OverlapBoxAll(Vector2.zero, new Vector2(20f, 20f), 0f).Length);       // 7
        Console.WriteLine(V(ball.bounds.center) + " " + V(ball.bounds.size) + " " + V(diamond.bounds.size));   // 0,300 100,100 141,141
        Console.WriteLine(ell.points.Length + " " + ell.pathCount + " " + ell.GetTotalPointCount() + " " + V(ell.GetPath(0)[3]));   // 6 1 6 100,100

        BoxCollider2D crate = Make<BoxCollider2D>("Crate", 0f, 1f);                                      // resting on the ground
        Collider2D[] found = new Collider2D[4];
        Console.WriteLine((crate.IsTouching(ground) ? 1 : 0) + " " + (crate.IsTouchingLayers(1) ? 1 : 0) + " " + crate.Overlap(new ContactFilter2D().NoFilter(), found));   // 1 1 0
        Collision2D hit = new Collision2D();
        hit.collider = ground;
        hit.otherCollider = crate;
        ContactPoint2D cp = hit.GetContact(0);
        Console.WriteLine(V(cp.normal) + " " + V(cp.point) + " " + (int)Math.Round(cp.separation * 100) + " " + cp.collider.name);   // 0,100 -50,50 0 Ground
        Physics2D.IgnoreCollision(crate, ground);
        Console.WriteLine((Physics2D.GetIgnoreCollision(ground, crate) ? 1 : 0) + " " + (crate.IsTouching(ground) ? 1 : 0));   // 1 0
        Physics2D.IgnoreCollision(crate, ground, false);
        Physics2D.SetLayerCollisionMask(8, ~1);
        Console.WriteLine(Physics2D.GetLayerCollisionMask(8) + " " + (Physics2D.GetIgnoreLayerCollision(8, 0) ? 1 : 0) + " " + Physics2D.GetLayerCollisionMask(3));   // -2 1 -1

        Rigidbody2D rb = crate.gameObject.AddComponent<Rigidbody2D>();
        rb.position = new Vector2(1f, 1f);
        Console.WriteLine(H(Physics2D.Raycast(new Vector2(1f, 5f), Vector2.down, 10f)));                  // Crate 350 100,150 0,100
        rb.simulated = false;
        Console.WriteLine(H(Physics2D.Raycast(new Vector2(1f, 5f), Vector2.down, 10f)));                  // Ground 450 100,50 0,100
        return 0;
    }
}
