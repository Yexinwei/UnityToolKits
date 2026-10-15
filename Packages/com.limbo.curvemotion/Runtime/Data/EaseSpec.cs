using System;
using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>缓动类型。名称与 DOTween 的 Ease 一致，导出代码时直接对应。</summary>
    public enum EaseType
    {
        Linear,
        InSine, OutSine, InOutSine,
        InQuad, OutQuad, InOutQuad,
        InCubic, OutCubic, InOutCubic,
        InQuart, OutQuart, InOutQuart,
        InQuint, OutQuint, InOutQuint,
        InExpo, OutExpo, InOutExpo,
        InCirc, OutCirc, InOutCirc,
        InElastic, OutElastic, InOutElastic,
        InBack, OutBack, InOutBack,
        InBounce, OutBounce, InOutBounce,
        /// <summary>自定义曲线，使用 <see cref="EaseSpec.curve"/>（时间与值都在 0~1）。</summary>
        Custom,
    }

    [Serializable]
    public class EaseSpec
    {
        public EaseType type = EaseType.OutQuad;
        public AnimationCurve curve;

        public EaseSpec Clone()
        {
            return new EaseSpec
            {
                type = type,
                curve = curve == null ? null : new AnimationCurve(curve.keys),
            };
        }
    }
}
