using System;
using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>缩放，按倍率作用于初始 localScale。</summary>
    [Serializable, MotionTrack("scale", "Scale")]
    public class ScaleTrack : MotionTrack
    {
        public override int Dimension => 3;
        public override ValueMode Mode => ValueMode.Multiply;
        public override Vector4 DefaultValue => new Vector4(1f, 1f, 1f, 0f);
    }

    /// <summary>旋转，欧拉角增量（度），叠加到初始 localEulerAngles。</summary>
    [Serializable, MotionTrack("rotation", "Rotation")]
    public class RotationTrack : MotionTrack
    {
        public override int Dimension => 3;
        public override ValueMode Mode => ValueMode.Add;
        public override Vector4 DefaultValue => Vector4.zero;
    }

    /// <summary>透明度，绝对值 0~1。作用于 CanvasGroup、Graphic 或 SpriteRenderer。</summary>
    [Serializable, MotionTrack("alpha", "Alpha")]
    public class AlphaTrack : MotionTrack
    {
        public override int Dimension => 1;
        public override ValueMode Mode => ValueMode.Absolute;
        public override Vector4 DefaultValue => new Vector4(1f, 0f, 0f, 0f);
        public override string[] ComponentNames => new[] { "a" };
    }

    /// <summary>颜色 RGB，绝对值 0~1。不含透明度，透明度由 <see cref="AlphaTrack"/> 负责，两者互不干扰。</summary>
    [Serializable, MotionTrack("color", "Color")]
    public class ColorTrack : MotionTrack
    {
        public override int Dimension => 3;
        public override ValueMode Mode => ValueMode.Absolute;
        public override Vector4 DefaultValue => new Vector4(1f, 1f, 1f, 0f);
        public override string[] ComponentNames => new[] { "r", "g", "b" };
    }
}
