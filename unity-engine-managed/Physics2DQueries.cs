// named-by: bounds points Overlap Cast IsTouching IsTouchingLayers OverlapPoint Raycast RaycastAll Linecast LinecastAll BoxCast BoxCastAll CircleCast CircleCastAll OverlapCircle OverlapCircleAll OverlapBox OverlapBoxAll IgnoreCollision GetIgnoreCollision GetLayerCollisionMask SetLayerCollisionMask IgnoreLayerCollision GetIgnoreLayerCollision SyncTransforms defaultContactOffset queriesHitTriggers queriesStartInColliders autoSyncTransforms GetContact GetContacts contactCount relativeVelocity otherRigidbody attachedRigidbody sharedMaterial pathCount SetPath GetPath GetTotalPointCount pointCount edgeCount SetPoints EdgeCollider2D DefaultRaycastLayers IgnoreRaycastLayer AllLayers
//
// CCSharp --unity-engine, managed half: Physics2D's queries (raycasts, shape casts, overlaps, contacts) over the colliders as they stand.  The
// backend (Box2D or the C# one) steps the bodies; the queries are geometry here, on each collider's convex pieces in world space, so they agree
// with the transforms whichever backend runs.

using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct RaycastHit2D
    {
        public Vector2 centroid;
        public Vector2 point;
        public Vector2 normal;
        public float distance;
        public float fraction;
        public Collider2D collider;

        public Rigidbody2D rigidbody { get { return collider == null ? null : collider.attachedRigidbody; } }
        public Transform transform { get { return collider == null ? null : collider.transform; } }
        public static implicit operator bool(RaycastHit2D hit) { return hit.collider != null; }
        public int CompareTo(RaycastHit2D other) { return collider == null ? 1 : other.collider == null ? -1 : distance < other.distance ? -1 : distance > other.distance ? 1 : 0; }
    }

    public struct ContactPoint2D
    {
        public Vector2 point;
        public Vector2 normal;
        public float separation;
        public Collider2D collider;
        public Collider2D otherCollider;
        public Rigidbody2D rigidbody;
        public Rigidbody2D otherRigidbody;
        public Vector2 relativeVelocity;
        public float normalImpulse;
        public float tangentImpulse;
        public bool enabled;
    }

    public struct ContactFilter2D
    {
        public const float NormalAngleUpperLimit = 359.9999f;
        public bool useTriggers;
        public bool useLayerMask;
        public bool useDepth;
        public bool useOutsideDepth;
        public bool useNormalAngle;
        public bool useOutsideNormalAngle;
        public LayerMask layerMask;
        public float minDepth;
        public float maxDepth;
        public float minNormalAngle;
        public float maxNormalAngle;

        public bool isFiltering { get { return !useTriggers || useLayerMask || useDepth || useNormalAngle; } }

        public ContactFilter2D NoFilter()
        {
            useTriggers = true;
            useLayerMask = useDepth = useOutsideDepth = useNormalAngle = useOutsideNormalAngle = false;
            layerMask = -1;
            minDepth = float.NegativeInfinity;
            maxDepth = float.PositiveInfinity;
            minNormalAngle = 0f;
            maxNormalAngle = NormalAngleUpperLimit;
            return this;
        }

        public void SetLayerMask(LayerMask layerMask) { this.layerMask = layerMask; useLayerMask = true; }
        public void ClearLayerMask() { useLayerMask = false; }
        public void SetDepth(float minDepth, float maxDepth) { this.minDepth = minDepth; this.maxDepth = maxDepth; useDepth = true; }
        public void ClearDepth() { useDepth = false; }
        public void SetNormalAngle(float minNormalAngle, float maxNormalAngle) { this.minNormalAngle = minNormalAngle; this.maxNormalAngle = maxNormalAngle; useNormalAngle = true; }
        public void ClearNormalAngle() { useNormalAngle = false; }

        public bool IsFilteringTrigger(Collider2D collider) { return !useTriggers && collider.isTrigger; }
        public bool IsFilteringLayerMask(GameObject obj) { return useLayerMask && (layerMask.value & (1 << obj.layer)) == 0; }
        public bool IsFilteringDepth(GameObject obj)
        {
            if (!useDepth) return false;
            float z = obj.transform.position.z;
            bool inside = z >= minDepth && z <= maxDepth;
            return useOutsideDepth ? inside : !inside;
        }
        public bool IsFilteringNormalAngle(Vector2 normal)
        {
            if (!useNormalAngle) return false;
            float a = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
            if (a < 0f) a += 360f;
            bool inside = a >= minNormalAngle && a <= maxNormalAngle;
            return useOutsideNormalAngle ? inside : !inside;
        }

        internal bool Rejects(Collider2D c) { return IsFilteringTrigger(c) || IsFilteringLayerMask(c.gameObject) || IsFilteringDepth(c.gameObject); }

        /// <summary>What a query without a filter uses: every layer, triggers as Physics2D.queriesHitTriggers says.</summary>
        internal static ContactFilter2D Unfiltered()
        {
            ContactFilter2D f = new ContactFilter2D().NoFilter();
            f.useTriggers = Physics2D.queriesHitTriggers;
            return f;
        }

        internal static ContactFilter2D FromLayers(int layerMask, float minDepth, float maxDepth)
        {
            ContactFilter2D f = Unfiltered();
            f.SetLayerMask(layerMask);
            if (!float.IsNegativeInfinity(minDepth) || !float.IsPositiveInfinity(maxDepth)) f.SetDepth(minDepth, maxDepth);
            return f;
        }
    }

    public class PhysicsMaterial2D : Object
    {
        public float friction = 0.4f;
        public float bounciness;

        public PhysicsMaterial2D() { }
        public PhysicsMaterial2D(string name) { this.name = name; }
    }

    /// <summary>The geometry under the queries.  A collider is a list of convex pieces in world space: a circle, a polygon, or a segment (two points).
    /// ponytail: an edge's and a box's edgeRadius only grow the shape, and casts march then bisect (a step a quarter of the smaller shape's size)
    /// instead of solving the time of impact.  Upgrade path: Box2D's b2ShapeCast / b2RayCast through pb2.</summary>
    public static class Shapes2D
    {
        public sealed class Piece
        {
            public bool circle;
            public Vector2 c;
            public float r;
            public Vector2[] p;
        }

        static Vector2 World(Vector2 pos, float cos, float sin, Vector3 s, float x, float y)
        {
            float lx = x * s.x, ly = y * s.y;
            return new Vector2(pos.x + lx * cos - ly * sin, pos.y + lx * sin + ly * cos);
        }

        static Piece Poly(params Vector2[] p) { Piece q = new Piece(); q.p = p; return q; }
        static Piece Round(Vector2 c, float r) { Piece q = new Piece(); q.circle = true; q.c = c; q.r = r; return q; }

        public static List<Piece> Box(Vector2 center, Vector2 size, float angle)
        {
            float a = angle * Mathf.Deg2Rad, cos = Mathf.Cos(a), sin = Mathf.Sin(a), hx = size.x * 0.5f, hy = size.y * 0.5f;
            Vector3 one = new Vector3(1f, 1f, 1f);
            return new List<Piece> { Poly(World(center, cos, sin, one, -hx, -hy), World(center, cos, sin, one, hx, -hy),
                                          World(center, cos, sin, one, hx, hy), World(center, cos, sin, one, -hx, hy)) };
        }

        public static List<Piece> Circle(Vector2 center, float radius) { return new List<Piece> { Round(center, radius) }; }

        /// <summary>The collider's pieces where it stands now.</summary>
        public static List<Piece> PiecesOf(Collider2D c)
        {
            Transform t = c.transform;
            Vector3 s = t.lossyScale, p3 = t.position;
            Vector2 pos = new Vector2(p3.x, p3.y);
            float a = Physics2D.AngleOf(t) * Mathf.Deg2Rad, cos = Mathf.Cos(a), sin = Mathf.Sin(a);
            float sx = Mathf.Abs(s.x), sy = Mathf.Abs(s.y), ox = c.offset.x, oy = c.offset.y;
            List<Piece> out_ = new List<Piece>();
            if (c.kind == 1)
            {
                out_.Add(Round(World(pos, cos, sin, s, ox, oy), c.radius * Mathf.Max(sx, sy)));
            }
            else if (c.kind == 0 || c.kind == 2)
            {
                float er = c.kind == 0 ? ((BoxCollider2D)c).edgeRadius : 0f;
                float hx = c.sizeX * 0.5f + (sx > 0f ? er / sx : 0f), hy = c.sizeY * 0.5f + (sy > 0f ? er / sy : 0f);
                if (c.kind == 2)
                {
                    // a capsule: a box between two circles, along its direction
                    float rad = c.vertical ? hx * sx : hy * sy;
                    float half = c.vertical ? hy - rad / Mathf.Max(sy, 1e-6f) : hx - rad / Mathf.Max(sx, 1e-6f);
                    if (half <= 0f)
                    {
                        out_.Add(Round(World(pos, cos, sin, s, ox, oy), rad));
                        return out_;
                    }
                    float ax = c.vertical ? 0f : half, ay = c.vertical ? half : 0f;
                    out_.Add(Round(World(pos, cos, sin, s, ox - ax, oy - ay), rad));
                    out_.Add(Round(World(pos, cos, sin, s, ox + ax, oy + ay), rad));
                    if (c.vertical) hy = half; else hx = half;
                }
                out_.Add(Poly(World(pos, cos, sin, s, ox - hx, oy - hy), World(pos, cos, sin, s, ox + hx, oy - hy),
                              World(pos, cos, sin, s, ox + hx, oy + hy), World(pos, cos, sin, s, ox - hx, oy + hy)));
            }
            else if (c.kind == 3)
            {
                PolygonCollider2D pc = (PolygonCollider2D)c;
                int at = 0;
                foreach (int n in pc.pieces)
                {
                    Vector2[] q = new Vector2[n];
                    for (int i = 0; i < n; i++) q[i] = World(pos, cos, sin, s, pc.pts[at + 2 * i], pc.pts[at + 2 * i + 1]);
                    at += 2 * n;
                    out_.Add(Poly(q));
                }
            }
            else if (c.kind == 4)
            {
                EdgeCollider2D ec = (EdgeCollider2D)c;
                float r = ec.edgeRadius * Mathf.Max(sx, sy);
                for (int i = 0; i + 3 < ec.pts.Count; i += 2)
                {
                    Vector2 a0 = World(pos, cos, sin, s, ec.pts[i] + ox, ec.pts[i + 1] + oy), a1 = World(pos, cos, sin, s, ec.pts[i + 2] + ox, ec.pts[i + 3] + oy);
                    if (r <= 0f) { out_.Add(Poly(a0, a1)); continue; }
                    Vector2 d = (a1 - a0).normalized, nn = new Vector2(-d.y, d.x) * r;
                    out_.Add(Poly(a0 - nn, a1 - nn, a1 + nn, a0 + nn));
                    out_.Add(Round(a0, r));
                    out_.Add(Round(a1, r));
                }
            }
            return out_;
        }

        static void Extent(List<Piece> pieces, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (Piece q in pieces)
            {
                if (q.circle)
                {
                    min = Vector2.Min(min, q.c - new Vector2(q.r, q.r));
                    max = Vector2.Max(max, q.c + new Vector2(q.r, q.r));
                }
                else foreach (Vector2 v in q.p) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
            }
        }

        public static Bounds BoundsOf(Collider2D c)
        {
            Vector2 min, max;
            Extent(PiecesOf(c), out min, out max);
            if (float.IsInfinity(min.x)) { Vector3 p = c.transform.position; return new Bounds(p, Vector3.zero); }
            Vector2 mid = (min + max) * 0.5f;
            return new Bounds(new Vector3(mid.x, mid.y, c.transform.position.z), new Vector3(max.x - min.x, max.y - min.y, 0f));
        }

        static Vector2 Center(Piece q)
        {
            if (q.circle) return q.c;
            Vector2 m = Vector2.zero;
            foreach (Vector2 v in q.p) m += v;
            return m / q.p.Length;
        }

        static void Project(Piece q, Vector2 off, Vector2 axis, out float lo, out float hi)
        {
            if (q.circle)
            {
                float m = Vector2.Dot(q.c + off, axis);
                lo = m - q.r;
                hi = m + q.r;
                return;
            }
            lo = float.PositiveInfinity;
            hi = float.NegativeInfinity;
            foreach (Vector2 v in q.p)
            {
                float d = Vector2.Dot(v + off, axis);
                if (d < lo) lo = d;
                if (d > hi) hi = d;
            }
        }

        static void Axes(Piece q, List<Vector2> axes)
        {
            if (q.circle) return;
            for (int i = 0; i < q.p.Length; i++)
            {
                Vector2 e = q.p[(i + 1) % q.p.Length] - q.p[i];
                if (e.sqrMagnitude > 1e-12f) axes.Add(new Vector2(-e.y, e.x).normalized);
                if (q.p.Length == 2) break;
            }
        }

        static Vector2 Closest(Piece q, Vector2 to)
        {
            Vector2 best = q.p[0];
            foreach (Vector2 v in q.p) if ((v - to).sqrMagnitude < (best - to).sqrMagnitude) best = v;
            return best;
        }

        /// <summary>How far a and b (b moved by off) overlap along the axis where they overlap least: negative when they are apart.  n points from a to b.</summary>
        public static float Separation(Piece a, Piece b, Vector2 off, out Vector2 n)
        {
            Vector2 ca = Center(a), cb = Center(b) + off;
            if (a.circle && b.circle)
            {
                Vector2 d = cb - ca;
                float len = d.magnitude;
                n = len > 1e-6f ? d / len : Vector2.up;
                return a.r + b.r - len;
            }
            List<Vector2> axes = new List<Vector2>();
            Axes(a, axes);
            Axes(b, axes);
            if (a.circle) axes.Add((Closest(b, ca - off) + off - ca).normalized);
            if (b.circle) axes.Add((cb - Closest(a, cb)).normalized);
            float best = float.PositiveInfinity;
            n = Vector2.up;
            foreach (Vector2 ax0 in axes)
            {
                if (ax0.sqrMagnitude < 0.5f) continue;
                Vector2 ax = Vector2.Dot(cb - ca, ax0) < 0f ? -ax0 : ax0;
                float alo, ahi, blo, bhi;
                Project(a, Vector2.zero, ax, out alo, out ahi);
                Project(b, off, ax, out blo, out bhi);
                float o = Mathf.Min(ahi, bhi) - Mathf.Max(alo, blo);
                if (o < best) { best = o; n = ax; }
            }
            return best;
        }

        /// <summary>The most overlapping pair of pieces (b moved by off): the overlap, n from a to b, and the pieces.</summary>
        static float Deepest(List<Piece> a, List<Piece> b, Vector2 off, out Vector2 n, out Piece pa, out Piece pb)
        {
            float best = float.NegativeInfinity;
            n = Vector2.up;
            pa = pb = null;
            foreach (Piece x in a)
                foreach (Piece y in b)
                {
                    Vector2 nn;
                    float d = Separation(x, y, off, out nn);
                    if (d > best) { best = d; n = nn; pa = x; pb = y; }
                }
            return best;
        }

        static Vector2 Support(Piece q, Vector2 dir)
        {
            if (q.circle) return q.c + dir.normalized * q.r;
            Vector2 best = q.p[0];
            foreach (Vector2 v in q.p) if (Vector2.Dot(v, dir) > Vector2.Dot(best, dir)) best = v;
            return best;
        }

        static bool Candidate(Collider2D c, Collider2D self, ContactFilter2D filter)
        {
            if (c == self || !Physics2D.Active(c) || filter.Rejects(c)) return false;
            if (self != null)
            {
                Rigidbody2D rb = self.attachedRigidbody;
                if (rb != null && c.attachedRigidbody == rb) return false;     // a collider does not meet its own body's others
            }
            return true;
        }

        public static ContactPoint2D Contact(Collider2D self, Collider2D other)
        {
            Vector2 n;
            Piece po, ps;
            float depth = Deepest(PiecesOf(other), PiecesOf(self), Vector2.zero, out n, out po, out ps);
            ContactPoint2D cp = new ContactPoint2D();
            cp.normal = n;
            cp.separation = -depth;
            cp.point = ps == null ? (Vector2)self.transform.position : Support(ps, -n);
            cp.collider = other;
            cp.otherCollider = self;
            cp.rigidbody = other.attachedRigidbody;
            cp.otherRigidbody = self.attachedRigidbody;
            Vector2 va = cp.rigidbody == null ? Vector2.zero : cp.rigidbody.velocity, vb = cp.otherRigidbody == null ? Vector2.zero : cp.otherRigidbody.velocity;
            cp.relativeVelocity = va - vb;
            cp.enabled = true;
            return cp;
        }

        public static bool Touching(Collider2D a, Collider2D b)
        {
            if (a == b || !Physics2D.Active(a) || !Physics2D.Active(b) || !Physics2D.Collides(a, b)) return false;
            Vector2 n;
            Piece pa, pb;
            return Deepest(PiecesOf(a), PiecesOf(b), Vector2.zero, out n, out pa, out pb) > -Physics2D.defaultContactOffset;
        }

        public static bool TouchingAny(Collider2D self, ContactFilter2D filter)
        {
            foreach (Collider2D c in Physics2D.colliders.ToArray())
            {
                if (c != self && !filter.Rejects(c) && Touching(self, c)) return true;
            }
            return false;
        }

        public static bool ContainsPoint(Collider2D c, Vector2 point)
        {
            Vector2 n;
            Piece pa, pb;
            return Physics2D.Active(c) && Deepest(PiecesOf(c), Circle(point, 0f), Vector2.zero, out n, out pa, out pb) >= 0f;
        }

        public static List<Collider2D> OverlapShape(List<Piece> shape, ContactFilter2D filter)
        {
            List<Collider2D> found = new List<Collider2D>();
            foreach (Collider2D c in Physics2D.colliders.ToArray())
            {
                if (!Candidate(c, null, filter)) continue;
                Vector2 n;
                Piece pa, pb;
                if (Deepest(PiecesOf(c), shape, Vector2.zero, out n, out pa, out pb) >= 0f) found.Add(c);
            }
            return found;
        }

        public static int Overlap(Collider2D self, ContactFilter2D filter, Collider2D[] array, List<Collider2D> list)
        {
            List<Collider2D> found = new List<Collider2D>();
            if (Physics2D.Active(self))
            {
                List<Piece> mine = PiecesOf(self);
                foreach (Collider2D c in Physics2D.colliders.ToArray())
                {
                    if (!Candidate(c, self, filter)) continue;
                    Vector2 n;
                    Piece pa, pb;
                    if (Deepest(PiecesOf(c), mine, Vector2.zero, out n, out pa, out pb) > 0f) found.Add(c);
                }
            }
            if (list != null) { list.Clear(); list.AddRange(found); return found.Count; }
            return Fill(found, array);
        }

        public static int Fill(List<Collider2D> found, Collider2D[] array)
        {
            int k = Math.Min(found.Count, array.Length);
            for (int i = 0; i < k; i++) array[i] = found[i];
            return k;
        }

        public static int Fill(List<RaycastHit2D> hits, RaycastHit2D[] array, List<RaycastHit2D> list)
        {
            if (list != null) { list.Clear(); list.AddRange(hits); return hits.Count; }
            int k = Math.Min(hits.Count, array.Length);
            for (int i = 0; i < k; i++) array[i] = hits[i];
            return k;
        }

        public static RaycastHit2D First(List<RaycastHit2D> hits) { return hits.Count > 0 ? hits[0] : new RaycastHit2D(); }

        static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

        /// <summary>Where the ray (d a unit vector) first meets the piece within max: t and the surface normal.  An origin inside meets it at 0, the normal against the ray.</summary>
        static bool RayPiece(Piece q, Vector2 o, Vector2 d, float max, out float t, out Vector2 n)
        {
            t = 0f;
            n = -d;
            if (q.circle)
            {
                Vector2 f = o - q.c;
                float b = Vector2.Dot(f, d), cc = Vector2.Dot(f, f) - q.r * q.r;
                if (cc <= 0f) return true;
                float disc = b * b - cc;
                if (disc < 0f) return false;
                t = -b - Mathf.Sqrt(disc);
                if (t < 0f || t > max) return false;
                n = (o + d * t - q.c).normalized;
                return true;
            }
            if (q.p.Length == 2)
            {
                Vector2 s = q.p[1] - q.p[0];
                float den = Cross(d, s);
                if (Mathf.Abs(den) < 1e-9f) return false;
                t = Cross(q.p[0] - o, s) / den;
                float u = Cross(q.p[0] - o, d) / den;
                if (t < 0f || t > max || u < 0f || u > 1f) return false;
                n = new Vector2(-s.y, s.x).normalized;
                if (Vector2.Dot(n, d) > 0f) n = -n;
                return true;
            }
            // Cyrus-Beck: clip the ray against each edge's half plane
            Vector2 mid = Center(q);
            float tE = 0f, tL = max;
            bool entered = false;
            for (int i = 0; i < q.p.Length; i++)
            {
                Vector2 a = q.p[i], e = q.p[(i + 1) % q.p.Length] - a;
                Vector2 nn = new Vector2(e.y, -e.x).normalized;
                if (Vector2.Dot(nn, a - mid) < 0f) nn = -nn;
                float den = Vector2.Dot(nn, d), num = Vector2.Dot(nn, a - o);
                if (Mathf.Abs(den) < 1e-9f)
                {
                    if (num < 0f) return false;
                    continue;
                }
                float tt = num / den;
                if (den < 0f) { if (tt > tE) { tE = tt; n = nn; entered = true; } }
                else if (tt < tL) tL = tt;
                if (tE > tL) return false;
            }
            t = tE;
            if (!entered) n = -d;
            return true;
        }

        static void Sort(List<RaycastHit2D> hits)
        {
            hits.Sort((x, y) => x.CompareTo(y));
        }

        public static List<RaycastHit2D> Ray(Vector2 origin, Vector2 direction, float distance, ContactFilter2D filter)
        {
            List<RaycastHit2D> hits = new List<RaycastHit2D>();
            if (direction.sqrMagnitude < 1e-12f || distance < 0f) return hits;
            Vector2 d = direction.normalized;
            foreach (Collider2D c in Physics2D.colliders.ToArray())
            {
                if (!Candidate(c, null, filter)) continue;
                float best = float.PositiveInfinity;
                Vector2 bn = Vector2.zero;
                foreach (Piece q in PiecesOf(c))
                {
                    float t;
                    Vector2 n;
                    if (RayPiece(q, origin, d, distance, out t, out n) && t < best) { best = t; bn = n; }
                }
                if (float.IsPositiveInfinity(best) || (best == 0f && !Physics2D.queriesStartInColliders) || filter.IsFilteringNormalAngle(bn)) continue;
                RaycastHit2D h = new RaycastHit2D();
                h.collider = c;
                h.distance = best;
                h.point = h.centroid = origin + d * best;
                h.normal = bn;
                h.fraction = float.IsPositiveInfinity(distance) ? 0f : best / Mathf.Max(distance, 1e-9f);
                hits.Add(h);
            }
            Sort(hits);
            return hits;
        }

        /// <summary>The t range in which the shape's box, moving along d, meets the box (min, max); false when never within [0, limit].</summary>
        static bool SweptRange(Vector2 smin, Vector2 smax, Vector2 tmin, Vector2 tmax, Vector2 d, float limit, out float lo, out float hi)
        {
            lo = 0f;
            hi = limit;
            for (int i = 0; i < 2; i++)
            {
                float di = i == 0 ? d.x : d.y, a0 = i == 0 ? smin.x : smin.y, a1 = i == 0 ? smax.x : smax.y, b0 = i == 0 ? tmin.x : tmin.y, b1 = i == 0 ? tmax.x : tmax.y;
                if (Mathf.Abs(di) < 1e-9f)
                {
                    if (a1 < b0 || a0 > b1) return false;
                    continue;
                }
                float t1 = (b0 - a1) / di, t2 = (b1 - a0) / di;
                if (t1 > t2) { float x = t1; t1 = t2; t2 = x; }
                lo = Mathf.Max(lo, t1);
                hi = Mathf.Min(hi, t2);
                if (lo > hi) return false;
            }
            return true;
        }

        static float MinHalf(Vector2 min, Vector2 max) { return 0.5f * Mathf.Min(max.x - min.x, max.y - min.y); }

        /// <summary>The colliders the shape meets moving along direction for distance (self, the collider cast, and its body's others left out).  A shape
        /// that already overlaps one deeper than the contact offset meets it at 0; one resting on it (within the offset) meets it only by moving into it.</summary>
        public static List<RaycastHit2D> Sweep(List<Piece> shape, Collider2D self, Vector2 direction, float distance, ContactFilter2D filter)
        {
            List<RaycastHit2D> hits = new List<RaycastHit2D>();
            Vector2 d = direction.sqrMagnitude < 1e-12f ? Vector2.zero : direction.normalized;
            Vector2 smin, smax;
            Extent(shape, out smin, out smax);
            if (float.IsInfinity(smin.x)) return hits;
            foreach (Collider2D c in Physics2D.colliders.ToArray())
            {
                if (!Candidate(c, self, filter)) continue;
                List<Piece> target = PiecesOf(c);
                Vector2 tmin, tmax, n;
                Piece pa, pb;
                Extent(target, out tmin, out tmax);
                float lo, hi;
                float limit = d == Vector2.zero ? 0f : distance;
                if (!SweptRange(smin, smax, tmin, tmax, d, limit, out lo, out hi)) continue;
                float start = Deepest(target, shape, Vector2.zero, out n, out pa, out pb);
                float hitT = -1f;
                if (start > Physics2D.defaultContactOffset)
                {
                    if (!Physics2D.queriesStartInColliders) continue;
                    hitT = 0f;
                    n = -d;
                }
                else if (d != Vector2.zero)
                {
                    float need = Mathf.Max(0f, start) + 1e-3f;
                    float h = Mathf.Max(0.002f, 0.25f * Mathf.Min(MinHalf(smin, smax), MinHalf(tmin, tmax)));
                    if (h <= 0.002f) h = Mathf.Max(0.002f, 0.25f * Mathf.Max(MinHalf(smin, smax), MinHalf(tmin, tmax)));
                    float prev = lo;
                    for (float t = lo; t <= hi + h; t += h)
                    {
                        float tt = Mathf.Min(t, hi);
                        if (Deepest(target, shape, d * tt, out n, out pa, out pb) > need)
                        {
                            float a = prev, b = tt;
                            for (int k = 0; k < 20; k++)
                            {
                                float m = 0.5f * (a + b);
                                if (Deepest(target, shape, d * m, out n, out pa, out pb) > need) b = m; else a = m;
                            }
                            hitT = b;
                            Deepest(target, shape, d * hitT, out n, out pa, out pb);
                            break;
                        }
                        prev = tt;
                        if (tt >= hi) break;
                    }
                }
                if (hitT < 0f || filter.IsFilteringNormalAngle(n)) continue;
                RaycastHit2D r = new RaycastHit2D();
                r.collider = c;
                r.distance = hitT;
                r.normal = n;
                r.centroid = (smin + smax) * 0.5f + d * hitT;
                r.point = pa == null ? r.centroid : Support(pa, n);
                r.fraction = float.IsPositiveInfinity(distance) || distance <= 0f ? 0f : hitT / distance;
                hits.Add(r);
            }
            Sort(hits);
            return hits;
        }

        public static int CastCollider(Collider2D self, Vector2 direction, ContactFilter2D filter, RaycastHit2D[] array, List<RaycastHit2D> list, float distance)
        {
            List<RaycastHit2D> hits = Physics2D.Active(self) ? Sweep(PiecesOf(self), self, direction, distance, filter) : new List<RaycastHit2D>();
            return Fill(hits, array, list);
        }

        /// <summary>The outline cut into triangles (three points each), by clipping ears.  ponytail: O(n^3), and a self-crossing outline stops where no ear
        /// is left.</summary>
        public static List<Vector2> Triangulate(Vector2[] path)
        {
            List<Vector2> tris = new List<Vector2>();
            List<Vector2> v = new List<Vector2>(path);
            float area = 0f;
            for (int i = 0; i < v.Count; i++) area += Cross(v[i], v[(i + 1) % v.Count]);
            if (area < 0f) v.Reverse();
            while (v.Count > 3)
            {
                bool cut = false;
                for (int i = 0; i < v.Count; i++)
                {
                    Vector2 a = v[(i + v.Count - 1) % v.Count], b = v[i], c = v[(i + 1) % v.Count];
                    if (Cross(b - a, c - b) <= 0f) continue;
                    bool empty = true;
                    for (int j = 0; j < v.Count && empty; j++)
                    {
                        Vector2 p = v[j];
                        if (p == a || p == b || p == c) continue;
                        if (Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f) empty = false;
                    }
                    if (!empty) continue;
                    tris.Add(a);
                    tris.Add(b);
                    tris.Add(c);
                    v.RemoveAt(i);
                    cut = true;
                    break;
                }
                if (!cut) break;
            }
            if (v.Count == 3 && Mathf.Abs(Cross(v[1] - v[0], v[2] - v[1])) > 1e-12f) { tris.Add(v[0]); tris.Add(v[1]); tris.Add(v[2]); }
            return tris;
        }
    }
}
