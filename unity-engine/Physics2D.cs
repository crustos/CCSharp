// CCSharp --unity-engine: 2D physics, in the same subset as the rest of the engine (it lowers to C, runs on DotNetAnywhere, and runs on .NET).
//
// A small impulse solver, not Box2D: rigid bodies without rotation (a body's angle is never changed), axis-aligned boxes and circles, gravity,
// restitution and friction, triggers, and the collision messages.  What Unity games of the simple kind need; a Box2D-backed solver would sit behind the
// same classes.
//
//   Rigidbody2D   bodyType (Dynamic, Kinematic, Static), mass, gravityScale, drag, velocity, AddForce, AddForce impulse
//   BoxCollider2D / CircleCollider2D   isTrigger, offset, size / radius, bounciness, friction
//   MonoBehaviour messages   FixedUpdate, OnCollisionEnter2D / Stay / Exit, OnTriggerEnter2D / Stay / Exit
//
// Colliders are axis aligned: a collider's world size is its size times the transform's scale, and the transform's rotation is not applied to it.

using System;
using System.Collections.Generic;

namespace UnityEngine;

public class Rigidbody2D : Component
{
    public RigidbodyType2D bodyType;
    public float mass;
    public float gravityScale;
    public float drag;
    public Vector2 velocity;
    public bool freezeRotation;
    float forceX;
    float forceY;
    // the backend's body (-1 for none) and what was last pushed to it or pulled from it
    public int handle;
    public float lastX;
    public float lastY;
    public float lastVx;
    public float lastVy;
    public float lastMass;
    public int lastType;
    public float lastGravity;
    public float lastDrag;
    public float angularVelocity;       // degrees / s
    float torqueAcc;
    float spinImpulse;
    public float lastAngle;             // degrees
    public float lastAngVel;
    public bool lastFreeze;
    public bool disabled;               // the backend body is switched off (its object is inactive)
    public bool simulated;              // false: the body and its colliders are out of the simulation (Unity's Rigidbody2D.simulated)

    public Rigidbody2D() { mass = 1f; gravityScale = 1f; handle = -1; simulated = true; }

    public override void OnAttach() { Physics2D.bodies.Add(this); }

    /// <summary>A force for the next step (mass divides it).</summary>
    public void AddForce(Vector2 force)
    {
        forceX += force.x;
        forceY += force.y;
    }

    /// <summary>Unity's overload with a mode: Force (0) is a force, Impulse (1) a change of velocity by force / mass.</summary>
    public void AddForce(Vector2 force, ForceMode2D mode)
    {
        if (mode == ForceMode2D.Impulse) AddImpulse(force);
        else AddForce(force);
    }

    public Vector2 linearVelocity
    {
        get { return velocity; }
        set { velocity = value; }
    }

    public float linearDamping
    {
        get { return drag; }
        set { drag = value; }
    }

    /// <summary>The owner's position in the plane.</summary>
    public Vector2 position
    {
        get { Vector3 p = transform.position; return new Vector2(p.x, p.y); }
        set { Vector3 p = transform.position; transform.position = new Vector3(value.x, value.y, p.z); }
    }

    public void MovePosition(Vector2 p) { position = p; }

    // ponytail: the centre of mass is the body's origin (the shapes' mass distribution is Box2D's alone).  Upgrade path: pb2_world_center.
    public Vector2 worldCenterOfMass { get { return position; } }

    public bool isKinematic
    {
        get { return bodyType == RigidbodyType2D.Kinematic; }
        set { bodyType = value ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic; }
    }

    /// <summary>The z rotation in degrees (Unity's Rigidbody2D.rotation): the owner's.</summary>
    public float rotation
    {
        get { return Physics2D.AngleOf(transform); }
        set { transform.rotation = Quaternion.Euler(0f, 0f, value); }
    }

    public void MoveRotation(float degrees) { rotation = degrees; }

    public void AddTorque(float t) { torqueAcc += t; }

    public void AddTorque(float t, ForceMode2D mode)
    {
        if (mode == ForceMode2D.Impulse) spinImpulse += t;
        else torqueAcc += t;
    }

    public void TakeTorque(out float t, out float impulse)
    {
        t = torqueAcc;
        impulse = spinImpulse;
        torqueAcc = 0f;
        spinImpulse = 0f;
    }

    /// <summary>An immediate change of velocity by impulse / mass.</summary>
    public void AddImpulse(Vector2 impulse)
    {
        if (bodyType != RigidbodyType2D.Dynamic) return;
        velocity = new Vector2(velocity.x + impulse.x / mass, velocity.y + impulse.y / mass);
    }

    public void TakeForce(out float fx, out float fy)
    {
        fx = forceX;
        fy = forceY;
        forceX = 0f;
        forceY = 0f;
    }
}

/// <summary>Base of the shapes.  `kind` is 0 for a box and 1 for a circle.</summary>
public abstract class Collider2D : Component
{
    public bool enabled;
    public bool isTrigger;
    public Vector2 offset;
    public float bounciness;
    public float friction;
    public int kind;                    // 0 box, 1 circle, 2 capsule, 3 polygon
    public int frictionCombine;         // PhysicsMaterialCombine: Average 0, Multiply 1, Minimum 2, Maximum 3
    public int bounceCombine;
    public bool vertical;               // a capsule's direction
    public float sizeX;
    public float sizeY;
    public float radius;
    public Rigidbody2D body;            // the body that moves it (set each step), or null: a static collider
    public int uid;                     // stable while it lives: the backend names it by this
    public int shape;                   // the backend's shape, or -1
    public int staticBody;              // the backend's static body, when it has no Rigidbody2D, else -1
    public float shapeSx;               // the scale the shape was made for
    public float shapeSy;
    public float sig;                   // a digest of what the shape was made from
    public float staticX;               // where the static body was put
    public float staticY;
    public int version;                 // counts changes to a polygon's or an edge's points (the backend makes the shape again)

    public override void OnAttach() { enabled = true; shape = -1; staticBody = -1; uid = Physics2D.NextUid(); Physics2D.colliders.Add(this); }

#if UNITY_MANAGED
    PhysicsMaterial2D m_Material;

    /// <summary>The world box around the shape (its rotation and scale taken in).</summary>
    public Bounds bounds { get { return Shapes2D.BoundsOf(this); } }
    public Rigidbody2D attachedRigidbody { get { return gameObject.GetComponent<Rigidbody2D>(); } }
    public PhysicsMaterial2D sharedMaterial
    {
        get { return m_Material; }
        set { m_Material = value; if (value != null) { friction = value.friction; bounciness = value.bounciness; } }
    }
    public int Overlap(ContactFilter2D contactFilter, Collider2D[] results) { return Shapes2D.Overlap(this, contactFilter, results, null); }
    public int Overlap(ContactFilter2D contactFilter, List<Collider2D> results) { return Shapes2D.Overlap(this, contactFilter, null, results); }
    public int Cast(Vector2 direction, RaycastHit2D[] results, float distance = float.PositiveInfinity) { return Shapes2D.CastCollider(this, direction, ContactFilter2D.Unfiltered(), results, null, distance); }
    public int Cast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance = float.PositiveInfinity) { return Shapes2D.CastCollider(this, direction, contactFilter, results, null, distance); }
    public int Cast(Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity) { return Shapes2D.CastCollider(this, direction, contactFilter, null, results, distance); }
    public bool IsTouching(Collider2D collider) { return Shapes2D.Touching(this, collider); }
    public bool IsTouching(ContactFilter2D contactFilter) { return Shapes2D.TouchingAny(this, contactFilter); }
    public bool IsTouchingLayers(int layerMask = -1) { ContactFilter2D f = ContactFilter2D.Unfiltered(); f.useTriggers = true; f.SetLayerMask(layerMask); return Shapes2D.TouchingAny(this, f); }
    public bool OverlapPoint(Vector2 point) { return Shapes2D.ContainsPoint(this, point); }
#endif
}

public class BoxCollider2D : Collider2D
{
    public float edgeRadius;            // rounds the corners (the box grows by it)

    public BoxCollider2D() { kind = 0; sizeX = 1f; sizeY = 1f; friction = 0.4f; }

    public Vector2 size
    {
        get { return new Vector2(sizeX, sizeY); }
        set { sizeX = value.x; sizeY = value.y; }
    }
}

public class CapsuleCollider2D : Collider2D
{
    public CapsuleCollider2D() { kind = 2; sizeX = 1f; sizeY = 2f; friction = 0.4f; vertical = true; }

    public Vector2 size
    {
        get { return new Vector2(sizeX, sizeY); }
        set { sizeX = value.x; sizeY = value.y; }
    }
}

/// <summary>A polygon collider: convex pieces (the packer cuts the outline up), each a run of AddPoint calls closed by EndPiece.  Points are in the object's space,
/// the collider's offset already added.</summary>
public class PolygonCollider2D : Collider2D
{
    public List<float> pts;
    public List<int> pieces;
    public List<float> pathPts;         // the outlines as authored (x, y pairs, without the offset): what points / GetPath give back
    public List<int> pathLens;
    public Vector2 pathOffset;          // the authored offset (offset itself is the pieces' box, which the C# backend collides)
    float minX;
    float minY;
    float maxX;
    float maxY;
    int open;

    public PolygonCollider2D()
    {
        kind = 3;
        friction = 0.4f;
        pts = new List<float>();
        pieces = new List<int>();
        pathPts = new List<float>();
        pathLens = new List<int>();
    }

    /// <summary>A point of an authored outline; EndPath closes it.  (The pieces collide; the outlines are what scripts read.)</summary>
    public void AddPathPoint(float x, float y)
    {
        pathPts.Add(x);
        pathPts.Add(y);
    }

    public void EndPath(int count) { pathLens.Add(count); }

    public void ClearPieces()
    {
        pts.Clear();
        pieces.Clear();
        open = 0;
        version += 1;
    }

    public void AddPoint(float x, float y)
    {
        if (pts.Count == 0) { minX = x; maxX = x; minY = y; maxY = y; }
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
        pts.Add(x);
        pts.Add(y);
        open += 1;
    }

#if UNITY_MANAGED
    public int pathCount
    {
        get { return pathLens.Count; }
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException("value");
            List<Vector2[]> keep = new List<Vector2[]>();
            for (int i = 0; i < value; i++) keep.Add(i < pathLens.Count ? GetPath(i) : new Vector2[0]);
            SetPaths(keep);
        }
    }

    /// <summary>Path 0.</summary>
    public Vector2[] points { get { return pathLens.Count == 0 ? new Vector2[0] : GetPath(0); } set { SetPath(0, value); } }

    public int GetTotalPointCount() { return pathPts.Count / 2; }

    public Vector2[] GetPath(int index)
    {
        if (index < 0 || index >= pathLens.Count) throw new ArgumentOutOfRangeException("index");
        int at = 0;
        for (int i = 0; i < index; i++) at += pathLens[i] * 2;
        Vector2[] r = new Vector2[pathLens[index]];
        for (int i = 0; i < r.Length; i++) r[i] = new Vector2(pathPts[at + 2 * i], pathPts[at + 2 * i + 1]);
        return r;
    }

    public void SetPath(int index, Vector2[] points)
    {
        if (index < 0) throw new ArgumentOutOfRangeException("index");
        List<Vector2[]> all = new List<Vector2[]>();
        for (int i = 0; i < Math.Max(pathLens.Count, index + 1); i++) all.Add(i == index ? points : i < pathLens.Count ? GetPath(i) : new Vector2[0]);
        SetPaths(all);
    }

    public void SetPath(int index, List<Vector2> points) { SetPath(index, points.ToArray()); }

    /// <summary>The outlines again: each cut into triangles for the shapes.</summary>
    void SetPaths(List<Vector2[]> paths)
    {
        pathPts.Clear();
        pathLens.Clear();
        ClearPieces();
        foreach (Vector2[] path in paths)
        {
            foreach (Vector2 q in path) AddPathPoint(q.x, q.y);
            EndPath(path.Length);
            foreach (Vector2 q in Shapes2D.Triangulate(path))
            {
                AddPoint(q.x + pathOffset.x, q.y + pathOffset.y);
                if (open == 3) EndPiece();
            }
        }
    }
#endif

    public void EndPiece()
    {
        pieces.Add(open);
        open = 0;
        // the managed backend sees the outline's box
        sizeX = maxX - minX;
        sizeY = maxY - minY;
        offset = new Vector2(0.5f * (minX + maxX), 0.5f * (minY + maxY));
    }
}

public class CircleCollider2D : Collider2D
{
    public CircleCollider2D() { kind = 1; radius = 0.5f; friction = 0.4f; }
}

/// <summary>An open chain of segments (points in the object's space, without the offset); edgeRadius gives them thickness.  The C# backend collides its box.</summary>
public class EdgeCollider2D : Collider2D
{
    public List<float> pts;
    public float edgeRadius;

    public EdgeCollider2D()
    {
        kind = 4;
        friction = 0.4f;
        pts = new List<float>();
        pts.Add(-0.5f);
        pts.Add(0f);
        pts.Add(0.5f);
        pts.Add(0f);
        sizeX = 1f;
    }

    public void ClearPoints() { pts.Clear(); version += 1; }

#if UNITY_MANAGED
    public int pointCount { get { return pts.Count / 2; } }
    public int edgeCount { get { return Math.Max(0, pts.Count / 2 - 1); } }

    public Vector2[] points
    {
        get
        {
            Vector2[] r = new Vector2[pts.Count / 2];
            for (int i = 0; i < r.Length; i++) r[i] = new Vector2(pts[2 * i], pts[2 * i + 1]);
            return r;
        }
        set
        {
            if (value == null || value.Length < 2) throw new ArgumentException("An EdgeCollider2D needs at least two points.");
            ClearPoints();
            foreach (Vector2 q in value) AddPoint(q.x, q.y);
        }
    }

    public bool SetPoints(List<Vector2> points) { if (points.Count < 2) return false; this.points = points.ToArray(); return true; }
#endif

    public void AddPoint(float x, float y)
    {
        pts.Add(x);
        pts.Add(y);
        version += 1;
        float minX = pts[0];
        float maxX = pts[0];
        float minY = pts[1];
        float maxY = pts[1];
        for (int i = 2; i < pts.Count; i += 2)
        {
            minX = MathF.Min(minX, pts[i]);
            maxX = MathF.Max(maxX, pts[i]);
            minY = MathF.Min(minY, pts[i + 1]);
            maxY = MathF.Max(maxY, pts[i + 1]);
        }
        sizeX = maxX - minX + 2f * edgeRadius;
        sizeY = maxY - minY + 2f * edgeRadius;
    }
}


/// <summary>The 2D joints, one class with the fields every kind reads (Unity's, same names); a subclass sets the kind and Unity's defaults.  They need the Box2D backend:
/// the C# backend does not move bodies by joints.  A change to any field makes the backend build the joint again.</summary>
public abstract class Joint2D : Component
{
    public int kind;                    // 0 hinge, 1 distance, 2 spring, 3 fixed, 4 slider, 5 wheel, 6 friction, 7 relative, 8 target
    public bool enabled;
    public Rigidbody2D connectedBody;   // null: the world
    public bool enableCollision;
    public bool autoConfigureConnectedAnchor;
    public Vector2 anchor;
    public Vector2 connectedAnchor;
    public bool useLimits;
    public float lower;                 // degrees (hinge) or metres (slider, wheel)
    public float upper;
    public bool useMotor;
    public float motorSpeed;            // degrees / s (hinge, wheel) or metres / s (slider)
    public float maxMotor;              // torque or force
    public float distance;
    public bool autoConfigureDistance;
    public bool maxDistanceOnly;
    public float frequency;
    public float dampingRatio;
    public float angle;                 // the slider's and the wheel's axis, degrees
    public bool autoConfigureAngle;
    public float maxForce;
    public float maxTorque;
    public float correctionScale;
    public bool autoConfigureOffset;
    public Vector2 linearOffset;
    public float angularOffset;
    public bool autoConfigureTarget;
    public Vector2 target;
    public int handle;                  // the backend's joint, or -1
    public float sig;

    public Joint2D()
    {
        enabled = true;
        handle = -1;
        autoConfigureConnectedAnchor = true;
        autoConfigureDistance = true;
        autoConfigureOffset = true;
        autoConfigureTarget = true;
        distance = 1f;
        maxMotor = 10000f;
        correctionScale = 0.3f;
        sig = -1f;
    }

    public override void OnAttach() { Physics2D.joints.Add(this); }
}

public class HingeJoint2D : Joint2D { public HingeJoint2D() { kind = 0; } }
public class DistanceJoint2D : Joint2D { public DistanceJoint2D() { kind = 1; } }
public class SpringJoint2D : Joint2D { public SpringJoint2D() { kind = 2; frequency = 1f; } }
public class FixedJoint2D : Joint2D { public FixedJoint2D() { kind = 3; } }
public class SliderJoint2D : Joint2D { public SliderJoint2D() { kind = 4; } }
public class WheelJoint2D : Joint2D { public WheelJoint2D() { kind = 5; frequency = 2f; dampingRatio = 0.7f; angle = 90f; } }
public class FrictionJoint2D : Joint2D { public FrictionJoint2D() { kind = 6; } }
public class RelativeJoint2D : Joint2D { public RelativeJoint2D() { kind = 7; maxForce = 10000f; maxTorque = 10000f; } }
public class TargetJoint2D : Joint2D { public TargetJoint2D() { kind = 8; frequency = 5f; dampingRatio = 1f; maxForce = 1000f; } }

/// <summary>What a collision message is given.  One object is reused for every message, so a script must not keep it.</summary>
[MaxInstances(2)]
public class Collision2D
{
    public Collider2D collider;         // the other collider
    public Collider2D otherCollider;    // this object's
    public GameObject gameObject;       // the other object
    public Rigidbody2D rigidbody;       // the other object's body, or null

#if UNITY_MANAGED
    public Transform transform { get { return gameObject.transform; } }
    public Rigidbody2D otherRigidbody { get { return otherCollider.attachedRigidbody; } }
    public int contactCount { get { return 1; } }

    /// <summary>The contact, worked out from the shapes now (the backend reports pairs, not points): its normal points from the other collider to this one.
    /// ponytail: one contact per pair, at the deepest point.  Upgrade path: Box2D's manifold points through pb2.</summary>
    public ContactPoint2D GetContact(int index)
    {
        if (index != 0) throw new ArgumentOutOfRangeException("index");
        return Shapes2D.Contact(otherCollider, collider);
    }

    public int GetContacts(ContactPoint2D[] contacts)
    {
        if (contacts.Length == 0) return 0;
        contacts[0] = GetContact(0);
        return 1;
    }

    public Vector2 relativeVelocity
    {
        get
        {
            Rigidbody2D a = otherRigidbody;
            Vector2 va = a == null ? Vector2.zero : a.velocity;
            Vector2 vb = rigidbody == null ? Vector2.zero : rigidbody.velocity;
            return vb - va;
        }
    }
#endif
}

public static class Physics2D
{
    public static List<Rigidbody2D> bodies = new List<Rigidbody2D>();
    public static List<Collider2D> colliders = new List<Collider2D>();
    public static List<Joint2D> joints = new List<Joint2D>();
    public static float gravityX = 0f;
    public static float gravityY = -9.81f;
    public static Vector2 gravity
    {
        get { return new Vector2(gravityX, gravityY); }
        set { gravityX = value.x; gravityY = value.y; }
    }
    public static float fixedDeltaTime = 0.02f;
    // Physics2D.IgnoreCollision's pairs (collider uids), and the layer collision matrix (a layer's mask; none set: every layer meets every layer)
    public static List<int> ignoreA = new List<int>();
    public static List<int> ignoreB = new List<int>();
    public static List<int> layerMasks = new List<int>();
    static int uids;
    public static int NextUid() { uids += 1; return uids; }
    /// <summary>An object's z rotation in degrees, from its quaternion.</summary>
    public static float AngleOf(Transform t)
    {
        Quaternion q = t.rotation;
        return 114.59156f * MathF.Atan2(q.z, q.w);
    }

#if UNITY_MANAGED
    public const int IgnoreRaycastLayer = 4;
    public const int DefaultRaycastLayers = -5;
    public const int AllLayers = -1;
    public static float defaultContactOffset = 0.01f;
    public static bool queriesHitTriggers = true;
    public static bool queriesStartInColliders = true;
    public static bool autoSyncTransforms;

    /// <summary>Queries read the transforms as they are, so there is nothing to push.</summary>
    public static void SyncTransforms() { }

    public static RaycastHit2D Raycast(Vector2 origin, Vector2 direction, float distance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers,
                                       float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        return Shapes2D.First(Shapes2D.Ray(origin, direction, distance, ContactFilter2D.FromLayers(layerMask, minDepth, maxDepth)));
    }
    public static int Raycast(Vector2 origin, Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance = float.PositiveInfinity)
    {
        return Shapes2D.Fill(Shapes2D.Ray(origin, direction, distance, contactFilter), results, null);
    }
    public static int Raycast(Vector2 origin, Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity)
    {
        return Shapes2D.Fill(Shapes2D.Ray(origin, direction, distance, contactFilter), null, results);
    }
    public static RaycastHit2D[] RaycastAll(Vector2 origin, Vector2 direction, float distance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers,
                                            float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        return Shapes2D.Ray(origin, direction, distance, ContactFilter2D.FromLayers(layerMask, minDepth, maxDepth)).ToArray();
    }
    public static RaycastHit2D Linecast(Vector2 start, Vector2 end, int layerMask = DefaultRaycastLayers, float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        return Raycast(start, end - start, (end - start).magnitude, layerMask, minDepth, maxDepth);
    }
    public static int Linecast(Vector2 start, Vector2 end, ContactFilter2D contactFilter, RaycastHit2D[] results)
    {
        return Raycast(start, end - start, contactFilter, results, (end - start).magnitude);
    }
    public static RaycastHit2D[] LinecastAll(Vector2 start, Vector2 end, int layerMask = DefaultRaycastLayers, float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        return RaycastAll(start, end - start, (end - start).magnitude, layerMask, minDepth, maxDepth);
    }
    public static RaycastHit2D BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers,
                                       float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        return Shapes2D.First(Shapes2D.Sweep(Shapes2D.Box(origin, size, angle), null, direction, distance, ContactFilter2D.FromLayers(layerMask, minDepth, maxDepth)));
    }
    public static int BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance = float.PositiveInfinity)
    {
        return Shapes2D.Fill(Shapes2D.Sweep(Shapes2D.Box(origin, size, angle), null, direction, distance, contactFilter), results, null);
    }
    public static RaycastHit2D[] BoxCastAll(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers,
                                            float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        return Shapes2D.Sweep(Shapes2D.Box(origin, size, angle), null, direction, distance, ContactFilter2D.FromLayers(layerMask, minDepth, maxDepth)).ToArray();
    }
    public static RaycastHit2D CircleCast(Vector2 origin, float radius, Vector2 direction, float distance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers,
                                          float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        return Shapes2D.First(Shapes2D.Sweep(Shapes2D.Circle(origin, radius), null, direction, distance, ContactFilter2D.FromLayers(layerMask, minDepth, maxDepth)));
    }
    public static RaycastHit2D[] CircleCastAll(Vector2 origin, float radius, Vector2 direction, float distance = float.PositiveInfinity, int layerMask = DefaultRaycastLayers,
                                               float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        return Shapes2D.Sweep(Shapes2D.Circle(origin, radius), null, direction, distance, ContactFilter2D.FromLayers(layerMask, minDepth, maxDepth)).ToArray();
    }
    public static Collider2D OverlapPoint(Vector2 point, int layerMask = DefaultRaycastLayers, float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        List<Collider2D> all = Shapes2D.OverlapShape(Shapes2D.Circle(point, 0f), ContactFilter2D.FromLayers(layerMask, minDepth, maxDepth));
        return all.Count == 0 ? null : all[0];
    }
    public static Collider2D OverlapCircle(Vector2 point, float radius, int layerMask = DefaultRaycastLayers, float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        List<Collider2D> all = Shapes2D.OverlapShape(Shapes2D.Circle(point, radius), ContactFilter2D.FromLayers(layerMask, minDepth, maxDepth));
        return all.Count == 0 ? null : all[0];
    }
    public static Collider2D[] OverlapCircleAll(Vector2 point, float radius, int layerMask = DefaultRaycastLayers, float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        return Shapes2D.OverlapShape(Shapes2D.Circle(point, radius), ContactFilter2D.FromLayers(layerMask, minDepth, maxDepth)).ToArray();
    }
    public static int OverlapCircle(Vector2 point, float radius, ContactFilter2D contactFilter, Collider2D[] results)
    {
        return Shapes2D.Fill(Shapes2D.OverlapShape(Shapes2D.Circle(point, radius), contactFilter), results);
    }
    public static Collider2D OverlapBox(Vector2 point, Vector2 size, float angle, int layerMask = DefaultRaycastLayers, float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        List<Collider2D> all = Shapes2D.OverlapShape(Shapes2D.Box(point, size, angle), ContactFilter2D.FromLayers(layerMask, minDepth, maxDepth));
        return all.Count == 0 ? null : all[0];
    }
    public static Collider2D[] OverlapBoxAll(Vector2 point, Vector2 size, float angle, int layerMask = DefaultRaycastLayers, float minDepth = float.NegativeInfinity, float maxDepth = float.PositiveInfinity)
    {
        return Shapes2D.OverlapShape(Shapes2D.Box(point, size, angle), ContactFilter2D.FromLayers(layerMask, minDepth, maxDepth)).ToArray();
    }
    public static int OverlapBox(Vector2 point, Vector2 size, float angle, ContactFilter2D contactFilter, Collider2D[] results)
    {
        return Shapes2D.Fill(Shapes2D.OverlapShape(Shapes2D.Box(point, size, angle), contactFilter), results);
    }

    public static void IgnoreCollision(Collider2D collider1, Collider2D collider2, bool ignore = true)
    {
        for (int i = ignoreA.Count - 1; i >= 0; i--)
        {
            if ((ignoreA[i] == collider1.uid && ignoreB[i] == collider2.uid) || (ignoreA[i] == collider2.uid && ignoreB[i] == collider1.uid)) { ignoreA.RemoveAt(i); ignoreB.RemoveAt(i); }
        }
        if (ignore) { ignoreA.Add(collider1.uid); ignoreB.Add(collider2.uid); }
    }
    public static bool GetIgnoreCollision(Collider2D collider1, Collider2D collider2)
    {
        for (int i = 0; i < ignoreA.Count; i++)
        {
            if ((ignoreA[i] == collider1.uid && ignoreB[i] == collider2.uid) || (ignoreA[i] == collider2.uid && ignoreB[i] == collider1.uid)) return true;
        }
        return false;
    }
    public static int GetLayerCollisionMask(int layer) { return LayerMaskOf(layer); }
    public static void SetLayerCollisionMask(int layer, int layerMask)
    {
        if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException("layer");
        while (layerMasks.Count < 32) layerMasks.Add(-1);
        layerMasks[layer] = layerMask;
    }
    public static void IgnoreLayerCollision(int layer1, int layer2, bool ignore = true)
    {
        int m1 = LayerMaskOf(layer1), m2 = LayerMaskOf(layer2);
        SetLayerCollisionMask(layer1, ignore ? m1 & ~(1 << layer2) : m1 | (1 << layer2));
        SetLayerCollisionMask(layer2, ignore ? m2 & ~(1 << layer1) : m2 | (1 << layer1));
    }
    public static bool GetIgnoreLayerCollision(int layer1, int layer2) { return (LayerMaskOf(layer1) & (1 << layer2)) == 0; }
#endif

    public static Collider2D ByUid(int uid)
    {
        for (int i = 0; i < colliders.Count; i++)
        {
            if (colliders[i].uid == uid) return colliders[i];
        }
        return null;
    }

    static Collision2D scratch;
    static float accumulator;

    // the pairs touching in the last step and in this one: (a, b, trigger)
    static List<Collider2D> prevA = new List<Collider2D>();
    static List<Collider2D> prevB = new List<Collider2D>();
    static List<int> prevT = new List<int>();
    public static List<Collider2D> curA = new List<Collider2D>();
    public static List<Collider2D> curB = new List<Collider2D>();
    public static List<int> curT = new List<int>();

    // the overlap Test found: a normal from a to b, and how deep

    public static void RemoveBody(Rigidbody2D r)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i] == r) { bodies.RemoveAt(i); break; }
        }
        PhysicsBackend.ReleaseBody(r);
    }

    public static void RemoveJoint(Joint2D j)
    {
        for (int i = 0; i < joints.Count; i++)
        {
            if (joints[i] == j) { joints.RemoveAt(i); break; }
        }
        PhysicsBackend.ReleaseJoint(j);
    }

    public static void RemoveCollider(Collider2D c)
    {
        for (int i = 0; i < colliders.Count; i++)
        {
            if (colliders[i] == c) { colliders.RemoveAt(i); break; }
        }
        PhysicsBackend.ReleaseCollider(c);
        // a pair that held it is over without a message (the object is going away)
        for (int i = prevA.Count - 1; i >= 0; i--)
        {
            if (prevA[i] == c || prevB[i] == c) { prevA.RemoveAt(i); prevB.RemoveAt(i); prevT.RemoveAt(i); }
        }
    }

    /// <summary>The fixed steps due for this frame: each runs FixedUpdate on the scripts, then the physics.</summary>
    public static void Update(float dt)
    {
        accumulator += dt;
        int steps = 0;
        while (accumulator >= fixedDeltaTime && steps < 5)
        {
            Engine.FixedUpdate();
            Step(fixedDeltaTime);
            accumulator -= fixedDeltaTime;
            steps++;
        }
        if (steps == 5) accumulator = 0f;
    }

    public static bool Active(Collider2D c)
    {
        Rigidbody2D rb = c.gameObject.GetComponent<Rigidbody2D>();
        if (rb != null && !rb.simulated) return false;
        return c.enabled && c.gameObject.activeInHierarchy && !c.destroyed;
    }

    public static int LayerMaskOf(int layer)
    {
        if (layer < 0 || layer >= layerMasks.Count) return -1;
        return layerMasks[layer];
    }

    /// <summary>May a and b touch: not an ignored pair, and their layers meet in the matrix.</summary>
    public static bool Collides(Collider2D a, Collider2D b)
    {
        if ((LayerMaskOf(a.gameObject.layer) & (1 << b.gameObject.layer)) == 0) return false;
        for (int i = 0; i < ignoreA.Count; i++)
        {
            if ((ignoreA[i] == a.uid && ignoreB[i] == b.uid) || (ignoreA[i] == b.uid && ignoreB[i] == a.uid)) return false;
        }
        return true;
    }

    /// <summary>One step: forces and gravity, movement, then the contacts and their messages.</summary>
    /// <summary>One step: the backend moves the bodies and says which colliders touch (curA, curB, curT), then the messages go out.</summary>
    public static void Step(float dt)
    {
        PhysicsBackend.Step(dt);
        Messages();
    }

    // ---- messages

    static bool Held(List<Collider2D> la, List<Collider2D> lb, List<int> lt, Collider2D a, Collider2D b)
    {
        for (int i = 0; i < la.Count; i++)
        {
            if (la[i] == a && lb[i] == b) return true;
        }
        return false;
    }

    static void Messages()
    {
        if (scratch == null) scratch = new Collision2D();
        for (int i = curA.Count - 1; i >= 0; i--)
        {
            // ponytail: an ignored pair (IgnoreCollision, the layer matrix) gets no messages and the C# backend does not push it apart; Box2D
            // still does (pb2 has no contact filter).  Upgrade path: a b2ShapeDef filter category / mask per collider and a custom filter.
            if (!Collides(curA[i], curB[i])) { curA.RemoveAt(i); curB.RemoveAt(i); curT.RemoveAt(i); }
        }
        for (int i = 0; i < curA.Count; i++)
        {
            bool known = Held(prevA, prevB, prevT, curA[i], curB[i]);
            Send(curA[i], curB[i], curT[i] == 1, known ? 1 : 0);
        }
        for (int i = 0; i < prevA.Count; i++)
        {
            if (!Held(curA, curB, curT, prevA[i], prevB[i])) Send(prevA[i], prevB[i], prevT[i] == 1, 2);
        }
        // this step's pairs are the next step's "last"
        prevA.Clear();
        prevB.Clear();
        prevT.Clear();
        for (int i = 0; i < curA.Count; i++)
        {
            prevA.Add(curA[i]);
            prevB.Add(curB[i]);
            prevT.Add(curT[i]);
        }
    }

    /// <summary>phase 0 Enter, 1 Stay, 2 Exit, to the scripts of both objects.</summary>
    static void Send(Collider2D a, Collider2D b, bool trigger, int phase)
    {
        Deliver(a, b, trigger, phase);
        Deliver(b, a, trigger, phase);
    }

    static void Deliver(Collider2D self, Collider2D other, bool trigger, int phase)
    {
        GameObject g = self.gameObject;
        scratch.collider = other;
        scratch.otherCollider = self;
        scratch.gameObject = other.gameObject;
        scratch.rigidbody = other.body;
        for (int i = 0; i < g.components.Count; i++)
        {
            MonoBehaviour m = g.components[i] as MonoBehaviour;
            if (m == null || !m.enabled) continue;
#if UNITY_MANAGED
            try { Message(m, trigger, phase, other); }
            catch (Exception e) { Debug.LogException(e, m); }     // as Unity: logged, and the other scripts still get theirs
#else
            Message(m, trigger, phase, other);
#endif
        }
    }

    static void Message(MonoBehaviour m, bool trigger, int phase, Collider2D other)
    {
        {
            if (trigger)
            {
                if (phase == 0) m.OnTriggerEnter2D(other);
                else if (phase == 1) m.OnTriggerStay2D(other);
                else m.OnTriggerExit2D(other);
            }
            else
            {
                if (phase == 0) m.OnCollisionEnter2D(scratch);
                else if (phase == 1) m.OnCollisionStay2D(scratch);
                else m.OnCollisionExit2D(scratch);
            }
        }
    }
}
