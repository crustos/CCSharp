// UnityEngine.Random: Unity's generator is xorshift128, seeded once at start (InitState sets it); value and the float Range include both ends,
// the int Range excludes its max (as Unity's do).  The numbers are not Unity's own for a given seed, only of the same kind.

using System;

namespace UnityEngine
{
    public static class Random
    {
        public struct State
        {
            internal uint s0, s1, s2, s3;
        }

        static State st = Seeded(Environment.TickCount);

        static State Seeded(int seed)
        {
            State s;
            s.s0 = (uint)seed;
            s.s1 = s.s0 * 1812433253u + 1u;
            s.s2 = s.s1 * 1812433253u + 1u;
            s.s3 = s.s2 * 1812433253u + 1u;
            return s;
        }

        static uint Next()
        {
            uint t = st.s0 ^ (st.s0 << 11);
            st.s0 = st.s1; st.s1 = st.s2; st.s2 = st.s3;
            st.s3 = st.s3 ^ (st.s3 >> 19) ^ t ^ (t >> 8);
            return st.s3;
        }

        public static void InitState(int seed) { st = Seeded(seed); }
        public static State state { get { return st; } set { st = value; } }

        /// <summary>A float in [0, 1], both ends included.</summary>
        public static float value { get { return (Next() & 0x7FFFFFu) / 8388607f; } }

        /// <summary>A float in [min, max], both ends included.</summary>
        public static float Range(float minInclusive, float maxInclusive) { return minInclusive + (maxInclusive - minInclusive) * value; }

        /// <summary>An int in [min, max): max is excluded (if max is not above min, min is returned).</summary>
        public static int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(Next() % (uint)(maxExclusive - minInclusive));
        }

        public static Vector2 insideUnitCircle
        {
            get
            {
                float a = value * 2f * Mathf.PI;
                float r = Mathf.Sqrt(value);
                return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
        }

        public static Vector3 onUnitSphere
        {
            get
            {
                float z = Range(-1f, 1f);
                float a = value * 2f * Mathf.PI;
                float r = Mathf.Sqrt(1f - z * z);
                return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
            }
        }

        public static Vector3 insideUnitSphere
        {
            get
            {
                Vector3 d = onUnitSphere;
                float r = Mathf.Pow(value, 1f / 3f);
                return new Vector3(d.x * r, d.y * r, d.z * r);
            }
        }

        public static Quaternion rotation
        {
            get
            {
                float u1 = value, u2 = value * 2f * Mathf.PI, u3 = value * 2f * Mathf.PI;
                float a = Mathf.Sqrt(1f - u1), b = Mathf.Sqrt(u1);
                return new Quaternion(a * Mathf.Sin(u2), a * Mathf.Cos(u2), b * Mathf.Sin(u3), b * Mathf.Cos(u3));
            }
        }

        public static Color ColorHSV() { return ColorHSV(0f, 1f, 0f, 1f, 0f, 1f, 1f, 1f); }
        public static Color ColorHSV(float hueMin, float hueMax, float saturationMin, float saturationMax, float valueMin, float valueMax, float alphaMin, float alphaMax)
        {
            float h = Mathf.Lerp(hueMin, hueMax, value), s = Mathf.Lerp(saturationMin, saturationMax, value), v = Mathf.Lerp(valueMin, valueMax, value);
            float i = Mathf.Floor(h * 6f), f = h * 6f - i;
            float p = v * (1f - s), q = v * (1f - f * s), t = v * (1f - (1f - f) * s);
            int k = ((int)i % 6 + 6) % 6;
            float r = k == 0 ? v : k == 1 ? q : k == 2 ? p : k == 3 ? p : k == 4 ? t : v;
            float g = k == 0 ? t : k == 1 ? v : k == 2 ? v : k == 3 ? q : k == 4 ? p : p;
            float bl = k == 0 ? p : k == 1 ? p : k == 2 ? t : k == 3 ? v : k == 4 ? v : q;
            return new Color(r, g, bl, Mathf.Lerp(alphaMin, alphaMax, value));
        }
    }
}
