// CCSharp --unity-engine --physics=box2d: the 2D physics backend on Box2D-Packed.
//
// The C# keeps the Unity-shaped side (components, scripts, messages); Box2D-Packed steps the bodies.  Everything crosses pb2.h as ints and floats, so
// Box2D's bit-packed definition structs stay on the C side.  Each step the backend creates the bodies and shapes that are new, pushes what scripts
// changed (position, velocity, mass, body type, gravity scale, drag, forces), steps, and pulls the positions and velocities back.
//
// Boxes and circles only, with no rotation: the same as the C# backend (unity-physics/managed), which this must agree with in what a game sees.

using System;
using System.Collections.Generic;
using Crust;

namespace UnityEngine;

[CppInclude("\"pb2.h\"")]
public static class PB2
{
    [Cpp("pb2_world({0}, {1})")] public static extern void World(float gx, float gy);
    [Cpp("pb2_set_gravity({0}, {1})")] public static extern void SetGravity(float gx, float gy);
    [Cpp("pb2_step({0})")] public static extern void Step(float dt);
    [Cpp("pb2_body({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})")] public static extern int Body(int type, float x, float y, float angle, float vx, float vy, float gravityScale, float damping, int freeze);
    [Cpp("pb2_destroy_body({0})")] public static extern void DestroyBody(int body);
    [Cpp("pb2_box({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})")] public static extern int Box(int body, int collider, float halfW, float halfH, float ox, float oy, int trigger, float friction, float bounce, int material);
    [Cpp("pb2_circle({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})")] public static extern int Circle(int body, int collider, float radius, float ox, float oy, int trigger, float friction, float bounce, int material);
    [Cpp("pb2_capsule({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10})")] public static extern int Capsule(int body, int collider, float radius, float x1, float y1, float x2, float y2, int trigger, float friction, float bounce, int material);
    [Cpp("pb2_poly_point({0}, {1})")] public static extern void PolyPoint(float x, float y);
    [Cpp("pb2_poly_piece({0}, {1}, {2}, {3}, {4}, {5})")] public static extern int PolyPiece(int body, int collider, int trigger, float friction, float bounce, int material);
    [Cpp("pb2_destroy_collider({0})")] public static extern void DestroyCollider(int collider);
    [Cpp("pb2_angle({0})")] public static extern float Angle(int body);
    [Cpp("pb2_set_angle({0}, {1})")] public static extern void SetAngle(int body, float radians);
    [Cpp("pb2_angvel({0})")] public static extern float AngVel(int body);
    [Cpp("pb2_set_angvel({0}, {1})")] public static extern void SetAngVel(int body, float radiansPerSecond);
    [Cpp("pb2_torque({0}, {1})")] public static extern void Torque(int body, float t);
    [Cpp("pb2_angular_impulse({0}, {1})")] public static extern void AngularImpulse(int body, float i);
    [Cpp("pb2_set_freeze({0}, {1})")] public static extern void SetFreeze(int body, int freeze);
    [Cpp("pb2_x({0})")] public static extern float X(int body);
    [Cpp("pb2_y({0})")] public static extern float Y(int body);
    [Cpp("pb2_vx({0})")] public static extern float Vx(int body);
    [Cpp("pb2_vy({0})")] public static extern float Vy(int body);
    [Cpp("pb2_set_pos({0}, {1}, {2})")] public static extern void SetPos(int body, float x, float y);
    [Cpp("pb2_set_velocity({0}, {1}, {2})")] public static extern void SetVelocity(int body, float vx, float vy);
    [Cpp("pb2_force({0}, {1}, {2})")] public static extern void Force(int body, float fx, float fy);
    [Cpp("pb2_set_type({0}, {1})")] public static extern void SetType(int body, int type);
    [Cpp("pb2_set_mass({0}, {1})")] public static extern void SetMass(int body, float mass);
    [Cpp("pb2_set_gravity_scale({0}, {1})")] public static extern void SetGravityScale(int body, float scale);
    [Cpp("pb2_set_damping({0}, {1})")] public static extern void SetDamping(int body, float damping);
    [Cpp("pb2_set_enabled({0}, {1})")] public static extern void SetEnabled(int body, int enabled);
    [Cpp("pb2_jparam({0}, {1})")] public static extern void JParam(int index, float value);
    [Cpp("pb2_joint({0}, {1}, {2})")] public static extern int Joint(int kind, int own, int other);
    [Cpp("pb2_destroy_joint({0})")] public static extern void DestroyJoint(int joint);
    [Cpp("pb2_event_count()")] public static extern int EventCount();
    [Cpp("pb2_event_kind({0})")] public static extern int EventKind(int i);
    [Cpp("pb2_event_a({0})")] public static extern int EventA(int i);
    [Cpp("pb2_event_b({0})")] public static extern int EventB(int i);
}

public static class PhysicsBackend
{
    static bool ready;
    static float pushedGx;
    static float pushedGy;

    // the pairs touching now (collider uids, a < b), kept from the begin and end events
    static List<int> tA = new List<int>();
    static List<int> tB = new List<int>();
    static List<int> tT = new List<int>();

    public static void ReleaseBody(Rigidbody2D r)
    {
        if (r.handle < 0) return;
        PB2.DestroyBody(r.handle);              // its shapes go with it
        r.handle = -1;
        for (int i = 0; i < Physics2D.joints.Count; i++)
        {
            Joint2D jn = Physics2D.joints[i];
            if (jn.handle >= 0 && (jn.connectedBody == r || jn.gameObject.GetComponent<Rigidbody2D>() == r)) { PB2.DestroyJoint(jn.handle); jn.handle = -1; jn.sig = -1f; }
        }
        for (int i = 0; i < Physics2D.colliders.Count; i++)
        {
            Collider2D c = Physics2D.colliders[i];
            if (c.body == r) { c.shape = -1; c.sig = -1f; }
        }
    }

    public static void ReleaseCollider(Collider2D c)
    {
        if (c.staticBody >= 0) PB2.DestroyBody(c.staticBody);
        else if (c.shape >= 0 && c.body != null && c.body.handle >= 0) PB2.DestroyCollider(c.uid);
        c.shape = -1;
        c.staticBody = -1;
        for (int i = tA.Count - 1; i >= 0; i--)
        {
            if (tA[i] == c.uid || tB[i] == c.uid) { tA.RemoveAt(i); tB.RemoveAt(i); tT.RemoveAt(i); }
        }
    }

    public static void ReleaseJoint(Joint2D j)
    {
        if (j.handle >= 0) PB2.DestroyJoint(j.handle);
        j.handle = -1;
    }

    static float B(bool v) { return v ? 1f : 0f; }

    /// <summary>Every field a joint is built from, as one number: when it changes the joint is built again.</summary>
    static float JointSignature(Joint2D j, int other)
    {
        float v = j.kind * 1.1f + B(j.enableCollision) * 2.3f + B(j.autoConfigureConnectedAnchor) * 3.7f + j.anchor.x * 5.1f + j.anchor.y * 7.3f;
        v = v + j.connectedAnchor.x * 11.9f + j.connectedAnchor.y * 13.1f + B(j.useLimits) * 17.3f + j.lower * 19.7f + j.upper * 23.9f;
        v = v + B(j.useMotor) * 29.1f + j.motorSpeed * 31.3f + j.maxMotor * 37.7f + j.distance * 41.9f + B(j.autoConfigureDistance) * 43.1f;
        v = v + B(j.maxDistanceOnly) * 47.3f + j.frequency * 53.7f + j.dampingRatio * 59.1f + j.angle * 61.3f + B(j.autoConfigureAngle) * 67.7f;
        v = v + j.maxForce * 71.9f + j.maxTorque * 73.1f + j.correctionScale * 79.3f + B(j.autoConfigureOffset) * 83.7f + j.linearOffset.x * 89.1f;
        v = v + j.linearOffset.y * 97.3f + j.angularOffset * 101.7f + B(j.autoConfigureTarget) * 103.9f + j.target.x * 107.1f + j.target.y * 109.3f;
        v = v + other * 113.7f;
        return v;
    }

    static void SyncJoint(Joint2D j)
    {
        Rigidbody2D own = j.gameObject.GetComponent<Rigidbody2D>();
        bool live = j.enabled && !j.destroyed && j.gameObject.activeSelf && own != null && own.handle >= 0 && !own.disabled;
        int other = -1;
        if (j.connectedBody != null)
        {
            if (j.connectedBody.handle < 0 || j.connectedBody.destroyed) live = false;
            else other = j.connectedBody.handle;
        }
        if (!live)
        {
            if (j.handle >= 0) ReleaseJoint(j);
            j.sig = -1f;
            return;
        }
        Vector3 s = j.transform.lossyScale;
        float sig = JointSignature(j, other) + own.handle * 127.3f + s.x * 131.9f + s.y * 137.1f;
        if (j.handle >= 0 && j.sig == sig) return;
        if (j.handle >= 0) ReleaseJoint(j);
        PB2.JParam(0, B(j.autoConfigureConnectedAnchor));
        PB2.JParam(1, j.anchor.x * s.x);
        PB2.JParam(2, j.anchor.y * s.y);
        PB2.JParam(3, j.connectedAnchor.x);
        PB2.JParam(4, j.connectedAnchor.y);
        PB2.JParam(5, B(j.enableCollision));
        PB2.JParam(6, B(j.useLimits));
        PB2.JParam(7, j.lower);
        PB2.JParam(8, j.upper);
        PB2.JParam(9, B(j.useMotor));
        PB2.JParam(10, j.motorSpeed);
        PB2.JParam(11, j.maxMotor);
        PB2.JParam(12, j.distance);
        PB2.JParam(13, B(j.autoConfigureDistance));
        PB2.JParam(14, B(j.maxDistanceOnly));
        PB2.JParam(15, j.frequency);
        PB2.JParam(16, j.dampingRatio);
        PB2.JParam(17, j.angle);
        PB2.JParam(18, B(j.autoConfigureAngle));
        PB2.JParam(19, j.maxForce);
        PB2.JParam(20, j.maxTorque);
        PB2.JParam(21, j.correctionScale);
        PB2.JParam(22, B(j.autoConfigureOffset));
        PB2.JParam(23, j.linearOffset.x);
        PB2.JParam(24, j.linearOffset.y);
        PB2.JParam(25, j.angularOffset);
        PB2.JParam(26, B(j.autoConfigureTarget));
        PB2.JParam(27, j.target.x);
        PB2.JParam(28, j.target.y);
        j.handle = PB2.Joint(j.kind, own.handle, other);
        j.sig = sig;
    }

    static int TypeOf(Rigidbody2D r)
    {
        if (r.bodyType == RigidbodyType2D.Static) return 2;
        if (r.bodyType == RigidbodyType2D.Kinematic) return 1;
        return 0;
    }

    static void SyncBody(Rigidbody2D r)
    {
        bool live = !r.destroyed && r.gameObject.activeSelf;
        if (r.handle < 0)
        {
            if (!live) return;
            Vector3 p = r.transform.position;
            float ang = Physics2D.AngleOf(r.transform);
            r.handle = PB2.Body(TypeOf(r), p.x, p.y, ang * 0.017453292f, r.velocity.x, r.velocity.y, r.gravityScale, r.drag, r.freezeRotation ? 1 : 0);
            r.lastAngle = ang;
            r.lastAngVel = r.angularVelocity;
            r.lastFreeze = r.freezeRotation;
            if (r.angularVelocity != 0f) PB2.SetAngVel(r.handle, r.angularVelocity * 0.017453292f);
            r.lastX = p.x;
            r.lastY = p.y;
            r.lastVx = r.velocity.x;
            r.lastVy = r.velocity.y;
            r.lastType = TypeOf(r);
            r.lastGravity = r.gravityScale;
            r.lastDrag = r.drag;
            r.lastMass = -1f;
            r.disabled = false;
            return;
        }
        if (!live)
        {
            if (!r.disabled) { PB2.SetEnabled(r.handle, 0); r.disabled = true; }
            return;
        }
        if (r.disabled) { PB2.SetEnabled(r.handle, 1); r.disabled = false; }
        Vector3 q = r.transform.position;
        if (q.x != r.lastX || q.y != r.lastY) { PB2.SetPos(r.handle, q.x, q.y); r.lastX = q.x; r.lastY = q.y; }
        if (r.velocity.x != r.lastVx || r.velocity.y != r.lastVy)
        {
            PB2.SetVelocity(r.handle, r.velocity.x, r.velocity.y);
            r.lastVx = r.velocity.x;
            r.lastVy = r.velocity.y;
        }
        float angNow = Physics2D.AngleOf(r.transform);
        if (angNow != r.lastAngle) { PB2.SetAngle(r.handle, angNow * 0.017453292f); r.lastAngle = angNow; }
        if (r.angularVelocity != r.lastAngVel) { PB2.SetAngVel(r.handle, r.angularVelocity * 0.017453292f); r.lastAngVel = r.angularVelocity; }
        if (r.freezeRotation != r.lastFreeze) { PB2.SetFreeze(r.handle, r.freezeRotation ? 1 : 0); r.lastFreeze = r.freezeRotation; }
        int t = TypeOf(r);
        if (t != r.lastType) { PB2.SetType(r.handle, t); r.lastType = t; r.lastMass = -1f; }
        if (r.gravityScale != r.lastGravity) { PB2.SetGravityScale(r.handle, r.gravityScale); r.lastGravity = r.gravityScale; }
        if (r.drag != r.lastDrag) { PB2.SetDamping(r.handle, r.drag); r.lastDrag = r.drag; }
        if (t == 0 && r.mass != r.lastMass) { PB2.SetMass(r.handle, r.mass); r.lastMass = r.mass; }
        float fx;
        float fy;
        r.TakeForce(out fx, out fy);
        if (fx != 0f || fy != 0f) PB2.Force(r.handle, fx, fy);
        float tq;
        float spin;
        r.TakeTorque(out tq, out spin);
        if (tq != 0f) PB2.Torque(r.handle, tq);
        if (spin != 0f) PB2.AngularImpulse(r.handle, spin);
    }

    /// <summary>What a shape was made from, as one number: if it changes the shape is made again.</summary>
    static float Signature(Collider2D c, float sx, float sy)
    {
        float v = c.sizeX * 3.1f + c.sizeY * 5.3f + c.radius * 7.7f + c.offset.x * 11.1f + c.offset.y * 13.3f;
        v = v + c.bounciness * 17.1f + c.friction * 19.9f + sx * 29.3f + sy * 31.7f;
        if (c.isTrigger) v = v + 23.5f;
        v = v + c.kind * 37.9f + c.frictionCombine * 41.3f + c.bounceCombine * 43.7f;
        PolygonCollider2D pc = c as PolygonCollider2D;
        if (pc != null) v = v + pc.pts.Count * 47.1f + pc.sizeX * 53.3f + pc.sizeY * 59.9f;
        return v;
    }

    static void SyncCollider(Collider2D c)
    {
        Rigidbody2D rb = c.gameObject.GetComponent<Rigidbody2D>();
        c.body = rb;
        bool live = Physics2D.Active(c);
        if (live && rb != null && (rb.handle < 0 || rb.disabled)) live = false;
        if (!live)
        {
            if (c.shape >= 0) { ReleaseShapeOnly(c); }
            return;
        }
        Vector3 p = c.transform.position;
        Vector3 s = c.transform.lossyScale;
        float sx = MathF.Abs(s.x);
        float sy = MathF.Abs(s.y);
        float sig = Signature(c, sx, sy);
        int body;
        if (rb != null)
        {
            if (c.staticBody >= 0) { PB2.DestroyBody(c.staticBody); c.staticBody = -1; c.shape = -1; }
            body = rb.handle;
        }
        else
        {
            if (c.staticBody < 0)
            {
                c.staticBody = PB2.Body(2, p.x, p.y, Physics2D.AngleOf(c.transform) * 0.017453292f, 0f, 0f, 1f, 0f, 1);
                c.staticX = p.x;
                c.staticY = p.y;
                c.shape = -1;
            }
            else if (p.x != c.staticX || p.y != c.staticY)
            {
                PB2.SetPos(c.staticBody, p.x, p.y);
                c.staticX = p.x;
                c.staticY = p.y;
            }
            body = c.staticBody;
        }
        if (c.shape >= 0 && c.sig == sig) return;
        if (c.shape >= 0) PB2.DestroyCollider(c.uid);
        float ox = c.offset.x * s.x;
        float oy = c.offset.y * s.y;
        int trig = c.isTrigger ? 1 : 0;
        int mat = c.frictionCombine + c.bounceCombine * 256;
        c.shape = 0;
        if (c.kind == 0)
        {
            PB2.Box(body, c.uid, 0.5f * c.sizeX * sx, 0.5f * c.sizeY * sy, ox, oy, trig, c.friction, c.bounciness, mat);
        }
        else if (c.kind == 1)
        {
            PB2.Circle(body, c.uid, c.radius * MathF.Max(sx, sy), ox, oy, trig, c.friction, c.bounciness, mat);
        }
        else if (c.kind == 2)
        {
            // a capsule: the radius across, a segment along; one no longer than it is wide is a circle (Box2D refuses a zero length)
            float hw = 0.5f * c.sizeX * sx;
            float hh = 0.5f * c.sizeY * sy;
            float rad = c.vertical ? hw : hh;
            float half = (c.vertical ? hh : hw) - rad;
            if (half <= 0.005f) PB2.Circle(body, c.uid, rad, ox, oy, trig, c.friction, c.bounciness, mat);
            else if (c.vertical) PB2.Capsule(body, c.uid, rad, ox, oy - half, ox, oy + half, trig, c.friction, c.bounciness, mat);
            else PB2.Capsule(body, c.uid, rad, ox - half, oy, ox + half, oy, trig, c.friction, c.bounciness, mat);
        }
        else
        {
            PolygonCollider2D pc = c as PolygonCollider2D;
            int at = 0;
            for (int k = 0; k < pc.pieces.Count; k++)
            {
                for (int j = 0; j < pc.pieces[k]; j++)
                {
                    PB2.PolyPoint(pc.pts[at] * s.x, pc.pts[at + 1] * s.y);
                    at += 2;
                }
                PB2.PolyPiece(body, c.uid, trig, c.friction, c.bounciness, mat);
            }
        }
        c.sig = sig;
        if (rb != null) rb.lastMass = -1f;       // the shapes decide the mass: apply the authored one again
    }

    static void ReleaseShapeOnly(Collider2D c)
    {
        if (c.staticBody >= 0) { PB2.DestroyBody(c.staticBody); c.staticBody = -1; }
        else if (c.body != null && c.body.handle >= 0) PB2.DestroyCollider(c.uid);
        c.shape = -1;
        c.sig = -1f;
        for (int i = tA.Count - 1; i >= 0; i--)
        {
            if (tA[i] == c.uid || tB[i] == c.uid) { tA.RemoveAt(i); tB.RemoveAt(i); tT.RemoveAt(i); }
        }
    }

    static int Find(int a, int b, int trigger)
    {
        for (int i = 0; i < tA.Count; i++)
        {
            if (tA[i] == a && tB[i] == b && tT[i] == trigger) return i;
        }
        return -1;
    }

    public static void Step(float dt)
    {
        if (!ready)
        {
            PB2.World(Physics2D.gravityX, Physics2D.gravityY);
            pushedGx = Physics2D.gravityX;
            pushedGy = Physics2D.gravityY;
            ready = true;
        }
        if (Physics2D.gravityX != pushedGx || Physics2D.gravityY != pushedGy)
        {
            PB2.SetGravity(Physics2D.gravityX, Physics2D.gravityY);
            pushedGx = Physics2D.gravityX;
            pushedGy = Physics2D.gravityY;
        }
        for (int i = 0; i < Physics2D.bodies.Count; i++) SyncBody(Physics2D.bodies[i]);
        for (int i = 0; i < Physics2D.colliders.Count; i++) SyncCollider(Physics2D.colliders[i]);

        for (int i = 0; i < Physics2D.joints.Count; i++) SyncJoint(Physics2D.joints[i]);

        PB2.Step(dt);

        for (int i = 0; i < Physics2D.bodies.Count; i++)
        {
            Rigidbody2D r = Physics2D.bodies[i];
            if (r.handle < 0 || r.disabled || r.destroyed) continue;
            if (r.bodyType == RigidbodyType2D.Static) continue;
            float x = PB2.X(r.handle);
            float y = PB2.Y(r.handle);
            Vector3 p = r.transform.position;
            r.transform.position = new Vector3(x, y, p.z);
            r.lastX = x;
            r.lastY = y;
            r.lastVx = PB2.Vx(r.handle);
            r.lastVy = PB2.Vy(r.handle);
            if (!r.freezeRotation)
            {
                float deg = PB2.Angle(r.handle) * 57.29578f;
                r.transform.rotation = Quaternion.Euler(0f, 0f, deg);
                r.lastAngle = Physics2D.AngleOf(r.transform);
                r.lastAngVel = PB2.AngVel(r.handle) * 57.29578f;
                r.angularVelocity = r.lastAngVel;
            }
            r.velocity = new Vector2(r.lastVx, r.lastVy);
        }

        int n = PB2.EventCount();
        for (int i = 0; i < n; i++)
        {
            int kind = PB2.EventKind(i);
            int a = PB2.EventA(i);
            int b = PB2.EventB(i);
            if (a > b) { int t = a; a = b; b = t; }
            int trigger = kind >= 2 ? 1 : 0;
            int at = Find(a, b, trigger);
            if (kind == 0 || kind == 2)
            {
                if (at < 0) { tA.Add(a); tB.Add(b); tT.Add(trigger); }
            }
            else if (at >= 0)
            {
                tA.RemoveAt(at);
                tB.RemoveAt(at);
                tT.RemoveAt(at);
            }
        }
        Physics2D.curA.Clear();
        Physics2D.curB.Clear();
        Physics2D.curT.Clear();
        for (int i = 0; i < tA.Count; i++)
        {
            Collider2D ca = Physics2D.ByUid(tA[i]);
            Collider2D cb = Physics2D.ByUid(tB[i]);
            if (ca == null || cb == null) continue;
            Physics2D.curA.Add(ca);
            Physics2D.curB.Add(cb);
            Physics2D.curT.Add(tT[i]);
        }
    }
}
