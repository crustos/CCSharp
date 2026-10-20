// UnityEngine.Rect, RectInt, Bounds, BoundsInt, Plane and LayerMask: Unity's members and meanings.

using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Rect : IEquatable<Rect>
    {
        float m_XMin, m_YMin, m_Width, m_Height;

        public Rect(float x, float y, float width, float height) { m_XMin = x; m_YMin = y; m_Width = width; m_Height = height; }
        public Rect(Vector2 position, Vector2 size) { m_XMin = position.x; m_YMin = position.y; m_Width = size.x; m_Height = size.y; }
        public Rect(Rect source) { m_XMin = source.m_XMin; m_YMin = source.m_YMin; m_Width = source.m_Width; m_Height = source.m_Height; }

        public static Rect zero { get { return new Rect(0f, 0f, 0f, 0f); } }
        public static Rect MinMaxRect(float xmin, float ymin, float xmax, float ymax) { return new Rect(xmin, ymin, xmax - xmin, ymax - ymin); }
        public void Set(float x, float y, float width, float height) { m_XMin = x; m_YMin = y; m_Width = width; m_Height = height; }

        public float x { get { return m_XMin; } set { m_XMin = value; } }
        public float y { get { return m_YMin; } set { m_YMin = value; } }
        public float width { get { return m_Width; } set { m_Width = value; } }
        public float height { get { return m_Height; } set { m_Height = value; } }
        public Vector2 position { get { return new Vector2(m_XMin, m_YMin); } set { m_XMin = value.x; m_YMin = value.y; } }
        public Vector2 size { get { return new Vector2(m_Width, m_Height); } set { m_Width = value.x; m_Height = value.y; } }
        public Vector2 center
        {
            get { return new Vector2(x + m_Width / 2f, y + m_Height / 2f); }
            set { m_XMin = value.x - m_Width / 2f; m_YMin = value.y - m_Height / 2f; }
        }
        public Vector2 min { get { return new Vector2(xMin, yMin); } set { xMin = value.x; yMin = value.y; } }
        public Vector2 max { get { return new Vector2(xMax, yMax); } set { xMax = value.x; yMax = value.y; } }
        /// <summary>Setting xMin keeps xMax where it is (the width changes), as in Unity.</summary>
        public float xMin { get { return m_XMin; } set { float oldxmax = xMax; m_XMin = value; m_Width = oldxmax - m_XMin; } }
        public float yMin { get { return m_YMin; } set { float oldymax = yMax; m_YMin = value; m_Height = oldymax - m_YMin; } }
        public float xMax { get { return m_Width + m_XMin; } set { m_Width = value - m_XMin; } }
        public float yMax { get { return m_Height + m_YMin; } set { m_Height = value - m_YMin; } }

        public bool Contains(Vector2 point) { return point.x >= xMin && point.x < xMax && point.y >= yMin && point.y < yMax; }
        public bool Contains(Vector3 point) { return point.x >= xMin && point.x < xMax && point.y >= yMin && point.y < yMax; }
        public bool Contains(Vector3 point, bool allowInverse)
        {
            if (!allowInverse) return Contains(point);
            bool xAxis = (width < 0f && point.x <= xMin && point.x > xMax) || (width >= 0f && point.x >= xMin && point.x < xMax);
            bool yAxis = (height < 0f && point.y <= yMin && point.y > yMax) || (height >= 0f && point.y >= yMin && point.y < yMax);
            return xAxis && yAxis;
        }

        static Rect OrderMinMax(Rect rect)
        {
            if (rect.xMin > rect.xMax) { float t = rect.xMin; rect.xMin = rect.xMax; rect.xMax = t; }
            if (rect.yMin > rect.yMax) { float t = rect.yMin; rect.yMin = rect.yMax; rect.yMax = t; }
            return rect;
        }
        public bool Overlaps(Rect other) { return other.xMax > xMin && other.xMin < xMax && other.yMax > yMin && other.yMin < yMax; }
        public bool Overlaps(Rect other, bool allowInverse)
        {
            Rect self = this;
            if (allowInverse) { self = OrderMinMax(self); other = OrderMinMax(other); }
            return self.Overlaps(other);
        }

        public static Vector2 NormalizedToPoint(Rect rectangle, Vector2 normalizedRectCoordinates)
        {
            return new Vector2(Mathf.Lerp(rectangle.x, rectangle.xMax, normalizedRectCoordinates.x), Mathf.Lerp(rectangle.y, rectangle.yMax, normalizedRectCoordinates.y));
        }
        public static Vector2 PointToNormalized(Rect rectangle, Vector2 point)
        {
            return new Vector2(Mathf.InverseLerp(rectangle.x, rectangle.xMax, point.x), Mathf.InverseLerp(rectangle.y, rectangle.yMax, point.y));
        }

        public static bool operator ==(Rect a, Rect b) { return a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height; }
        public static bool operator !=(Rect a, Rect b) { return !(a == b); }
        public override bool Equals(object other) { return other is Rect && Equals((Rect)other); }
        public bool Equals(Rect other) { return this == other; }
        public override int GetHashCode() { return x.GetHashCode() ^ (width.GetHashCode() << 2) ^ (y.GetHashCode() >> 2) ^ (height.GetHashCode() >> 1); }
        public override string ToString()
        {
            return "(x:" + x.ToString("F2") + ", y:" + y.ToString("F2") + ", width:" + width.ToString("F2") + ", height:" + height.ToString("F2") + ")";
        }
    }

    public struct RectInt : IEquatable<RectInt>
    {
        int m_XMin, m_YMin, m_Width, m_Height;

        public RectInt(int xMin, int yMin, int width, int height) { m_XMin = xMin; m_YMin = yMin; m_Width = width; m_Height = height; }
        public RectInt(Vector2Int position, Vector2Int size) { m_XMin = position.x; m_YMin = position.y; m_Width = size.x; m_Height = size.y; }

        public int x { get { return m_XMin; } set { m_XMin = value; } }
        public int y { get { return m_YMin; } set { m_YMin = value; } }
        public int width { get { return m_Width; } set { m_Width = value; } }
        public int height { get { return m_Height; } set { m_Height = value; } }
        public Vector2 center { get { return new Vector2(x + m_Width / 2f, y + m_Height / 2f); } }
        public Vector2Int min { get { return new Vector2Int(xMin, yMin); } set { xMin = value.x; yMin = value.y; } }
        public Vector2Int max { get { return new Vector2Int(xMax, yMax); } set { xMax = value.x; yMax = value.y; } }
        public Vector2Int position { get { return new Vector2Int(m_XMin, m_YMin); } set { m_XMin = value.x; m_YMin = value.y; } }
        public Vector2Int size { get { return new Vector2Int(m_Width, m_Height); } set { m_Width = value.x; m_Height = value.y; } }
        public int xMin { get { return Math.Min(m_XMin, m_XMin + m_Width); } set { int oldxmax = xMax; m_XMin = value; m_Width = oldxmax - m_XMin; } }
        public int yMin { get { return Math.Min(m_YMin, m_YMin + m_Height); } set { int oldymax = yMax; m_YMin = value; m_Height = oldymax - m_YMin; } }
        public int xMax { get { return Math.Max(m_XMin, m_XMin + m_Width); } set { m_Width = value - m_XMin; } }
        /// <summary>Every cell inside, x varying fastest (Unity's order).</summary>
        public IEnumerable<Vector2Int> allPositionsWithin
        {
            get
            {
                int x0 = xMin, x1 = xMax, y0 = yMin, y1 = yMax;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++) yield return new Vector2Int(x, y);
            }
        }
        public int yMax { get { return Math.Max(m_YMin, m_YMin + m_Height); } set { m_Height = value - m_YMin; } }

        public void SetMinMax(Vector2Int minPosition, Vector2Int maxPosition) { min = minPosition; max = maxPosition; }
        public void ClampToBounds(RectInt bounds)
        {
            position = new Vector2Int(Math.Max(Math.Min(bounds.xMax, position.x), bounds.xMin), Math.Max(Math.Min(bounds.yMax, position.y), bounds.yMin));
            size = new Vector2Int(Math.Min(bounds.xMax - position.x, size.x), Math.Min(bounds.yMax - position.y, size.y));
        }
        public bool Contains(Vector2Int position) { return position.x >= xMin && position.y >= yMin && position.x < xMax && position.y < yMax; }
        public bool Overlaps(RectInt other) { return other.xMin < xMax && other.xMax > xMin && other.yMin < yMax && other.yMax > yMin; }

        public static bool operator ==(RectInt a, RectInt b) { return a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height; }
        public static bool operator !=(RectInt a, RectInt b) { return !(a == b); }
        public override bool Equals(object other) { return other is RectInt && Equals((RectInt)other); }
        public bool Equals(RectInt other) { return this == other; }
        public override int GetHashCode() { return x ^ (y << 8) ^ (width << 16) ^ (height << 24); }
        public override string ToString() { return "(x:" + x + ", y:" + y + ", width:" + width + ", height:" + height + ")"; }
    }

    public struct Bounds : IEquatable<Bounds>
    {
        Vector3 m_Center, m_Extents;

        public Bounds(Vector3 center, Vector3 size) { m_Center = center; m_Extents = size * 0.5f; }

        public Vector3 center { get { return m_Center; } set { m_Center = value; } }
        public Vector3 size { get { return m_Extents * 2f; } set { m_Extents = value * 0.5f; } }
        public Vector3 extents { get { return m_Extents; } set { m_Extents = value; } }
        public Vector3 min { get { return center - extents; } set { SetMinMax(value, max); } }
        public Vector3 max { get { return center + extents; } set { SetMinMax(min, value); } }

        public void SetMinMax(Vector3 min, Vector3 max) { extents = (max - min) * 0.5f; center = min + extents; }
        public void Encapsulate(Vector3 point) { SetMinMax(Vector3.Min(min, point), Vector3.Max(max, point)); }
        public void Encapsulate(Bounds bounds) { Encapsulate(bounds.center - bounds.extents); Encapsulate(bounds.center + bounds.extents); }
        public void Expand(float amount) { amount *= 0.5f; extents += new Vector3(amount, amount, amount); }
        public void Expand(Vector3 amount) { extents += amount * 0.5f; }
        public bool Intersects(Bounds bounds)
        {
            return min.x <= bounds.max.x && max.x >= bounds.min.x && min.y <= bounds.max.y && max.y >= bounds.min.y && min.z <= bounds.max.z && max.z >= bounds.min.z;
        }
        public bool Contains(Vector3 point)
        {
            Vector3 lo = min, hi = max;
            return point.x >= lo.x && point.x <= hi.x && point.y >= lo.y && point.y <= hi.y && point.z >= lo.z && point.z <= hi.z;
        }
        public Vector3 ClosestPoint(Vector3 point)
        {
            Vector3 lo = min, hi = max;
            return new Vector3(Mathf.Clamp(point.x, lo.x, hi.x), Mathf.Clamp(point.y, lo.y, hi.y), Mathf.Clamp(point.z, lo.z, hi.z));
        }
        public float SqrDistance(Vector3 point) { Vector3 d = ClosestPoint(point) - point; return Vector3.Dot(d, d); }
        public bool IntersectRay(Ray ray) { float distance; return IntersectRay(ray, out distance); }
        /// <summary>The slab test: the distance along the ray to where it enters the box (0 when the origin is inside).</summary>
        public bool IntersectRay(Ray ray, out float distance)
        {
            float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
            Vector3 lo = min, hi = max, o = ray.origin, d = ray.direction;
            for (int i = 0; i < 3; i++)
            {
                float oi = i == 0 ? o.x : i == 1 ? o.y : o.z, di = i == 0 ? d.x : i == 1 ? d.y : d.z;
                float li = i == 0 ? lo.x : i == 1 ? lo.y : lo.z, hiI = i == 0 ? hi.x : i == 1 ? hi.y : hi.z;
                if (Mathf.Abs(di) < 1e-8f) { if (oi < li || oi > hiI) { distance = 0f; return false; } continue; }
                float t1 = (li - oi) / di, t2 = (hiI - oi) / di;
                if (t1 > t2) { float t = t1; t1 = t2; t2 = t; }
                tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) { distance = 0f; return false; }
            }
            if (tmax < 0f) { distance = 0f; return false; }
            distance = tmin > 0f ? tmin : 0f;
            return true;
        }

        public static bool operator ==(Bounds a, Bounds b) { return a.center == b.center && a.extents == b.extents; }
        public static bool operator !=(Bounds a, Bounds b) { return !(a == b); }
        public override bool Equals(object other) { return other is Bounds && Equals((Bounds)other); }
        public bool Equals(Bounds other) { return center.Equals(other.center) && extents.Equals(other.extents); }
        public override int GetHashCode() { return center.GetHashCode() ^ (extents.GetHashCode() << 2); }
        public override string ToString() { return "Center: " + center + ", Extents: " + extents; }
    }

    public struct BoundsInt : IEquatable<BoundsInt>
    {
        Vector3Int m_Position, m_Size;

        public BoundsInt(int xMin, int yMin, int zMin, int sizeX, int sizeY, int sizeZ)
        {
            m_Position = new Vector3Int(xMin, yMin, zMin); m_Size = new Vector3Int(sizeX, sizeY, sizeZ);
        }
        public BoundsInt(Vector3Int position, Vector3Int size) { m_Position = position; m_Size = size; }

        public int x { get { return m_Position.x; } set { m_Position.x = value; } }
        public int y { get { return m_Position.y; } set { m_Position.y = value; } }
        public int z { get { return m_Position.z; } set { m_Position.z = value; } }
        public Vector3 center { get { return new Vector3(x + m_Size.x / 2f, y + m_Size.y / 2f, z + m_Size.z / 2f); } }
        public Vector3Int min { get { return new Vector3Int(xMin, yMin, zMin); } set { xMin = value.x; yMin = value.y; zMin = value.z; } }
        public Vector3Int max { get { return new Vector3Int(xMax, yMax, zMax); } set { xMax = value.x; yMax = value.y; zMax = value.z; } }
        public int xMin { get { return Math.Min(m_Position.x, m_Position.x + m_Size.x); } set { int oldxmax = xMax; m_Position.x = value; m_Size.x = oldxmax - m_Position.x; } }
        public int yMin { get { return Math.Min(m_Position.y, m_Position.y + m_Size.y); } set { int oldymax = yMax; m_Position.y = value; m_Size.y = oldymax - m_Position.y; } }
        public int zMin { get { return Math.Min(m_Position.z, m_Position.z + m_Size.z); } set { int oldzmax = zMax; m_Position.z = value; m_Size.z = oldzmax - m_Position.z; } }
        public int xMax { get { return Math.Max(m_Position.x, m_Position.x + m_Size.x); } set { m_Size.x = value - m_Position.x; } }
        public IEnumerable<Vector3Int> allPositionsWithin
        {
            get
            {
                int x0 = xMin, x1 = xMax, y0 = yMin, y1 = yMax, z0 = zMin, z1 = zMax;
                for (int z = z0; z < z1; z++)
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++) yield return new Vector3Int(x, y, z);
            }
        }
        public int yMax { get { return Math.Max(m_Position.y, m_Position.y + m_Size.y); } set { m_Size.y = value - m_Position.y; } }
        public int zMax { get { return Math.Max(m_Position.z, m_Position.z + m_Size.z); } set { m_Size.z = value - m_Position.z; } }
        public Vector3Int position { get { return m_Position; } set { m_Position = value; } }
        public Vector3Int size { get { return m_Size; } set { m_Size = value; } }

        public void SetMinMax(Vector3Int minPosition, Vector3Int maxPosition) { min = minPosition; max = maxPosition; }
        public bool Contains(Vector3Int position)
        {
            return position.x >= xMin && position.y >= yMin && position.z >= zMin && position.x < xMax && position.y < yMax && position.z < zMax;
        }
        public void ClampToBounds(BoundsInt bounds)
        {
            position = new Vector3Int(Math.Max(Math.Min(bounds.xMax, position.x), bounds.xMin), Math.Max(Math.Min(bounds.yMax, position.y), bounds.yMin),
                Math.Max(Math.Min(bounds.zMax, position.z), bounds.zMin));
            size = new Vector3Int(Math.Min(bounds.xMax - position.x, size.x), Math.Min(bounds.yMax - position.y, size.y), Math.Min(bounds.zMax - position.z, size.z));
        }

        public static bool operator ==(BoundsInt a, BoundsInt b) { return a.m_Position == b.m_Position && a.m_Size == b.m_Size; }
        public static bool operator !=(BoundsInt a, BoundsInt b) { return !(a == b); }
        public override bool Equals(object other) { return other is BoundsInt && Equals((BoundsInt)other); }
        public bool Equals(BoundsInt other) { return this == other; }
        public override int GetHashCode() { return m_Position.GetHashCode() ^ (m_Size.GetHashCode() << 2); }
        public override string ToString() { return "Position: " + m_Position + ", Size: " + m_Size; }
    }

    public struct Ray
    {
        Vector3 m_Origin, m_Direction;

        public Ray(Vector3 origin, Vector3 direction) { m_Origin = origin; m_Direction = direction.normalized; }
        public Vector3 origin { get { return m_Origin; } set { m_Origin = value; } }
        public Vector3 direction { get { return m_Direction; } set { m_Direction = value.normalized; } }
        public Vector3 GetPoint(float distance) { return m_Origin + m_Direction * distance; }
        public override string ToString() { return "Origin: " + m_Origin + ", Dir: " + m_Direction; }
    }

    public struct Plane
    {
        Vector3 m_Normal;
        float m_Distance;

        public Plane(Vector3 inNormal, Vector3 inPoint) { m_Normal = inNormal.normalized; m_Distance = -Vector3.Dot(m_Normal, inPoint); }
        public Plane(Vector3 inNormal, float d) { m_Normal = inNormal.normalized; m_Distance = d; }
        public Plane(Vector3 a, Vector3 b, Vector3 c) { m_Normal = Vector3.Cross(b - a, c - a).normalized; m_Distance = -Vector3.Dot(m_Normal, a); }

        public Vector3 normal { get { return m_Normal; } set { m_Normal = value; } }
        public float distance { get { return m_Distance; } set { m_Distance = value; } }
        public Plane flipped { get { return new Plane(-m_Normal, -m_Distance); } }

        public void SetNormalAndPosition(Vector3 inNormal, Vector3 inPoint) { m_Normal = inNormal.normalized; m_Distance = -Vector3.Dot(m_Normal, inPoint); }
        public void Set3Points(Vector3 a, Vector3 b, Vector3 c) { m_Normal = Vector3.Cross(b - a, c - a).normalized; m_Distance = -Vector3.Dot(m_Normal, a); }
        public void Flip() { m_Normal = -m_Normal; m_Distance = -m_Distance; }
        public void Translate(Vector3 translation) { m_Distance += Vector3.Dot(m_Normal, translation); }
        public float GetDistanceToPoint(Vector3 point) { return Vector3.Dot(m_Normal, point) + m_Distance; }
        public bool GetSide(Vector3 point) { return Vector3.Dot(m_Normal, point) + m_Distance > 0f; }
        public bool SameSide(Vector3 inPt0, Vector3 inPt1)
        {
            float d0 = GetDistanceToPoint(inPt0), d1 = GetDistanceToPoint(inPt1);
            return (d0 > 0f && d1 > 0f) || (d0 <= 0f && d1 <= 0f);
        }
        public Vector3 ClosestPointOnPlane(Vector3 point) { return point - m_Normal * (Vector3.Dot(m_Normal, point) + m_Distance); }
        public bool Raycast(Ray ray, out float enter)
        {
            float vdot = Vector3.Dot(ray.direction, m_Normal);
            float ndot = -Vector3.Dot(ray.origin, m_Normal) - m_Distance;
            if (Mathf.Approximately(vdot, 0f)) { enter = 0f; return false; }
            enter = ndot / vdot;
            return enter > 0f;
        }
        public override string ToString() { return "(normal:" + m_Normal + ", distance:" + m_Distance.ToString("F1") + ")"; }
    }

    /// <summary>A set of layers, one bit each.  The layer names are the project's (ProjectSettings/TagManager.asset): Unity's built-in ones unless the
    /// program sets <see cref="layerNames"/> (the packer writes them).</summary>
    public struct LayerMask
    {
        int m_Mask;

        public static string[] layerNames = { "Default", "TransparentFX", "Ignore Raycast", "", "Water", "UI", "", "", "", "", "", "", "", "", "", "",
                                              "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" };

        public int value { get { return m_Mask; } set { m_Mask = value; } }

        public static implicit operator int(LayerMask mask) { return mask.m_Mask; }
        public static implicit operator LayerMask(int intVal) { LayerMask m; m.m_Mask = intVal; return m; }

        public static string LayerToName(int layer) { return layer >= 0 && layer < 32 ? layerNames[layer] : ""; }
        public static int NameToLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return -1;
            for (int i = 0; i < 32; i++) if (layerNames[i] == layerName) return i;
            return -1;
        }
        public static int GetMask(params string[] layerNames)
        {
            int mask = 0;
            foreach (string n in layerNames)
            {
                int layer = NameToLayer(n);
                if (layer != -1) mask |= 1 << layer;
            }
            return mask;
        }
    }
}
