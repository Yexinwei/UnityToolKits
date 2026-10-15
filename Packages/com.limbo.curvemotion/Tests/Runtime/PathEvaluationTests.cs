using NUnit.Framework;
using UnityEngine;

namespace Limbo.CurveMotion.Tests
{
    public class PathEvaluationTests
    {
        const float Eps = 1e-3f;

        [Test]
        public void StraightLine_ConstantSpeed()
        {
            var eval = new MotionEvaluator(TestData.Line(1f));
            Assert.AreEqual(10f, eval.GetSegmentLength(0), Eps);
            Assert.AreEqual(1f, eval.Duration, Eps);
            Assert.AreEqual(2.5f, eval.EvaluatePosition(0.25f).x, 0.01f);
            Assert.AreEqual(5f, eval.EvaluatePosition(0.5f).x, 0.01f);
            Assert.AreEqual(10f, eval.EvaluatePosition(1f).x, Eps);
        }

        [Test]
        public void Segments_HaveIndependentDurations()
        {
            var eval = new MotionEvaluator(TestData.Line(0.4f, 1.6f));
            Assert.AreEqual(0f, eval.GetPointTime(0), Eps);
            Assert.AreEqual(0.4f, eval.GetPointTime(1), Eps);
            Assert.AreEqual(2f, eval.GetPointTime(2), Eps);
            Assert.AreEqual(2f, eval.PathDuration, Eps);
            // 第二段中点：x = 10 + 5
            Assert.AreEqual(15f, eval.EvaluatePosition(0.4f + 0.8f).x, 0.01f);
        }

        [Test]
        public void SpeedMode_DerivesDurationFromLength()
        {
            var data = TestData.Line(1f);
            data.points[0].segment.timing = TimingMode.Speed;
            data.points[0].segment.speed = 5f;
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(2f, eval.GetSegmentDuration(0), Eps);
        }

        [Test]
        public void SegmentEase_ShapesProgress()
        {
            var data = TestData.Line(1f);
            data.points[0].segment.ease.type = EaseType.OutQuad;
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(7.5f, eval.EvaluatePosition(0.5f).x, 0.01f);
        }

        [Test]
        public void AutoPoints_CurvePassesThroughEveryPoint()
        {
            var data = new MotionData();
            data.AddPoint(new Vector3(0, 0, 0));
            data.AddPoint(new Vector3(10, 10, 0));
            data.AddPoint(new Vector3(20, 0, 0));
            var eval = new MotionEvaluator(data);
            var mid = eval.EvaluatePosition(eval.GetPointTime(1));
            Assert.AreEqual(10f, mid.x, Eps);
            Assert.AreEqual(10f, mid.y, Eps);
            Assert.Greater(eval.GetSegmentLength(0), 14.14f, "曲线段应长于直线距离");
        }

        [Test]
        public void SmoothPoint_MirrorsTangent()
        {
            var data = TestData.Line(1f, 1f);
            data.points[1].type = PointType.Smooth;
            data.points[1].outTangent = new Vector3(2, 3, 0);
            PathMath.ResolveTangents(data.points, 1, out var inT, out var outT);
            Assert.AreEqual(-2f, inT.x, Eps);
            Assert.AreEqual(-3f, inT.y, Eps);
            Assert.AreEqual(2f, outT.x, Eps);
        }

        [Test]
        public void Position_IsRelativeToFirstPoint()
        {
            var data = TestData.Line(1f);
            data.points[0].position = new Vector3(100, 0, 0);
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(0f, eval.EvaluatePosition(0f).x, Eps);
            Assert.AreEqual(-90f, eval.EvaluatePosition(1f).x, Eps);
        }

        [Test]
        public void SinglePoint_HasZeroPathDuration()
        {
            var data = new MotionData();
            data.AddPoint(Vector3.zero);
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(0f, eval.PathDuration, Eps);
            Assert.AreEqual(0f, eval.EvaluatePosition(1f).x, Eps);
        }

        [Test]
        public void Rebuild_PicksUpChanges()
        {
            var data = TestData.Line(1f);
            var eval = new MotionEvaluator(data);
            data.points[1].position = new Vector3(20, 0, 0);
            eval.Rebuild();
            Assert.AreEqual(20f, eval.GetSegmentLength(0), Eps);
        }
    }
}
