using System;

namespace Limbo.CurveMotion
{
    /// <summary>
    /// 缓动函数。公式与 DOTween 默认参数一致（overshoot/amplitude = 1.70158，elastic period = 0.3），
    /// 编辑器预览与导出的 DOTween 代码播放结果相同。输入为归一化时间 0~1。
    /// </summary>
    public static class Easing
    {
        const float Overshoot = 1.70158f;
        const float PiOver2 = (float)(Math.PI * 0.5);
        const float TwoPi = (float)(Math.PI * 2.0);

        public static float Evaluate(EaseSpec ease, float t)
        {
            t = Clamp01(t);
            if (ease == null) return t;
            if (ease.type == EaseType.Custom)
                return ease.curve != null && ease.curve.length > 0 ? ease.curve.Evaluate(t) : t;
            return Evaluate(ease.type, t);
        }

        public static float Evaluate(EaseType type, float t)
        {
            t = Clamp01(t);
            switch (type)
            {
                case EaseType.Linear: return t;

                case EaseType.InSine: return 1f - (float)Math.Cos(t * PiOver2);
                case EaseType.OutSine: return (float)Math.Sin(t * PiOver2);
                case EaseType.InOutSine: return -0.5f * ((float)Math.Cos(Math.PI * t) - 1f);

                case EaseType.InQuad: return t * t;
                case EaseType.OutQuad: return -t * (t - 2f);
                case EaseType.InOutQuad:
                    if ((t *= 2f) < 1f) return 0.5f * t * t;
                    t -= 1f;
                    return -0.5f * (t * (t - 2f) - 1f);

                case EaseType.InCubic: return t * t * t;
                case EaseType.OutCubic: t -= 1f; return t * t * t + 1f;
                case EaseType.InOutCubic:
                    if ((t *= 2f) < 1f) return 0.5f * t * t * t;
                    t -= 2f;
                    return 0.5f * (t * t * t + 2f);

                case EaseType.InQuart: return t * t * t * t;
                case EaseType.OutQuart: t -= 1f; return -(t * t * t * t - 1f);
                case EaseType.InOutQuart:
                    if ((t *= 2f) < 1f) return 0.5f * t * t * t * t;
                    t -= 2f;
                    return -0.5f * (t * t * t * t - 2f);

                case EaseType.InQuint: return t * t * t * t * t;
                case EaseType.OutQuint: t -= 1f; return t * t * t * t * t + 1f;
                case EaseType.InOutQuint:
                    if ((t *= 2f) < 1f) return 0.5f * t * t * t * t * t;
                    t -= 2f;
                    return 0.5f * (t * t * t * t * t + 2f);

                case EaseType.InExpo: return t == 0f ? 0f : (float)Math.Pow(2, 10 * (t - 1f));
                case EaseType.OutExpo: return t == 1f ? 1f : 1f - (float)Math.Pow(2, -10 * t);
                case EaseType.InOutExpo:
                    if (t == 0f) return 0f;
                    if (t == 1f) return 1f;
                    if ((t *= 2f) < 1f) return 0.5f * (float)Math.Pow(2, 10 * (t - 1f));
                    return 0.5f * (2f - (float)Math.Pow(2, -10 * (t - 1f)));

                case EaseType.InCirc: return -((float)Math.Sqrt(1f - t * t) - 1f);
                case EaseType.OutCirc: t -= 1f; return (float)Math.Sqrt(1f - t * t);
                case EaseType.InOutCirc:
                    if ((t *= 2f) < 1f) return -0.5f * ((float)Math.Sqrt(1f - t * t) - 1f);
                    t -= 2f;
                    return 0.5f * ((float)Math.Sqrt(1f - t * t) + 1f);

                case EaseType.InElastic: return InElastic(t);
                case EaseType.OutElastic: return OutElastic(t);
                case EaseType.InOutElastic: return InOutElastic(t);

                case EaseType.InBack: return t * t * ((Overshoot + 1f) * t - Overshoot);
                case EaseType.OutBack: t -= 1f; return t * t * ((Overshoot + 1f) * t + Overshoot) + 1f;
                case EaseType.InOutBack:
                {
                    const float s = Overshoot * 1.525f;
                    if ((t *= 2f) < 1f) return 0.5f * (t * t * ((s + 1f) * t - s));
                    t -= 2f;
                    return 0.5f * (t * t * ((s + 1f) * t + s) + 2f);
                }

                case EaseType.InBounce: return 1f - OutBounce(1f - t);
                case EaseType.OutBounce: return OutBounce(t);
                case EaseType.InOutBounce:
                    return t < 0.5f ? (1f - OutBounce(1f - t * 2f)) * 0.5f : OutBounce(t * 2f - 1f) * 0.5f + 0.5f;

                default: return t;
            }
        }

        static float InElastic(float t)
        {
            if (t == 0f) return 0f;
            if (t == 1f) return 1f;
            const float period = 0.3f;
            float s = period / TwoPi * (float)Math.Asin(1f / Overshoot);
            t -= 1f;
            return -(Overshoot * (float)Math.Pow(2, 10 * t) * (float)Math.Sin((t - s) * TwoPi / period));
        }

        static float OutElastic(float t)
        {
            if (t == 0f) return 0f;
            if (t == 1f) return 1f;
            const float period = 0.3f;
            float s = period / TwoPi * (float)Math.Asin(1f / Overshoot);
            return Overshoot * (float)Math.Pow(2, -10 * t) * (float)Math.Sin((t - s) * TwoPi / period) + 1f;
        }

        static float InOutElastic(float t)
        {
            if (t == 0f) return 0f;
            if (t == 1f) return 1f;
            const float period = 0.3f * 1.5f;
            float s = period / TwoPi * (float)Math.Asin(1f / Overshoot);
            t = t * 2f - 1f;
            if (t < 0f)
                return -0.5f * (Overshoot * (float)Math.Pow(2, 10 * t) * (float)Math.Sin((t - s) * TwoPi / period));
            return Overshoot * (float)Math.Pow(2, -10 * t) * (float)Math.Sin((t - s) * TwoPi / period) * 0.5f + 1f;
        }

        static float OutBounce(float t)
        {
            if (t < 1f / 2.75f) return 7.5625f * t * t;
            if (t < 2f / 2.75f) { t -= 1.5f / 2.75f; return 7.5625f * t * t + 0.75f; }
            if (t < 2.5f / 2.75f) { t -= 2.25f / 2.75f; return 7.5625f * t * t + 0.9375f; }
            t -= 2.625f / 2.75f;
            return 7.5625f * t * t + 0.984375f;
        }

        static float Clamp01(float t) => t < 0f ? 0f : t > 1f ? 1f : t;
    }
}
