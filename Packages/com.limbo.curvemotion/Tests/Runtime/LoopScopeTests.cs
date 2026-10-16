using NUnit.Framework;
using Limbo.CurveMotion.Serialization;

namespace Limbo.CurveMotion.Tests
{
    public class LoopScopeTests
    {
        const float Eps = 1e-4f;

        static MotionData TailLoop(int count, MotionLoopType type)
        {
            // 两段：0~1s 飞过去，1~1.5s 是尾段。
            var data = TestData.Line(1f, 0.5f);
            data.loop.count = count;
            data.loop.type = type;
            data.loop.scope = MotionLoopScope.LastSegment;
            return data;
        }

        [Test]
        public void TailLoop_StartsAtLastSegment()
        {
            var eval = new MotionEvaluator(TailLoop(3, MotionLoopType.Restart));
            Assert.AreEqual(1f, eval.LoopStart, Eps);
            Assert.AreEqual(0.5f, eval.LoopLength, Eps);
            Assert.AreEqual(2.5f, eval.TotalDuration, Eps, "前面 1s 只播一次，尾段 0.5s 播 3 次");
        }

        [Test]
        public void TailLoop_Restart_RepeatsOnlyTheTail()
        {
            var eval = new MotionEvaluator(TailLoop(3, MotionLoopType.Restart));
            Assert.AreEqual(0.6f, eval.ToLocalTime(0.6f), Eps, "尾段之前照常播放");
            Assert.AreEqual(1.25f, eval.ToLocalTime(1.25f), Eps);
            Assert.AreEqual(1.25f, eval.ToLocalTime(1.75f), Eps, "第二次尾段从尾段开头重来");
            Assert.AreEqual(1.5f, eval.ToLocalTime(10f), Eps, "播完停在结尾");
        }

        [Test]
        public void TailLoop_Yoyo_ReversesEveryOtherRepeat()
        {
            var eval = new MotionEvaluator(TailLoop(-1, MotionLoopType.Yoyo));
            Assert.IsTrue(float.IsPositiveInfinity(eval.TotalDuration));
            Assert.AreEqual(1.4f, eval.ToLocalTime(1.6f), Eps, "第二次尾段倒着播");
            Assert.AreEqual(1.1f, eval.ToLocalTime(2.1f), Eps, "第三次又正着播");
            Assert.GreaterOrEqual(eval.ToLocalTime(100.3f), 1f, "无限循环也不会回到尾段之前");
        }

        [Test]
        public void WholeLoop_IsUnchanged()
        {
            var data = TestData.Line(1f, 0.5f);
            data.loop.count = 2;
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(0f, eval.LoopStart, Eps);
            Assert.AreEqual(3f, eval.TotalDuration, Eps);
            Assert.AreEqual(0.25f, eval.ToLocalTime(1.75f), Eps);
        }

        [Test]
        public void Json_RoundTripsScope()
        {
            string json = MotionJson.ToJson(TailLoop(-1, MotionLoopType.Yoyo));
            StringAssert.Contains("\"scope\": \"LastSegment\"", json);
            StringAssert.Contains("\"start\": 1", json);
            var back = MotionJson.FromJson(json);
            Assert.AreEqual(MotionLoopScope.LastSegment, back.loop.scope);
            Assert.AreEqual(-1, back.loop.count);
        }

        [Test]
        public void Json_WithoutScope_DefaultsToWhole()
        {
            string json = MotionJson.ToJson(TailLoop(2, MotionLoopType.Restart)).Replace("\"scope\"", "\"unusedField\"");
            Assert.AreEqual(MotionLoopScope.Whole, MotionJson.FromJson(json).loop.scope);
        }
    }
}
