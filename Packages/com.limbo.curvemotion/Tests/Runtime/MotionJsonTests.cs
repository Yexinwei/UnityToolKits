using System;
using System.Collections.Generic;
using Limbo.CurveMotion.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace Limbo.CurveMotion.Tests
{
    public class MotionJsonTests
    {
        const float Eps = 1e-4f;

        static MotionData Sample()
        {
            var data = TestData.Line(0.4f, 0.6f);
            data.name = "CoinFly";
            data.description = "金币飞向\"钱包\"";
            data.target = TargetKind.Transform;
            data.points[1].type = PointType.Free;
            data.points[1].inTangent = new Vector3(-1, 2, 0);
            data.points[1].outTangent = new Vector3(3, 1, 0);
            data.points[1].segment.timing = TimingMode.Speed;
            data.points[1].segment.speed = 20f;
            data.points[1].segment.ease = new EaseSpec
            {
                type = EaseType.Custom,
                curve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0.5f, 0f)),
            };
            data.points[0].segment.desc = "向右滑出";

            var scale = new ScaleTrack();
            var clip = scale.AddClip("p1", -0.1f, 0.2f);
            clip.value = new Vector4(1.2f, 1.2f, 1f, 0f);
            clip.ease.type = EaseType.OutBack;
            data.tracks.Add(scale);

            var alpha = new AlphaTrack { enabled = false };
            alpha.AddClip("p2", 0f, 0.3f).value = new Vector4(0f, 0f, 0f, 0f);
            data.tracks.Add(alpha);

            data.events.Add(new MotionEvent { anchor = "p2", offset = 0.05f, name = "OnArrive", message = "coin arrived" });
            data.loop.count = 2;
            data.loop.type = MotionLoopType.Yoyo;
            return data;
        }

        [Test]
        public void RoundTrip_PreservesEditableFields()
        {
            var original = Sample();
            var warnings = new List<string>();
            var copy = MotionJson.FromJson(MotionJson.ToJson(original), warnings);

            Assert.IsEmpty(warnings);
            Assert.AreEqual(original.name, copy.name);
            Assert.AreEqual(original.description, copy.description);
            Assert.AreEqual(TargetKind.Transform, copy.target);
            Assert.AreEqual(2, copy.loop.count);
            Assert.AreEqual(MotionLoopType.Yoyo, copy.loop.type);

            Assert.AreEqual(3, copy.points.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(original.points[i].id, copy.points[i].id);
                Assert.AreEqual(original.points[i].type, copy.points[i].type);
                Assert.AreEqual(original.points[i].position.x, copy.points[i].position.x, Eps);
            }
            Assert.AreEqual(-1f, copy.points[1].inTangent.x, Eps);
            Assert.AreEqual(1f, copy.points[1].outTangent.y, Eps);
            Assert.AreEqual("向右滑出", copy.points[0].segment.desc);
            Assert.AreEqual(TimingMode.Speed, copy.points[1].segment.timing);
            Assert.AreEqual(20f, copy.points[1].segment.speed, Eps);
            Assert.AreEqual(EaseType.Custom, copy.points[1].segment.ease.type);
            Assert.AreEqual(2, copy.points[1].segment.ease.curve.length);
            Assert.AreEqual(2f, copy.points[1].segment.ease.curve.keys[0].outTangent, Eps);

            Assert.AreEqual(2, copy.tracks.Count);
            Assert.IsInstanceOf<ScaleTrack>(copy.tracks[0]);
            Assert.IsInstanceOf<AlphaTrack>(copy.tracks[1]);
            Assert.IsFalse(copy.tracks[1].enabled);
            var clip = copy.tracks[0].clips[0];
            Assert.AreEqual("p1", clip.anchor);
            Assert.AreEqual(-0.1f, clip.offset, Eps);
            Assert.AreEqual(0.2f, clip.duration, Eps);
            Assert.AreEqual(EaseType.OutBack, clip.ease.type);
            Assert.AreEqual(1.2f, clip.value.x, Eps);
            Assert.AreEqual(1f, clip.value.z, Eps);

            Assert.AreEqual(1, copy.events.Count);
            Assert.AreEqual("OnArrive", copy.events[0].name);
            Assert.AreEqual(0.05f, copy.events[0].offset, Eps);
        }

        [Test]
        public void RoundTrip_EvaluatesIdentically()
        {
            var original = Sample();
            var copy = MotionJson.FromJson(MotionJson.ToJson(original));
            var a = new MotionEvaluator(original);
            var b = new MotionEvaluator(copy);
            Assert.AreEqual(a.Duration, b.Duration, Eps);
            for (float t = 0f; t <= a.Duration; t += 0.05f)
            {
                var pa = a.EvaluatePosition(t);
                var pb = b.EvaluatePosition(t);
                Assert.AreEqual(pa.x, pb.x, 1e-3f, "t=" + t);
                Assert.AreEqual(pa.y, pb.y, 1e-3f, "t=" + t);
                Assert.AreEqual(a.EvaluateTrack(0, t, Vector4.one).x, b.EvaluateTrack(0, t, Vector4.one).x, 1e-3f);
            }
        }

        [Test]
        public void Output_ContainsComputedFieldsForAI()
        {
            var json = MotionJson.ToJson(Sample());
            var root = (Dictionary<string, object>)Json.Parse(json);
            Assert.AreEqual(1.0, (double)root["schemaVersion"]);
            Assert.IsTrue(root.ContainsKey("conventions"));
            Assert.Greater((double)root["duration"], 0.0);

            var path = (Dictionary<string, object>)root["path"];
            var points = (List<object>)path["points"];
            Assert.AreEqual(0.4, (double)((Dictionary<string, object>)points[1])["time"], 1e-4);
            var segments = (List<object>)path["segments"];
            Assert.AreEqual(2, segments.Count);
            Assert.GreaterOrEqual((double)((Dictionary<string, object>)segments[0])["length"], 10.0);

            var clip = (Dictionary<string, object>)((List<object>)((Dictionary<string, object>)((List<object>)root["tracks"])[0])["clips"])[0];
            Assert.AreEqual(0.3, (double)clip["start"], 1e-4);
            Assert.AreEqual(3, ((List<object>)clip["value"]).Count, "scale 只写 3 个分量");
        }

        [Test]
        public void UnknownTrackType_IsSkippedWithWarning()
        {
            const string json = "{\"schemaVersion\":1,\"tracks\":[{\"type\":\"sprite\",\"clips\":[]},{\"type\":\"alpha\",\"clips\":[]}]}";
            var warnings = new List<string>();
            var data = MotionJson.FromJson(json, warnings);
            Assert.AreEqual(1, data.tracks.Count);
            Assert.AreEqual(1, warnings.Count);
        }

        [Test]
        public void NewerSchemaVersion_Throws()
        {
            Assert.Throws<FormatException>(() => MotionJson.FromJson("{\"schemaVersion\":999}"));
        }

        [Test]
        public void MissingSchemaVersion_Throws()
        {
            Assert.Throws<FormatException>(() => MotionJson.FromJson("{\"name\":\"x\"}"));
        }

        [Test]
        public void Parser_HandlesEscapesAndNumbers()
        {
            var root = (Dictionary<string, object>)Json.Parse("{\"s\":\"a\\\"b\\u4e2d\\n\",\"n\":-1.5e2,\"b\":true,\"z\":null,\"l\":[]}");
            Assert.AreEqual("a\"b中\n", root["s"]);
            Assert.AreEqual(-150.0, root["n"]);
            Assert.AreEqual(true, root["b"]);
            Assert.IsNull(root["z"]);
            Assert.IsEmpty((List<object>)root["l"]);
        }

        [Test]
        public void Parser_RejectsMalformed()
        {
            Assert.Throws<FormatException>(() => Json.Parse("{\"a\":1,}"));
            Assert.Throws<FormatException>(() => Json.Parse("[1 2]"));
            Assert.Throws<FormatException>(() => Json.Parse("{} x"));
        }

        [Test]
        public void Numbers_UseInvariantCultureAndRounding()
        {
            Assert.AreEqual("0.33333", Json.FormatNumber(1.0 / 3.0));
            Assert.AreEqual("-2.5", Json.FormatNumber(-2.5));
            Assert.AreEqual("0", Json.FormatNumber(-0.000001));
            Assert.AreEqual("0", Json.FormatNumber(double.NaN));
        }
    }
}
