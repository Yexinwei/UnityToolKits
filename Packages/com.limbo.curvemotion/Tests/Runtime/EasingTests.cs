using System;
using NUnit.Framework;
using UnityEngine;

namespace Limbo.CurveMotion.Tests
{
    public class EasingTests
    {
        [Test]
        public void AllBuiltinEases_StartAtZero_EndAtOne()
        {
            foreach (EaseType type in Enum.GetValues(typeof(EaseType)))
            {
                if (type == EaseType.Custom) continue;
                Assert.AreEqual(0f, Easing.Evaluate(type, 0f), 1e-4f, type + " @0");
                Assert.AreEqual(1f, Easing.Evaluate(type, 1f), 1e-4f, type + " @1");
            }
        }

        [Test]
        public void KnownValues_MatchDOTweenFormulas()
        {
            Assert.AreEqual(0.5f, Easing.Evaluate(EaseType.Linear, 0.5f), 1e-6f);
            Assert.AreEqual(0.75f, Easing.Evaluate(EaseType.OutQuad, 0.5f), 1e-6f);
            Assert.AreEqual(0.25f, Easing.Evaluate(EaseType.InQuad, 0.5f), 1e-6f);
            Assert.AreEqual(0.5f, Easing.Evaluate(EaseType.InOutCubic, 0.5f), 1e-6f);
            // InBack(0.5) = 0.25 * (2.70158 * 0.5 - 1.70158)
            Assert.AreEqual(-0.0876975f, Easing.Evaluate(EaseType.InBack, 0.5f), 1e-5f);
            Assert.Greater(Easing.Evaluate(EaseType.OutBack, 0.7f), 1f, "OutBack 应该过冲");
        }

        [Test]
        public void InputIsClamped()
        {
            Assert.AreEqual(0f, Easing.Evaluate(EaseType.OutQuad, -1f), 1e-6f);
            Assert.AreEqual(1f, Easing.Evaluate(EaseType.OutQuad, 2f), 1e-6f);
        }

        [Test]
        public void CustomCurve_IsEvaluated()
        {
            var ease = new EaseSpec
            {
                type = EaseType.Custom,
                curve = new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(1f, 1f, 1f, 1f)),
            };
            Assert.AreEqual(0.5f, Easing.Evaluate(ease, 0.5f), 1e-4f);
        }

        [Test]
        public void CustomWithoutCurve_FallsBackToLinear()
        {
            Assert.AreEqual(0.3f, Easing.Evaluate(new EaseSpec { type = EaseType.Custom }, 0.3f), 1e-6f);
        }
    }
}
