using NUnit.Framework;
using UnityEngine;

namespace Limbo.CurveMotion.Tests
{
    public class MotionEditingTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void InsertPoint_SplitsDurationAndKeepsLaterTimes()
        {
            var data = TestData.Line(1f, 0.5f);
            int index = MotionEditing.InsertPoint(data, 0, 0.25f, new Vector3(2.5f, 0f, 0f));
            Assert.AreEqual(1, index);
            Assert.AreEqual(4, data.points.Count);
            Assert.AreEqual(PointType.Auto, data.points[1].type);

            var eval = new MotionEvaluator(data);
            Assert.AreEqual(0.25f, eval.GetSegmentDuration(0), Eps);
            Assert.AreEqual(0.75f, eval.GetSegmentDuration(1), Eps);
            Assert.AreEqual(1f, eval.GetPointTime(2), Eps, "原来的 p1 到达时间不变");
            Assert.AreEqual(1.5f, eval.Duration, Eps);
        }

        [Test]
        public void InsertPoint_KeepsClipAnchors()
        {
            var data = TestData.Line(1f, 1f);
            var track = new AlphaTrack();
            track.AddClip("p1", 0.2f, 0.1f);
            data.tracks.Add(track);
            MotionEditing.InsertPoint(data, 0, 0.5f, new Vector3(5f, 0f, 0f));
            Assert.AreEqual(1.2f, new MotionEvaluator(data).GetClipStart(0, 0), Eps);
        }

        [Test]
        public void AppendPoint_ExtendsAlongLastSegment()
        {
            var data = TestData.Line(1f);
            int index = MotionEditing.AppendPoint(data, 5f);
            Assert.AreEqual(2, index);
            Assert.AreEqual(15f, data.points[2].position.x, Eps);
            Assert.AreEqual(1f, data.points[1].segment.duration, Eps, "新段沿用上一段设置");
        }

        [Test]
        public void RemovePoint_MergesSegmentsAndReanchorsKeepingAbsoluteTime()
        {
            var data = TestData.Line(0.4f, 0.6f, 1f);
            var track = new ScaleTrack();
            track.AddClip("p1", 0.1f, 0.2f);
            track.AddClip("p2", 0f, 0.2f);
            data.tracks.Add(track);
            data.events.Add(new MotionEvent { anchor = "p1", offset = 0.05f });

            Assert.IsTrue(MotionEditing.RemovePoint(data, 1));
            Assert.AreEqual(3, data.points.Count);
            Assert.AreEqual("p0", track.clips[0].anchor);

            var eval = new MotionEvaluator(data);
            Assert.AreEqual(1f, eval.GetSegmentDuration(0), Eps, "0.4 + 0.6 合并");
            Assert.AreEqual(1f, eval.GetPointTime(1), Eps, "p2 到达时间不变");
            Assert.AreEqual(0.5f, eval.GetClipStart(0, 0), Eps, "片段绝对时间不变");
            Assert.AreEqual(1f, eval.GetClipStart(0, 1), Eps);
            Assert.AreEqual(0.45f, eval.GetEventTime(0), Eps);
        }

        [Test]
        public void RemovePoint_RefusesStartPoint()
        {
            var data = TestData.Line(1f);
            Assert.IsFalse(MotionEditing.RemovePoint(data, 0));
            Assert.AreEqual(2, data.points.Count);
        }

        [Test]
        public void RemoveLastPoint_ReanchorsToPrevious()
        {
            var data = TestData.Line(1f, 1f);
            var track = new AlphaTrack();
            track.AddClip("p2", -0.1f, 0.1f);
            data.tracks.Add(track);
            Assert.IsTrue(MotionEditing.RemovePoint(data, 2));
            Assert.AreEqual("p1", track.clips[0].anchor);
            Assert.AreEqual(1.9f, new MotionEvaluator(data).GetClipStart(0, 0), Eps);
        }

        [Test]
        public void SetPointType_FromAutoToFree_KeepsCurveShape()
        {
            var data = new MotionData();
            data.AddPoint(new Vector3(0, 0, 0));
            data.AddPoint(new Vector3(10, 10, 0));
            data.AddPoint(new Vector3(20, 0, 0));
            var before = new MotionEvaluator(data).EvaluatePosition(0.25f);

            MotionEditing.SetPointType(data, 1, PointType.Free);
            var after = new MotionEvaluator(data).EvaluatePosition(0.25f);
            Assert.AreEqual(before.x, after.x, 1e-3f);
            Assert.AreEqual(before.y, after.y, 1e-3f);
        }

        [Test]
        public void SetPointType_FromCornerToSmooth_GivesDefaultTangent()
        {
            var data = TestData.Line(1f, 1f);
            MotionEditing.SetPointType(data, 1, PointType.Smooth);
            Assert.Greater(data.points[1].outTangent.x, 0f);
            Assert.AreEqual(-data.points[1].outTangent.x, data.points[1].inTangent.x, Eps);
        }
    }
}
