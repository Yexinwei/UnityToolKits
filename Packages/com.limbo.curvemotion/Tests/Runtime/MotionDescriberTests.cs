using Limbo.CurveMotion.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace Limbo.CurveMotion.Tests
{
    public class MotionDescriberTests
    {
        static MotionData Sample()
        {
            var data = TestData.Line(0.4f, 0.6f);
            var scale = new ScaleTrack();
            var clip = scale.AddClip("p1", -0.1f, 0.2f);
            clip.value = new Vector4(1.2f, 1.2f, 1.2f, 0f);
            clip.ease.type = EaseType.OutBack;
            data.tracks.Add(scale);
            data.events.Add(new MotionEvent { anchor = "p2", name = "OnArrive", message = "到了" });
            return data;
        }

        [Test]
        public void Segment_StraightLine()
        {
            var data = Sample();
            var eval = new MotionEvaluator(data);
            Assert.AreEqual("直线向右移动 (10, 0) px，0.4 秒，Linear（匀速）", MotionDescriber.Segment(data, eval, 0));
        }

        [Test]
        public void Segment_SpeedAndEase()
        {
            var data = Sample();
            data.target = TargetKind.Transform;
            data.points[1].segment.timing = TimingMode.Speed;
            data.points[1].segment.speed = 20f;
            data.points[1].segment.ease = new EaseSpec { type = EaseType.OutQuad };
            var eval = new MotionEvaluator(data);
            Assert.AreEqual("直线向右移动 (10, 0) 单位，0.5 秒（速度 20 单位/秒），OutQuad（减速停下）", MotionDescriber.Segment(data, eval, 1));
        }

        [Test]
        public void Segment_CurvedMentionsBulge()
        {
            var data = TestData.Line(0.5f);
            data.points[0].type = PointType.Free;
            data.points[0].outTangent = new Vector3(0f, 8f, 0f);
            data.points[1].type = PointType.Free;
            data.points[1].inTangent = new Vector3(0f, 8f, 0f);
            var eval = new MotionEvaluator(data);
            StringAssert.StartsWith("弧线向右移动 (10, 0) px（向上拱起）", MotionDescriber.Segment(data, eval, 0));
        }

        [Test]
        public void Segment_ManualWins()
        {
            var data = Sample();
            data.points[0].segment.desc = "弹出";
            var eval = new MotionEvaluator(data);
            Assert.AreEqual("弹出", MotionDescriber.SegmentOrManual(data, eval, 0));
            data.points[0].segment.desc = "  ";
            StringAssert.StartsWith("直线", MotionDescriber.SegmentOrManual(data, eval, 0));
        }

        [Test]
        public void Clip_TimeValueEase()
        {
            var data = Sample();
            var eval = new MotionEvaluator(data);
            Assert.AreEqual("0.3–0.5 秒：缩放到初始的 ×1.2，OutBack（冲过头再回弹）", MotionDescriber.Clip(data, eval, 0, 0));
        }

        [Test]
        public void Clip_TruncatedAndInstant()
        {
            var data = Sample();
            var scale = data.tracks[0];
            scale.AddClip("p1", 0f, 0f).value = new Vector4(1f, 1f, 1f, 0f);
            var eval = new MotionEvaluator(data);
            StringAssert.EndsWith("（0.4 秒起由同轨道下一个片段接管）", MotionDescriber.Clip(data, eval, 0, 0));
            Assert.AreEqual("0.4 秒：瞬间缩放回初始大小", MotionDescriber.Clip(data, eval, 0, 1));
        }

        [Test]
        public void Event_AndSummary()
        {
            var data = Sample();
            data.loop = new LoopSettings { count = 3, type = MotionLoopType.Yoyo };
            var eval = new MotionEvaluator(data);
            Assert.AreEqual("1 秒触发事件 OnArrive：到了", MotionDescriber.Event(data, eval, 0));
            Assert.AreEqual("UI 动效，单次 1 秒：路径 2 段，总长 20 px，属性变化 Scale，事件 1 个；整段循环 3 次（来回）。", MotionDescriber.Summary(data, eval));

            data.loop = new LoopSettings { count = -1, scope = MotionLoopScope.LastSegment };
            eval = new MotionEvaluator(data);
            Assert.AreEqual("前 0.4 秒播一次，之后最后一段无限循环", MotionDescriber.Loop(data, eval));
        }

        [TestCase(1f, 0f, "右")]
        [TestCase(1f, 1f, "右上")]
        [TestCase(-1f, 0.2f, "左")]
        [TestCase(0f, -3f, "下")]
        [TestCase(0.5f, -0.5f, "右下")]
        public void Direction_EightWays(float x, float y, string expected)
        {
            Assert.AreEqual(expected, MotionDescriber.Direction(new Vector3(x, y, 0f)));
        }

        [TestCase(0.5f, 3, "0.5")]
        [TestCase(1f, 3, "1")]
        [TestCase(-0.0001f, 3, "0")]
        [TestCase(1.23456f, 2, "1.23")]
        [TestCase(2200f, 0, "2200")]
        public void Num_TrimsZeros(float value, int decimals, string expected)
        {
            Assert.AreEqual(expected, MotionDescriber.Num(value, decimals));
        }

        [Test]
        public void Rgb_WithHex()
        {
            Assert.AreEqual("RGB(1, 0.5, 0) #FF8000", MotionDescriber.Rgb(1f, 0.5f, 0f));
        }

        [Test]
        public void Json_AutoDescIsWrittenButNotReadBack()
        {
            var data = Sample();
            data.points[0].segment.desc = "弹出";
            string json = MotionJson.ToJson(data);
            StringAssert.Contains("\"summary\": \"UI 动效", json);
            StringAssert.Contains("\"desc\": \"弹出\",\n", json.Replace("\r\n", "\n"));
            StringAssert.Contains("\"descSource\": \"manual\"", json);
            StringAssert.Contains("\"desc\": \"直线向右移动 (10, 0) px，0.6 秒，Linear（匀速）\"", json);
            StringAssert.Contains("\"descSource\": \"auto\"", json);
            StringAssert.Contains("\"desc\": \"1 秒触发事件 OnArrive：到了\"", json);

            var back = MotionJson.FromJson(json);
            Assert.AreEqual("弹出", back.points[0].segment.desc);
            Assert.IsTrue(string.IsNullOrEmpty(back.points[1].segment.desc), "自动生成的描述不读回");
            Assert.IsTrue(string.IsNullOrEmpty(back.tracks[0].clips[0].desc));
            Assert.AreEqual(json, MotionJson.ToJson(back), "读回再写出不变");
        }

        [Test]
        public void Json_DescWithoutSourceCountsAsManual()
        {
            var data = Sample();
            string json = MotionJson.ToJson(data).Replace("\"descSource\": \"auto\"", "\"descSourceRemoved\": \"auto\"");
            var back = MotionJson.FromJson(json);
            Assert.AreEqual("直线向右移动 (10, 0) px，0.4 秒，Linear（匀速）", back.points[0].segment.desc, "旧版 JSON 没有 descSource，desc 按手填处理");
        }
    }
}
