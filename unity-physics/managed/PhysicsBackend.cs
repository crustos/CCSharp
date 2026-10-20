// CCSharp --unity-engine: the 2D physics backend written in C#.  It runs everywhere the engine does (.NET, DNA, the lowered C) with the same
// results; `--physics=box2d` swaps it for Box2D-Packed (unity-physics/box2d).
//
// Axis aligned boxes and circles, no rotation; see Physics2D.cs.

using System;
using System.Collections.Generic;

namespace UnityEngine;

public static class PhysicsBackend
{
    // the overlap Test found: a normal from a to b, and how deep
    static float nx;
    static float ny;
    static float depth;

    public static void ReleaseBody(Rigidbody2D r) { }
    public static void ReleaseCollider(Collider2D c) { }
    public static void ReleaseJoint(Joint2D j) { }               // (joints need the Box2D backend)

    public static void Step(float dt)
    {
        for (int i = 0; i < Physics2D.colliders.Count; i++)
        {
            Collider2D c = Physics2D.colliders[i];
            c.body = c.gameObject.GetComponent<Rigidbody2D>();
        }
        for (int i = 0; i < Physics2D.bodies.Count; i++)
        {
            Rigidbody2D r = Physics2D.bodies[i];
            if (r.destroyed || !r.gameObject.activeInHierarchy || !r.simulated) continue;
            float vx = r.velocity.x;
            float vy = r.velocity.y;
            float fx;
            float fy;
            r.TakeForce(out fx, out fy);
            if (r.bodyType == RigidbodyType2D.Dynamic)
            {
                vx = vx + (Physics2D.gravityX * r.gravityScale + fx / r.mass) * dt;
                vy = vy + (Physics2D.gravityY * r.gravityScale + fy / r.mass) * dt;
                float damp = 1f / (1f + r.drag * dt);
                vx = vx * damp;
                vy = vy * damp;
                r.velocity = new Vector2(vx, vy);
            }
            if (r.bodyType != RigidbodyType2D.Static)
            {
                Vector3 p = r.transform.position;
                r.transform.position = new Vector3(p.x + vx * dt, p.y + vy * dt, p.z);
            }
        }

        Physics2D.curA.Clear();
        Physics2D.curB.Clear();
        Physics2D.curT.Clear();
        for (int iter = 0; iter < 4; iter++)
        {
            for (int i = 0; i < Physics2D.colliders.Count; i++)
            {
                Collider2D a = Physics2D.colliders[i];
                if (!Physics2D.Active(a)) continue;
                for (int j = i + 1; j < Physics2D.colliders.Count; j++)
                {
                    Collider2D b = Physics2D.colliders[j];
                    if (!Physics2D.Active(b)) continue;
                    if (a.gameObject == b.gameObject) continue;
                    bool aMoves = a.body != null && a.body.bodyType != RigidbodyType2D.Static;
                    bool bMoves = b.body != null && b.body.bodyType != RigidbodyType2D.Static;
                    if (!aMoves && !bMoves) continue;               // two static Physics2D.colliders never meet
                    if (!Physics2D.Collides(a, b) || !Test(a, b)) continue;
                    bool trigger = a.isTrigger || b.isTrigger;
                    if (iter == 0)
                    {
                        Physics2D.curA.Add(a);
                        Physics2D.curB.Add(b);
                        Physics2D.curT.Add(trigger ? 1 : 0);
                    }
                    if (!trigger) Resolve(a, b);
                }
            }
        }
    }

    // ---- narrow phase: boxes and circles, axis aligned

    static float CenterX(Collider2D c)
    {
        Vector3 p = c.transform.position;
        Vector3 s = c.transform.lossyScale;
        return p.x + c.offset.x * s.x;
    }

    static float CenterY(Collider2D c)
    {
        Vector3 p = c.transform.position;
        Vector3 s = c.transform.lossyScale;
        return p.y + c.offset.y * s.y;
    }

    static float HalfX(Collider2D c)
    {
        Vector3 s = c.transform.lossyScale;
        return 0.5f * c.sizeX * MathF.Abs(s.x);
    }

    static float HalfY(Collider2D c)
    {
        Vector3 s = c.transform.lossyScale;
        return 0.5f * c.sizeY * MathF.Abs(s.y);
    }

    static float Radius(Collider2D c)
    {
        Vector3 s = c.transform.lossyScale;
        float k = MathF.Max(MathF.Abs(s.x), MathF.Abs(s.y));
        return c.radius * k;
    }

    /// <summary>PhysicsMaterialCombine: Average 0, Multiply 1, Minimum 2, Maximum 3; the higher mode wins.</summary>
    static float Combine(float x, float y, int ca, int cb)
    {
        int mode = ca > cb ? ca : cb;
        if (mode == 1) return x * y;
        if (mode == 2) return MathF.Min(x, y);
        if (mode == 3) return MathF.Max(x, y);
        return 0.5f * (x + y);
    }

    static float Clamp(float v, float lo, float hi)
    {
        if (v < lo) return lo;
        if (v > hi) return hi;
        return v;
    }

    /// <summary>Do a and b overlap?  Sets nx, ny (a unit normal from a to b) and depth.</summary>
    static bool Test(Collider2D a, Collider2D b)
    {
        if (a.kind != 1 && b.kind != 1) return BoxBox(a, b);       // (a capsule and a polygon are boxes here: their bounds)
        if (a.kind == 1 && b.kind == 1) return CircleCircle(a, b);
        if (a.kind == 1) return CircleBox(a, b, false);
        bool hit = CircleBox(b, a, true);
        return hit;
    }

    static bool BoxBox(Collider2D a, Collider2D b)
    {
        float dx = CenterX(b) - CenterX(a);
        float dy = CenterY(b) - CenterY(a);
        float px = HalfX(a) + HalfX(b) - MathF.Abs(dx);
        float py = HalfY(a) + HalfY(b) - MathF.Abs(dy);
        if (px <= 0f || py <= 0f) return false;
        if (px < py)
        {
            nx = dx < 0f ? -1f : 1f;
            ny = 0f;
            depth = px;
        }
        else
        {
            nx = 0f;
            ny = dy < 0f ? -1f : 1f;
            depth = py;
        }
        return true;
    }

    static bool CircleCircle(Collider2D a, Collider2D b)
    {
        float dx = CenterX(b) - CenterX(a);
        float dy = CenterY(b) - CenterY(a);
        float r = Radius(a) + Radius(b);
        float d2 = dx * dx + dy * dy;
        if (d2 >= r * r) return false;
        float d = MathF.Sqrt(d2);
        if (d > 0.00001f)
        {
            nx = dx / d;
            ny = dy / d;
        }
        else
        {
            nx = 0f;
            ny = 1f;
        }
        depth = r - d;
        return true;
    }

    /// <summary>A circle against a box.  With `flip` the pair is (box, circle) and the normal is turned to run from the box to the circle.</summary>
    static bool CircleBox(Collider2D circle, Collider2D box, bool flip)
    {
        float cx = CenterX(circle);
        float cy = CenterY(circle);
        float bx = CenterX(box);
        float by = CenterY(box);
        float hx = HalfX(box);
        float hy = HalfY(box);
        float r = Radius(circle);
        float qx = Clamp(cx - bx, -hx, hx);
        float qy = Clamp(cy - by, -hy, hy);
        float ex = cx - bx - qx;       // from the nearest point of the box to the circle's centre
        float ey = cy - by - qy;
        float d2 = ex * ex + ey * ey;
        float tx;
        float ty;
        float dep;
        if (d2 > 0.0000001f)
        {
            if (d2 >= r * r) return false;
            float d = MathF.Sqrt(d2);
            tx = ex / d;
            ty = ey / d;
            dep = r - d;
        }
        else
        {
            // the centre is inside the box: out through the nearest face
            float left = cx - bx + hx;
            float right = hx - (cx - bx);
            float down = cy - by + hy;
            float up = hy - (cy - by);
            float m = MathF.Min(MathF.Min(left, right), MathF.Min(down, up));
            tx = 0f;
            ty = 0f;
            if (m == left) tx = -1f;
            else if (m == right) tx = 1f;
            else if (m == down) ty = -1f;
            else ty = 1f;
            dep = m + r;
        }
        // (tx, ty) runs from the box to the circle
        if (flip)
        {
            // the pair is (box, circle) = (a, b): from a to b
            nx = tx;
            ny = ty;
        }
        else
        {
            nx = -tx;
            ny = -ty;
        }
        depth = dep;
        return true;
    }

    // ---- response

    static float InvMass(Collider2D c)
    {
        if (c.body == null) return 0f;
        if (c.body.bodyType != RigidbodyType2D.Dynamic) return 0f;
        return 1f / c.body.mass;
    }

    static void Resolve(Collider2D a, Collider2D b)
    {
        float ia = InvMass(a);
        float ib = InvMass(b);
        float sum = ia + ib;
        float nnx = nx;
        float nny = ny;
        float dep = depth;
        // push apart
        float slop = 0.005f;
        float corr = MathF.Max(dep - slop, 0f) * 0.8f;
        if (corr > 0f)
        {
            if (ia > 0f)
            {
                Vector3 p = a.transform.position;
                float k = corr * ia / (sum > 0f ? sum : 1f);
                a.transform.position = new Vector3(p.x - nnx * k, p.y - nny * k, p.z);
            }
            if (ib > 0f)
            {
                Vector3 p = b.transform.position;
                float k = corr * ib / (sum > 0f ? sum : 1f);
                b.transform.position = new Vector3(p.x + nnx * k, p.y + nny * k, p.z);
            }
        }
        if (sum <= 0f) return;
        float avx = 0f;
        float avy = 0f;
        float bvx = 0f;
        float bvy = 0f;
        if (a.body != null) { avx = a.body.velocity.x; avy = a.body.velocity.y; }
        if (b.body != null) { bvx = b.body.velocity.x; bvy = b.body.velocity.y; }
        float rvx = bvx - avx;
        float rvy = bvy - avy;
        float vn = rvx * nnx + rvy * nny;
        if (vn > 0f) return;                         // already separating
        float e = Combine(a.bounciness, b.bounciness, a.bounceCombine, b.bounceCombine);
        if (MathF.Abs(vn) < 0.5f) e = 0f;            // resting contact does not jitter
        float j = -(1f + e) * vn / sum;
        float ix = j * nnx;
        float iy = j * nny;
        // friction: along the tangent, at most mu times the normal impulse
        float tx = rvx - vn * nnx;
        float ty = rvy - vn * nny;
        float tl = MathF.Sqrt(tx * tx + ty * ty);
        float fjx = 0f;
        float fjy = 0f;
        if (tl > 0.00001f)
        {
            tx = tx / tl;
            ty = ty / tl;
            float jt = -(rvx * tx + rvy * ty) / sum;
            float mu = Combine(a.friction, b.friction, a.frictionCombine, b.frictionCombine);
            float lim = mu * j;
            if (jt > lim) jt = lim;
            if (jt < -lim) jt = -lim;
            fjx = jt * tx;
            fjy = jt * ty;
        }
        if (ia > 0f)
        {
            a.body.velocity = new Vector2(avx - (ix + fjx) * ia, avy - (iy + fjy) * ia);
        }
        if (ib > 0f)
        {
            b.body.velocity = new Vector2(bvx + (ix + fjx) * ib, bvy + (iy + fjy) * ib);
        }
    }

}
