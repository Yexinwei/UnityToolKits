// UnityEngine 最小桩，仅供 tools/verify 在无 Unity 环境下编译运行 Runtime 代码。
// 签名与 Unity 保持一致；只实现 Runtime 用到的成员。不要放进包里。
using System;

namespace UnityEngine
{
    public class Object { }
    public class ScriptableObject : Object { }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string menuName { get; set; }
        public string fileName { get; set; }
        public int order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeReference : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TextAreaAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute { public MinAttribute(float min) { } }

    public static class Mathf
    {
        public const float PI = 3.14159274f;
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Abs(float v) => Math.Abs(v);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => a * d;
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3})";
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => new Vector4(0, 0, 0, 0);
        public static Vector4 one => new Vector4(1, 1, 1, 1);
        public float this[int i]
        {
            get { switch (i) { case 0: return x; case 1: return y; case 2: return z; case 3: return w; default: throw new IndexOutOfRangeException(); } }
            set { switch (i) { case 0: x = value; break; case 1: y = value; break; case 2: z = value; break; case 3: w = value; break; default: throw new IndexOutOfRangeException(); } }
        }
        public static Vector4 Scale(Vector4 a, Vector4 b) => new Vector4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);
        public static Vector4 LerpUnclamped(Vector4 a, Vector4 b, float t) => a + (b - a) * t;
        public static Vector4 operator +(Vector4 a, Vector4 b) => new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        public static Vector4 operator -(Vector4 a, Vector4 b) => new Vector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        public static Vector4 operator *(Vector4 a, float d) => new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);
        public static implicit operator Vector3(Vector4 v) => new Vector3(v.x, v.y, v.z);
        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3}, {w:F3})";
    }

    public struct Keyframe
    {
        public float time, value, inTangent, outTangent;
        public Keyframe(float time, float value) : this(time, value, 0f, 0f) { }
        public Keyframe(float time, float value, float inTangent, float outTangent)
        {
            this.time = time; this.value = value; this.inTangent = inTangent; this.outTangent = outTangent;
        }
    }

    public class AnimationCurve
    {
        Keyframe[] m_Keys;
        public AnimationCurve(params Keyframe[] keys) { m_Keys = (Keyframe[])keys.Clone(); Array.Sort(m_Keys, (a, b) => a.time.CompareTo(b.time)); }
        public Keyframe[] keys { get => (Keyframe[])m_Keys.Clone(); set => m_Keys = (Keyframe[])value.Clone(); }
        public int length => m_Keys.Length;

        // 非加权 Hermite 插值，与 Unity 默认（非 weighted）关键帧一致。
        public float Evaluate(float t)
        {
            if (m_Keys.Length == 0) return 0f;
            if (t <= m_Keys[0].time) return m_Keys[0].value;
            var last = m_Keys[m_Keys.Length - 1];
            if (t >= last.time) return last.value;
            for (int i = 0; i < m_Keys.Length - 1; i++)
            {
                var k0 = m_Keys[i];
                var k1 = m_Keys[i + 1];
                if (t > k1.time) continue;
                float dt = k1.time - k0.time;
                float s = dt > 0f ? (t - k0.time) / dt : 0f;
                float s2 = s * s, s3 = s2 * s;
                float m0 = k0.outTangent * dt, m1 = k1.inTangent * dt;
                return (2 * s3 - 3 * s2 + 1) * k0.value + (s3 - 2 * s2 + s) * m0 + (-2 * s3 + 3 * s2) * k1.value + (s3 - s2) * m1;
            }
            return last.value;
        }
    }
}
