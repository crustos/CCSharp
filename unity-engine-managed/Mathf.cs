// UnityEngine.Mathf: Unity's float math, with Unity's meanings where they differ from System.Math (Sign(0) is 1, Round rounds half to even, Lerp
// clamps t, Repeat and PingPong wrap negative values the way Unity does).

using System;

namespace UnityEngine
{
    public static class Mathf
    {
        public const float PI = 3.14159274f;
        public const float Infinity = float.PositiveInfinity;
        public const float NegativeInfinity = float.NegativeInfinity;
        public const float Deg2Rad = PI * 2f / 360f;
        public const float Rad2Deg = 1f / Deg2Rad;
        public static readonly float Epsilon = float.Epsilon;

        public static float Sin(float f) { return (float)Math.Sin(f); }
        public static float Cos(float f) { return (float)Math.Cos(f); }
        public static float Tan(float f) { return (float)Math.Tan(f); }
        public static float Asin(float f) { return (float)Math.Asin(f); }
        public static float Acos(float f) { return (float)Math.Acos(f); }
        public static float Atan(float f) { return (float)Math.Atan(f); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Sqrt(float f) { return (float)Math.Sqrt(f); }
        public static float Abs(float f) { return f < 0f ? -f : f; }
        public static int Abs(int value) { return value < 0 ? -value : value; }
        public static float Pow(float f, float p) { return (float)Math.Pow(f, p); }
        public static float Exp(float power) { return (float)Math.Exp(power); }
        public static float Log(float f) { return (float)Math.Log(f); }
        public static float Log(float f, float p) { return (float)(Math.Log(f) / Math.Log(p)); }
        public static float Log10(float f) { return (float)Math.Log10(f); }

        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Min(params float[] values)
        {
            if (values.Length == 0) return 0f;
            float m = values[0];
            for (int i = 1; i < values.Length; i++) if (values[i] < m) m = values[i];
            return m;
        }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static int Min(params int[] values)
        {
            if (values.Length == 0) return 0;
            int m = values[0];
            for (int i = 1; i < values.Length; i++) if (values[i] < m) m = values[i];
            return m;
        }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Max(params float[] values)
        {
            if (values.Length == 0) return 0f;
            float m = values[0];
            for (int i = 1; i < values.Length; i++) if (values[i] > m) m = values[i];
            return m;
        }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static int Max(params int[] values)
        {
            if (values.Length == 0) return 0;
            int m = values[0];
            for (int i = 1; i < values.Length; i++) if (values[i] > m) m = values[i];
            return m;
        }

        public static float Ceil(float f) { return (float)Math.Ceiling(f); }
        public static float Floor(float f) { return (float)Math.Floor(f); }
        /// <summary>Half to even, as System.Math.Round (and Unity): Round(2.5) is 2, Round(3.5) is 4.</summary>
        public static float Round(float f)
        {
            double r = Math.Floor(f + 0.5);
            if (r - f == 0.5 && r % 2.0 != 0.0) r -= 1.0;
            return (float)r;
        }
        public static int CeilToInt(float f) { return (int)Math.Ceiling(f); }
        public static int FloorToInt(float f) { return (int)Math.Floor(f); }
        public static int RoundToInt(float f) { return (int)Round(f); }
        /// <summary>1 for zero and above, -1 below (Unity's, not Math.Sign's 0).</summary>
        public static float Sign(float f) { return f >= 0f ? 1f : -1f; }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) value = min;
            else if (value > max) value = max;
            return value;
        }
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) value = min;
            else if (value > max) value = max;
            return value;
        }
        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float LerpUnclamped(float a, float b, float t) { return a + (b - a) * t; }
        public static float LerpAngle(float a, float b, float t)
        {
            float delta = Repeat(b - a, 360f);
            if (delta > 180f) delta -= 360f;
            return a + delta * Clamp01(t);
        }
        public static float InverseLerp(float a, float b, float value) { return a != b ? Clamp01((value - a) / (b - a)) : 0f; }
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Abs(target - current) <= maxDelta) return target;
            return current + Sign(target - current) * maxDelta;
        }
        public static float MoveTowardsAngle(float current, float target, float maxDelta)
        {
            float deltaAngle = DeltaAngle(current, target);
            if (-maxDelta < deltaAngle && deltaAngle < maxDelta) return target;
            target = current + deltaAngle;
            return MoveTowards(current, target, maxDelta);
        }
        public static float SmoothStep(float from, float to, float t)
        {
            t = Clamp01(t);
            t = -2f * t * t * t + 3f * t * t;
            return to * t + from * (1f - t);
        }
        public static bool Approximately(float a, float b)
        {
            return Abs(b - a) < Max(0.000001f * Max(Abs(a), Abs(b)), Epsilon * 8f);
        }

        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime)
        {
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, Infinity, Time.deltaTime);
        }
        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed)
        {
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, Time.deltaTime);
        }
        /// <summary>Unity's critically damped spring (Game Programming Gems 4, 1.10).</summary>
        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            smoothTime = Max(0.0001f, smoothTime);
            float omega = 2f / smoothTime;
            float x = omega * deltaTime;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            float change = current - target;
            float originalTo = target;
            float maxChange = maxSpeed * smoothTime;
            change = Clamp(change, -maxChange, maxChange);
            target = current - change;
            float temp = (currentVelocity + omega * change) * deltaTime;
            currentVelocity = (currentVelocity - omega * temp) * exp;
            float output = target + (change + temp) * exp;
            if (originalTo - current > 0f == output > originalTo)
            {
                output = originalTo;
                currentVelocity = (output - originalTo) / deltaTime;
            }
            return output;
        }
        public static float SmoothDampAngle(float current, float target, ref float currentVelocity, float smoothTime)
        {
            return SmoothDampAngle(current, target, ref currentVelocity, smoothTime, Infinity, Time.deltaTime);
        }
        public static float SmoothDampAngle(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed)
        {
            return SmoothDampAngle(current, target, ref currentVelocity, smoothTime, maxSpeed, Time.deltaTime);
        }
        public static float SmoothDampAngle(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            target = current + DeltaAngle(current, target);
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        /// <summary>t wrapped into [0, length): Repeat(-1, 3) is 2.</summary>
        public static float Repeat(float t, float length) { return Clamp(t - Floor(t / length) * length, 0f, length); }
        public static float PingPong(float t, float length)
        {
            t = Repeat(t, length * 2f);
            return length - Abs(t - length);
        }
        /// <summary>The shortest difference between two angles in degrees, in [-180, 180].</summary>
        public static float DeltaAngle(float current, float target)
        {
            float delta = Repeat(target - current, 360f);
            if (delta > 180f) delta -= 360f;
            return delta;
        }

        public static bool IsPowerOfTwo(int value) { return value > 0 && (value & (value - 1)) == 0; }
        public static int NextPowerOfTwo(int value)
        {
            if (value <= 0) return 0;
            value--;
            value |= value >> 1; value |= value >> 2; value |= value >> 4; value |= value >> 8; value |= value >> 16;
            return value + 1;
        }
        public static int ClosestPowerOfTwo(int value)
        {
            int next = NextPowerOfTwo(value);
            int prev = next >> 1;
            return value - prev < next - value ? prev : next;
        }
    }
}
