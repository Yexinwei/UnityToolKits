using System;
using UnityEngine;
#if CURVEMOTION_UGUI
using UnityEngine.UI;
#endif

namespace Limbo.CurveMotion
{
    /// <summary>缩放，按倍率作用于初始 localScale。</summary>
    [Serializable, MotionTrack("scale", "Scale")]
    public class ScaleTrack : MotionTrack
    {
        public override int Dimension => 3;
        public override ValueMode Mode => ValueMode.Multiply;
        public override Vector4 DefaultValue => new Vector4(1f, 1f, 1f, 0f);

        public override bool TryReadValue(GameObject target, out Vector4 value)
        {
            value = target.transform.localScale;
            return true;
        }

        public override void WriteValue(GameObject target, Vector4 value) => target.transform.localScale = value;

        public override string DescribeValue(Vector4 value)
        {
            if (Same(value.x, 1f) && Same(value.y, 1f) && Same(value.z, 1f)) return "缩放回初始大小";
            if (Same(value.x, value.y) && Same(value.y, value.z)) return "缩放到初始的 ×" + MotionDescriber.Num(value.x, 3);
            if (Same(value.x, value.y) && Same(value.z, 1f)) return "缩放到初始的 ×" + MotionDescriber.Num(value.x, 3) + "（Z 不变）";
            return "缩放到初始的 ×(" + MotionDescriber.Num(value.x, 3) + ", " + MotionDescriber.Num(value.y, 3) + ", " + MotionDescriber.Num(value.z, 3) + ")";
        }

        static bool Same(float a, float b) => Math.Abs(a - b) < 1e-4f;
    }

    /// <summary>旋转，欧拉角增量（度），叠加到初始 localEulerAngles。</summary>
    [Serializable, MotionTrack("rotation", "Rotation")]
    public class RotationTrack : MotionTrack
    {
        public override int Dimension => 3;
        public override ValueMode Mode => ValueMode.Add;
        public override Vector4 DefaultValue => Vector4.zero;

        public override bool TryReadValue(GameObject target, out Vector4 value)
        {
            value = target.transform.localEulerAngles;
            return true;
        }

        public override void WriteValue(GameObject target, Vector4 value) => target.transform.localEulerAngles = value;

        public override string DescribeValue(Vector4 value)
        {
            string[] axes = { "X", "Y", "Z" };
            int nonZero = 0, axis = 0;
            for (int i = 0; i < 3; i++)
                if (Math.Abs(value[i]) >= 1e-3f) { nonZero++; axis = i; }
            if (nonZero == 0) return "转回初始角度";
            if (nonZero == 1) return "绕 " + axes[axis] + " 轴转到初始角度 " + Signed(value[axis]) + "°";
            return "转到初始角度 +(" + MotionDescriber.Num(value.x, 2) + "°, " + MotionDescriber.Num(value.y, 2) + "°, " + MotionDescriber.Num(value.z, 2) + "°)";
        }

        static string Signed(float v) => (v > 0f ? "+" : "") + MotionDescriber.Num(v, 2);
    }

    /// <summary>透明度，绝对值 0~1。依次查找 CanvasGroup、Graphic、SpriteRenderer。</summary>
    [Serializable, MotionTrack("alpha", "Alpha")]
    public class AlphaTrack : MotionTrack
    {
        public override int Dimension => 1;
        public override ValueMode Mode => ValueMode.Absolute;
        public override Vector4 DefaultValue => new Vector4(1f, 0f, 0f, 0f);
        public override string[] ComponentNames => new[] { "a" };

        public override bool TryReadValue(GameObject target, out Vector4 value)
        {
            value = DefaultValue;
            var group = target.GetComponent<CanvasGroup>();
            if (group != null) { value.x = group.alpha; return true; }
            if (ColorTargets.TryGetColor(target, out var color)) { value.x = color.a; return true; }
            return false;
        }

        public override void WriteValue(GameObject target, Vector4 value)
        {
            var group = target.GetComponent<CanvasGroup>();
            if (group != null) { group.alpha = value.x; return; }
            if (!ColorTargets.TryGetColor(target, out var color)) return;
            color.a = value.x;
            ColorTargets.SetColor(target, color);
        }

        public override string DescribeValue(Vector4 value)
        {
            string text = "透明度变为 " + MotionDescriber.Num(value.x, 3);
            if (value.x <= 1e-3f) return text + "（完全透明）";
            if (value.x >= 1f - 1e-3f) return text + "（不透明）";
            return text;
        }
    }

    /// <summary>颜色 RGB，绝对值 0~1。不含透明度，透明度由 <see cref="AlphaTrack"/> 负责，两者互不干扰。</summary>
    [Serializable, MotionTrack("color", "Color")]
    public class ColorTrack : MotionTrack
    {
        public override int Dimension => 3;
        public override ValueMode Mode => ValueMode.Absolute;
        public override Vector4 DefaultValue => new Vector4(1f, 1f, 1f, 0f);
        public override string[] ComponentNames => new[] { "r", "g", "b" };

        public override bool TryReadValue(GameObject target, out Vector4 value)
        {
            value = DefaultValue;
            if (!ColorTargets.TryGetColor(target, out var color)) return false;
            value = new Vector4(color.r, color.g, color.b, 0f);
            return true;
        }

        public override void WriteValue(GameObject target, Vector4 value)
        {
            if (!ColorTargets.TryGetColor(target, out var color)) return;
            ColorTargets.SetColor(target, new Color(value.x, value.y, value.z, color.a));
        }

        public override string DescribeValue(Vector4 value) => "颜色变为 " + MotionDescriber.Rgb(value.x, value.y, value.z);
    }

    /// <summary>颜色来源：UGUI 的 Graphic（含 TextMeshPro）优先，其次 SpriteRenderer。</summary>
    static class ColorTargets
    {
        public static bool TryGetColor(GameObject target, out Color color)
        {
#if CURVEMOTION_UGUI
            var graphic = target.GetComponent<Graphic>();
            if (graphic != null) { color = graphic.color; return true; }
#endif
            var sprite = target.GetComponent<SpriteRenderer>();
            if (sprite != null) { color = sprite.color; return true; }
            color = Color.white;
            return false;
        }

        public static void SetColor(GameObject target, Color color)
        {
#if CURVEMOTION_UGUI
            var graphic = target.GetComponent<Graphic>();
            if (graphic != null) { graphic.color = color; return; }
#endif
            var sprite = target.GetComponent<SpriteRenderer>();
            if (sprite != null) sprite.color = color;
        }
    }
}
