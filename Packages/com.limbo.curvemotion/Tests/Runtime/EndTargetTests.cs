using NUnit.Framework;
using Limbo.CurveMotion.Serialization;
using UnityEngine;

namespace Limbo.CurveMotion.Tests
{
    public class EndTargetTests
    {
        const float Eps = 1e-3f;

        static MotionData UnevenLine()
        {
            // 两段长度 10 和 30，各 1 秒、线性。
            var data = new MotionData();
            data.AddPoint(new Vector3(0f, 0f, 0f), PointType.Corner).segment.duration = 1f;
            data.AddPoint(new Vector3(10f, 0f, 0f), PointType.Corner).segment.duration = 1f;
            data.AddPoint(new Vector3(40f, 0f, 0f), PointType.Corner);
            return data;
        }

        [Test]
        public void PathProgress_FollowsArcLengthNotTime()
        {
            var eval = new MotionEvaluator(UnevenLine());
            Assert.AreEqual(0f, eval.GetPathProgress(0f), Eps);
            Assert.AreEqual(0.125f, eval.GetPathProgress(0.5f), Eps);
            Assert.AreEqual(0.25f, eval.GetPathProgress(1f), Eps);
            Assert.AreEqual(0.625f, eval.GetPathProgress(1.5f), Eps);
            Assert.AreEqual(1f, eval.GetPathProgress(2f), Eps);
            Assert.AreEqual(1f, eval.GetPathProgress(5f), Eps, "路径走完后保持 1");
        }

        [Test]
        public void EndCorrection_KeepsStartAndLandsOnActualEnd()
        {
            var eval = new MotionEvaluator(UnevenLine());
            var correction = new Vector3(0f, 8f, 0f);
            Assert.AreEqual(0f, eval.EvaluatePosition(0f, correction).y, Eps, "起点不动");
            Assert.AreEqual(2f, eval.EvaluatePosition(1f, correction).y, Eps, "走了 1/4 的弧长，修正 1/4");
            var end = eval.EvaluatePosition(2f, correction);
            Assert.AreEqual(40f, end.x, Eps);
            Assert.AreEqual(8f, end.y, Eps, "终点落在实际终点");
        }

        [Test]
        public void AuthoredEndOffset_IsRelativeToStart()
        {
            var data = UnevenLine();
            data.points[0].position = new Vector3(5f, 5f, 0f);
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(35f, eval.AuthoredEndOffset.x, Eps);
            Assert.AreEqual(-5f, eval.AuthoredEndOffset.y, Eps);
        }

        [Test]
        public void SetEndOffset_MovesLastPointOnlyWhenChanged()
        {
            var data = UnevenLine();
            Assert.IsTrue(MotionEditing.SetEndOffset(data, new Vector3(30f, 12f, 0f)));
            Assert.AreEqual(12f, data.points[2].position.y, Eps);
            Assert.IsFalse(MotionEditing.SetEndOffset(data, new Vector3(30f, 12f, 0f)), "没变化时不改");
            Assert.AreEqual(10f, data.points[1].position.x, Eps, "中间点不动");
        }

        [Test]
        public void Json_RoundTripsEndTarget()
        {
            var data = UnevenLine();
            data.target = TargetKind.Transform;
            data.endTarget.bound = true;
            data.endTarget.kind = TargetKind.RectTransform;
            data.endTarget.hint = "金币栏图标";

            string json = MotionJson.ToJson(data);
            StringAssert.Contains("\"authoredEnd\": [40, 0, 0]", json);
            var back = MotionJson.FromJson(json);
            Assert.IsTrue(back.endTarget.bound);
            Assert.AreEqual(TargetKind.RectTransform, back.endTarget.kind);
            Assert.AreEqual("金币栏图标", back.endTarget.hint);
        }

        [Test]
        public void Json_WithoutEndTarget_DefaultsToUnbound()
        {
            var data = UnevenLine();
            string json = MotionJson.ToJson(data).Replace("\"endTarget\"", "\"unusedField\"");
            Assert.IsFalse(MotionJson.FromJson(json).endTarget.bound);
        }
    }
}
