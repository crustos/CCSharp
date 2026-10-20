// UnityEngine.Matrix4x4: column-major like Unity's (m<row><column>; this[i] counts down the columns).

using System;

namespace UnityEngine
{
    public struct Matrix4x4 : IEquatable<Matrix4x4>
    {
        public float m00, m10, m20, m30;
        public float m01, m11, m21, m31;
        public float m02, m12, m22, m32;
        public float m03, m13, m23, m33;

        public Matrix4x4(Vector4 column0, Vector4 column1, Vector4 column2, Vector4 column3)
        {
            m00 = column0.x; m01 = column1.x; m02 = column2.x; m03 = column3.x;
            m10 = column0.y; m11 = column1.y; m12 = column2.y; m13 = column3.y;
            m20 = column0.z; m21 = column1.z; m22 = column2.z; m23 = column3.z;
            m30 = column0.w; m31 = column1.w; m32 = column2.w; m33 = column3.w;
        }

        public float this[int row, int column]
        {
            get { return this[row + column * 4]; }
            set { this[row + column * 4] = value; }
        }

        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return m00; case 1: return m10; case 2: return m20; case 3: return m30;
                    case 4: return m01; case 5: return m11; case 6: return m21; case 7: return m31;
                    case 8: return m02; case 9: return m12; case 10: return m22; case 11: return m32;
                    case 12: return m03; case 13: return m13; case 14: return m23; case 15: return m33;
                    default: throw new IndexOutOfRangeException("Invalid matrix index!");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: m00 = value; break; case 1: m10 = value; break; case 2: m20 = value; break; case 3: m30 = value; break;
                    case 4: m01 = value; break; case 5: m11 = value; break; case 6: m21 = value; break; case 7: m31 = value; break;
                    case 8: m02 = value; break; case 9: m12 = value; break; case 10: m22 = value; break; case 11: m32 = value; break;
                    case 12: m03 = value; break; case 13: m13 = value; break; case 14: m23 = value; break; case 15: m33 = value; break;
                    default: throw new IndexOutOfRangeException("Invalid matrix index!");
                }
            }
        }

        public static Matrix4x4 zero { get { return new Matrix4x4(); } }
        public static Matrix4x4 identity
        {
            get
            {
                Matrix4x4 m = new Matrix4x4();
                m.m00 = 1f; m.m11 = 1f; m.m22 = 1f; m.m33 = 1f;
                return m;
            }
        }

        public Vector4 GetColumn(int index) { return new Vector4(this[0, index], this[1, index], this[2, index], this[3, index]); }
        public Vector4 GetRow(int index) { return new Vector4(this[index, 0], this[index, 1], this[index, 2], this[index, 3]); }
        public void SetColumn(int index, Vector4 c) { this[0, index] = c.x; this[1, index] = c.y; this[2, index] = c.z; this[3, index] = c.w; }
        public void SetRow(int index, Vector4 r) { this[index, 0] = r.x; this[index, 1] = r.y; this[index, 2] = r.z; this[index, 3] = r.w; }

        public static Matrix4x4 Translate(Vector3 v)
        {
            Matrix4x4 m = identity;
            m.m03 = v.x; m.m13 = v.y; m.m23 = v.z;
            return m;
        }

        public static Matrix4x4 Scale(Vector3 v)
        {
            Matrix4x4 m = new Matrix4x4();
            m.m00 = v.x; m.m11 = v.y; m.m22 = v.z; m.m33 = 1f;
            return m;
        }

        public static Matrix4x4 Rotate(Quaternion q)
        {
            float x = q.x * 2f, y = q.y * 2f, z = q.z * 2f;
            float xx = q.x * x, yy = q.y * y, zz = q.z * z, xy = q.x * y, xz = q.x * z, yz = q.y * z, wx = q.w * x, wy = q.w * y, wz = q.w * z;
            Matrix4x4 m;
            m.m00 = 1f - (yy + zz); m.m10 = xy + wz; m.m20 = xz - wy; m.m30 = 0f;
            m.m01 = xy - wz; m.m11 = 1f - (xx + zz); m.m21 = yz + wx; m.m31 = 0f;
            m.m02 = xz + wy; m.m12 = yz - wx; m.m22 = 1f - (xx + yy); m.m32 = 0f;
            m.m03 = 0f; m.m13 = 0f; m.m23 = 0f; m.m33 = 1f;
            return m;
        }

        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s) { return Translate(pos) * Rotate(q) * Scale(s); }

        public void SetTRS(Vector3 pos, Quaternion q, Vector3 s) { this = TRS(pos, q, s); }

        public static Matrix4x4 Ortho(float left, float right, float bottom, float top, float zNear, float zFar)
        {
            Matrix4x4 m = identity;
            m.m00 = 2f / (right - left); m.m11 = 2f / (top - bottom); m.m22 = -2f / (zFar - zNear);
            m.m03 = -(right + left) / (right - left); m.m13 = -(top + bottom) / (top - bottom); m.m23 = -(zFar + zNear) / (zFar - zNear);
            return m;
        }

        public Matrix4x4 transpose
        {
            get
            {
                Matrix4x4 t;
                t.m00 = m00; t.m01 = m10; t.m02 = m20; t.m03 = m30;
                t.m10 = m01; t.m11 = m11; t.m12 = m21; t.m13 = m31;
                t.m20 = m02; t.m21 = m12; t.m22 = m22; t.m23 = m32;
                t.m30 = m03; t.m31 = m13; t.m32 = m23; t.m33 = m33;
                return t;
            }
        }

        public float determinant
        {
            get
            {
                float s0 = m00 * m11 - m10 * m01, s1 = m00 * m12 - m10 * m02, s2 = m00 * m13 - m10 * m03;
                float s3 = m01 * m12 - m11 * m02, s4 = m01 * m13 - m11 * m03, s5 = m02 * m13 - m12 * m03;
                float c5 = m22 * m33 - m32 * m23, c4 = m21 * m33 - m31 * m23, c3 = m21 * m32 - m31 * m22;
                float c2 = m20 * m33 - m30 * m23, c1 = m20 * m32 - m30 * m22, c0 = m20 * m31 - m30 * m21;
                return s0 * c5 - s1 * c4 + s2 * c3 + s3 * c2 - s4 * c1 + s5 * c0;
            }
        }

        /// <summary>The inverse (all zeros when the matrix has none).</summary>
        public Matrix4x4 inverse
        {
            get
            {
                float s0 = m00 * m11 - m10 * m01, s1 = m00 * m12 - m10 * m02, s2 = m00 * m13 - m10 * m03;
                float s3 = m01 * m12 - m11 * m02, s4 = m01 * m13 - m11 * m03, s5 = m02 * m13 - m12 * m03;
                float c5 = m22 * m33 - m32 * m23, c4 = m21 * m33 - m31 * m23, c3 = m21 * m32 - m31 * m22;
                float c2 = m20 * m33 - m30 * m23, c1 = m20 * m32 - m30 * m22, c0 = m20 * m31 - m30 * m21;
                float det = s0 * c5 - s1 * c4 + s2 * c3 + s3 * c2 - s4 * c1 + s5 * c0;
                if (det == 0f) return zero;
                float inv = 1f / det;
                Matrix4x4 r;
                r.m00 = (m11 * c5 - m12 * c4 + m13 * c3) * inv;
                r.m01 = (-m01 * c5 + m02 * c4 - m03 * c3) * inv;
                r.m02 = (m31 * s5 - m32 * s4 + m33 * s3) * inv;
                r.m03 = (-m21 * s5 + m22 * s4 - m23 * s3) * inv;
                r.m10 = (-m10 * c5 + m12 * c2 - m13 * c1) * inv;
                r.m11 = (m00 * c5 - m02 * c2 + m03 * c1) * inv;
                r.m12 = (-m30 * s5 + m32 * s2 - m33 * s1) * inv;
                r.m13 = (m20 * s5 - m22 * s2 + m23 * s1) * inv;
                r.m20 = (m10 * c4 - m11 * c2 + m13 * c0) * inv;
                r.m21 = (-m00 * c4 + m01 * c2 - m03 * c0) * inv;
                r.m22 = (m30 * s4 - m31 * s2 + m33 * s0) * inv;
                r.m23 = (-m20 * s4 + m21 * s2 - m23 * s0) * inv;
                r.m30 = (-m10 * c3 + m11 * c1 - m12 * c0) * inv;
                r.m31 = (m00 * c3 - m01 * c1 + m02 * c0) * inv;
                r.m32 = (-m30 * s3 + m31 * s1 - m32 * s0) * inv;
                r.m33 = (m20 * s3 - m21 * s1 + m22 * s0) * inv;
                return r;
            }
        }

        public Vector3 MultiplyPoint(Vector3 p)
        {
            float x = m00 * p.x + m01 * p.y + m02 * p.z + m03;
            float y = m10 * p.x + m11 * p.y + m12 * p.z + m13;
            float z = m20 * p.x + m21 * p.y + m22 * p.z + m23;
            float w = m30 * p.x + m31 * p.y + m32 * p.z + m33;
            w = 1f / w;
            return new Vector3(x * w, y * w, z * w);
        }

        public Vector3 MultiplyPoint3x4(Vector3 p)
        {
            return new Vector3(m00 * p.x + m01 * p.y + m02 * p.z + m03, m10 * p.x + m11 * p.y + m12 * p.z + m13, m20 * p.x + m21 * p.y + m22 * p.z + m23);
        }

        public Vector3 MultiplyVector(Vector3 v)
        {
            return new Vector3(m00 * v.x + m01 * v.y + m02 * v.z, m10 * v.x + m11 * v.y + m12 * v.z, m20 * v.x + m21 * v.y + m22 * v.z);
        }

        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            Matrix4x4 r;
            r.m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20 + a.m03 * b.m30;
            r.m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21 + a.m03 * b.m31;
            r.m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22 + a.m03 * b.m32;
            r.m03 = a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03 * b.m33;
            r.m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20 + a.m13 * b.m30;
            r.m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21 + a.m13 * b.m31;
            r.m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22 + a.m13 * b.m32;
            r.m13 = a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13 * b.m33;
            r.m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20 + a.m23 * b.m30;
            r.m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21 + a.m23 * b.m31;
            r.m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22 + a.m23 * b.m32;
            r.m23 = a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23 * b.m33;
            r.m30 = a.m30 * b.m00 + a.m31 * b.m10 + a.m32 * b.m20 + a.m33 * b.m30;
            r.m31 = a.m30 * b.m01 + a.m31 * b.m11 + a.m32 * b.m21 + a.m33 * b.m31;
            r.m32 = a.m30 * b.m02 + a.m31 * b.m12 + a.m32 * b.m22 + a.m33 * b.m32;
            r.m33 = a.m30 * b.m03 + a.m31 * b.m13 + a.m32 * b.m23 + a.m33 * b.m33;
            return r;
        }

        public static Vector4 operator *(Matrix4x4 m, Vector4 v)
        {
            return new Vector4(m.m00 * v.x + m.m01 * v.y + m.m02 * v.z + m.m03 * v.w, m.m10 * v.x + m.m11 * v.y + m.m12 * v.z + m.m13 * v.w,
                m.m20 * v.x + m.m21 * v.y + m.m22 * v.z + m.m23 * v.w, m.m30 * v.x + m.m31 * v.y + m.m32 * v.z + m.m33 * v.w);
        }

        public static bool operator ==(Matrix4x4 a, Matrix4x4 b) { return a.Equals(b); }
        public static bool operator !=(Matrix4x4 a, Matrix4x4 b) { return !a.Equals(b); }
        public override bool Equals(object other) { return other is Matrix4x4 && Equals((Matrix4x4)other); }
        public bool Equals(Matrix4x4 o)
        {
            for (int i = 0; i < 16; i++) if (this[i] != o[i]) return false;
            return true;
        }
        public override int GetHashCode() { return GetColumn(0).GetHashCode() ^ (GetColumn(1).GetHashCode() << 2) ^ (GetColumn(2).GetHashCode() >> 2) ^ (GetColumn(3).GetHashCode() >> 1); }
    }
}
