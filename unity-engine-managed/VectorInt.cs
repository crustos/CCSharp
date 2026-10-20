// UnityEngine.Vector2Int, Vector3Int and Vector4: Unity's members, operators and conversions.

using System;

namespace UnityEngine
{
    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;

        public Vector2Int(int x, int y) { this.x = x; this.y = y; }

        public int this[int index]
        {
            get { if (index == 0) return x; if (index == 1) return y; throw new IndexOutOfRangeException("Invalid Vector2Int index " + index); }
            set { if (index == 0) x = value; else if (index == 1) y = value; else throw new IndexOutOfRangeException("Invalid Vector2Int index " + index); }
        }

        public static Vector2Int zero { get { return new Vector2Int(0, 0); } }
        public static Vector2Int one { get { return new Vector2Int(1, 1); } }
        public static Vector2Int up { get { return new Vector2Int(0, 1); } }
        public static Vector2Int down { get { return new Vector2Int(0, -1); } }
        public static Vector2Int left { get { return new Vector2Int(-1, 0); } }
        public static Vector2Int right { get { return new Vector2Int(1, 0); } }

        public float magnitude { get { return Mathf.Sqrt(x * x + y * y); } }
        public int sqrMagnitude { get { return x * x + y * y; } }

        public void Set(int x, int y) { this.x = x; this.y = y; }
        public void Clamp(Vector2Int min, Vector2Int max)
        {
            x = Math.Max(min.x, x); x = Math.Min(max.x, x);
            y = Math.Max(min.y, y); y = Math.Min(max.y, y);
        }

        public static float Distance(Vector2Int a, Vector2Int b) { float dx = a.x - b.x, dy = a.y - b.y; return Mathf.Sqrt(dx * dx + dy * dy); }
        public static Vector2Int Min(Vector2Int a, Vector2Int b) { return new Vector2Int(Math.Min(a.x, b.x), Math.Min(a.y, b.y)); }
        public static Vector2Int Max(Vector2Int a, Vector2Int b) { return new Vector2Int(Math.Max(a.x, b.x), Math.Max(a.y, b.y)); }
        public static Vector2Int Scale(Vector2Int a, Vector2Int b) { return new Vector2Int(a.x * b.x, a.y * b.y); }
        public void Scale(Vector2Int scale) { x *= scale.x; y *= scale.y; }
        public static Vector2Int FloorToInt(Vector2 v) { return new Vector2Int(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y)); }
        public static Vector2Int CeilToInt(Vector2 v) { return new Vector2Int(Mathf.CeilToInt(v.x), Mathf.CeilToInt(v.y)); }
        public static Vector2Int RoundToInt(Vector2 v) { return new Vector2Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y)); }

        public static implicit operator Vector2(Vector2Int v) { return new Vector2(v.x, v.y); }
        public static explicit operator Vector3Int(Vector2Int v) { return new Vector3Int(v.x, v.y, 0); }

        public static Vector2Int operator -(Vector2Int v) { return new Vector2Int(-v.x, -v.y); }
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) { return new Vector2Int(a.x + b.x, a.y + b.y); }
        public static Vector2Int operator -(Vector2Int a, Vector2Int b) { return new Vector2Int(a.x - b.x, a.y - b.y); }
        public static Vector2Int operator *(Vector2Int a, Vector2Int b) { return new Vector2Int(a.x * b.x, a.y * b.y); }
        public static Vector2Int operator *(int a, Vector2Int b) { return new Vector2Int(a * b.x, a * b.y); }
        public static Vector2Int operator *(Vector2Int a, int b) { return new Vector2Int(a.x * b, a.y * b); }
        public static Vector2Int operator /(Vector2Int a, int b) { return new Vector2Int(a.x / b, a.y / b); }
        public static bool operator ==(Vector2Int a, Vector2Int b) { return a.x == b.x && a.y == b.y; }
        public static bool operator !=(Vector2Int a, Vector2Int b) { return !(a == b); }

        public override bool Equals(object other) { return other is Vector2Int && Equals((Vector2Int)other); }
        public bool Equals(Vector2Int other) { return x == other.x && y == other.y; }
        public override int GetHashCode() { return x ^ (y << 2); }
        public override string ToString() { return "(" + x + ", " + y + ")"; }
    }

    public struct Vector3Int : IEquatable<Vector3Int>
    {
        public int x, y, z;

        public Vector3Int(int x, int y) { this.x = x; this.y = y; z = 0; }
        public Vector3Int(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }

        public int this[int index]
        {
            get
            {
                if (index == 0) return x; if (index == 1) return y; if (index == 2) return z;
                throw new IndexOutOfRangeException("Invalid Vector3Int index " + index);
            }
            set
            {
                if (index == 0) x = value; else if (index == 1) y = value; else if (index == 2) z = value;
                else throw new IndexOutOfRangeException("Invalid Vector3Int index " + index);
            }
        }

        public static Vector3Int zero { get { return new Vector3Int(0, 0, 0); } }
        public static Vector3Int one { get { return new Vector3Int(1, 1, 1); } }
        public static Vector3Int up { get { return new Vector3Int(0, 1, 0); } }
        public static Vector3Int down { get { return new Vector3Int(0, -1, 0); } }
        public static Vector3Int left { get { return new Vector3Int(-1, 0, 0); } }
        public static Vector3Int right { get { return new Vector3Int(1, 0, 0); } }
        public static Vector3Int forward { get { return new Vector3Int(0, 0, 1); } }
        public static Vector3Int back { get { return new Vector3Int(0, 0, -1); } }

        public float magnitude { get { return Mathf.Sqrt(x * x + y * y + z * z); } }
        public int sqrMagnitude { get { return x * x + y * y + z * z; } }

        public void Set(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
        public void Clamp(Vector3Int min, Vector3Int max)
        {
            x = Math.Min(max.x, Math.Max(min.x, x));
            y = Math.Min(max.y, Math.Max(min.y, y));
            z = Math.Min(max.z, Math.Max(min.z, z));
        }

        public static float Distance(Vector3Int a, Vector3Int b) { return (a - b).magnitude; }
        public static Vector3Int Min(Vector3Int a, Vector3Int b) { return new Vector3Int(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z)); }
        public static Vector3Int Max(Vector3Int a, Vector3Int b) { return new Vector3Int(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z)); }
        public static Vector3Int Scale(Vector3Int a, Vector3Int b) { return new Vector3Int(a.x * b.x, a.y * b.y, a.z * b.z); }
        public void Scale(Vector3Int scale) { x *= scale.x; y *= scale.y; z *= scale.z; }
        public static Vector3Int FloorToInt(Vector3 v) { return new Vector3Int(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z)); }
        public static Vector3Int CeilToInt(Vector3 v) { return new Vector3Int(Mathf.CeilToInt(v.x), Mathf.CeilToInt(v.y), Mathf.CeilToInt(v.z)); }
        public static Vector3Int RoundToInt(Vector3 v) { return new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z)); }

        public static implicit operator Vector3(Vector3Int v) { return new Vector3(v.x, v.y, v.z); }
        public static explicit operator Vector2Int(Vector3Int v) { return new Vector2Int(v.x, v.y); }

        public static Vector3Int operator -(Vector3Int v) { return new Vector3Int(-v.x, -v.y, -v.z); }
        public static Vector3Int operator +(Vector3Int a, Vector3Int b) { return new Vector3Int(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3Int operator -(Vector3Int a, Vector3Int b) { return new Vector3Int(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3Int operator *(Vector3Int a, Vector3Int b) { return new Vector3Int(a.x * b.x, a.y * b.y, a.z * b.z); }
        public static Vector3Int operator *(int a, Vector3Int b) { return new Vector3Int(a * b.x, a * b.y, a * b.z); }
        public static Vector3Int operator *(Vector3Int a, int b) { return new Vector3Int(a.x * b, a.y * b, a.z * b); }
        public static Vector3Int operator /(Vector3Int a, int b) { return new Vector3Int(a.x / b, a.y / b, a.z / b); }
        public static bool operator ==(Vector3Int a, Vector3Int b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Vector3Int a, Vector3Int b) { return !(a == b); }

        public override bool Equals(object other) { return other is Vector3Int && Equals((Vector3Int)other); }
        public bool Equals(Vector3Int other) { return this == other; }
        public override int GetHashCode() { return x ^ (y << 4) ^ (y >> 28) ^ (z >> 4) ^ (z << 28); }
        public override string ToString() { return "(" + x + ", " + y + ", " + z + ")"; }
    }

    public struct Vector4 : IEquatable<Vector4>
    {
        public static implicit operator Vector4(Color c) { return new Vector4(c.r, c.g, c.b, c.a); }
        public static implicit operator Color(Vector4 v) { return new Color(v.x, v.y, v.z, v.w); }
        public float x, y, z, w;

        public Vector4(float x, float y) { this.x = x; this.y = y; z = 0f; w = 0f; }
        public Vector4(float x, float y, float z) { this.x = x; this.y = y; this.z = z; w = 0f; }
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public float this[int index]
        {
            get
            {
                if (index == 0) return x; if (index == 1) return y; if (index == 2) return z; if (index == 3) return w;
                throw new IndexOutOfRangeException("Invalid Vector4 index!");
            }
            set
            {
                if (index == 0) x = value; else if (index == 1) y = value; else if (index == 2) z = value; else if (index == 3) w = value;
                else throw new IndexOutOfRangeException("Invalid Vector4 index!");
            }
        }

        public static Vector4 zero { get { return new Vector4(0f, 0f, 0f, 0f); } }
        public static Vector4 one { get { return new Vector4(1f, 1f, 1f, 1f); } }
        public float sqrMagnitude { get { return Dot(this, this); } }
        public float magnitude { get { return Mathf.Sqrt(Dot(this, this)); } }
        public Vector4 normalized { get { float m = magnitude; return m > 0.00001f ? this / m : zero; } }

        public void Set(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static float Dot(Vector4 a, Vector4 b) { return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w; }
        public static Vector4 Lerp(Vector4 a, Vector4 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector4 Scale(Vector4 a, Vector4 b) { return new Vector4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w); }
        public static float Distance(Vector4 a, Vector4 b) { return (a - b).magnitude; }

        public static implicit operator Vector4(Vector3 v) { return new Vector4(v.x, v.y, v.z, 0f); }
        public static implicit operator Vector3(Vector4 v) { return new Vector3(v.x, v.y, v.z); }
        public static implicit operator Vector4(Vector2 v) { return new Vector4(v.x, v.y, 0f, 0f); }
        public static implicit operator Vector2(Vector4 v) { return new Vector2(v.x, v.y); }

        public static Vector4 operator -(Vector4 v) { return new Vector4(-v.x, -v.y, -v.z, -v.w); }
        public static Vector4 operator +(Vector4 a, Vector4 b) { return new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w); }
        public static Vector4 operator -(Vector4 a, Vector4 b) { return new Vector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w); }
        public static Vector4 operator *(Vector4 a, float d) { return new Vector4(a.x * d, a.y * d, a.z * d, a.w * d); }
        public static Vector4 operator *(float d, Vector4 a) { return a * d; }
        public static Vector4 operator /(Vector4 a, float d) { return new Vector4(a.x / d, a.y / d, a.z / d, a.w / d); }
        public static bool operator ==(Vector4 a, Vector4 b) { return (a - b).sqrMagnitude < 9.99999944E-11f; }
        public static bool operator !=(Vector4 a, Vector4 b) { return !(a == b); }

        public override bool Equals(object other) { return other is Vector4 && Equals((Vector4)other); }
        public bool Equals(Vector4 o) { return x == o.x && y == o.y && z == o.z && w == o.w; }
        public override int GetHashCode() { return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1); }
        public override string ToString() { return "(" + x.ToString("F2") + ", " + y.ToString("F2") + ", " + z.ToString("F2") + ", " + w.ToString("F2") + ")"; }
    }
}
