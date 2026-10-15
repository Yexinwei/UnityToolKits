using NUnit.Framework;
using UnityEngine;

namespace Limbo.CurveMotion.Tests
{
    public class TimelineEditingTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void AnchorAt_UsesLastPointAtOrBeforeTime()
        {
            var data = TestData.Line(0.5f, 0.5f);
            var eval = new MotionEvaluator(data);

            MotionEditing.AnchorAt(data, eval, 0.7f, out var anchor, out var offset);
            Assert.AreEqual("p1", anchor);
            Assert.AreEqual(0.2f, offset, Eps);

            MotionEditing.AnchorAt(data, eval, 0.5f, out anchor, out offset);
            Assert.AreEqual("p1", anchor, "正好在点上时挂这个点");
            Assert.AreEqual(0f, offset, Eps);

            MotionEditing.AnchorAt(data, eval, 1.3f, out anchor, out offset);
            Assert.AreEqual("p2", anchor, "超过路径终点时挂最后一个点");
            Assert.AreEqual(0.3f, offset, Eps);

            MotionEditing.AnchorAt(data, eval, -1f, out anchor, out offset);
            Assert.AreEqual("p0", anchor);
            Assert.AreEqual(0f, offset, Eps);
        }

        [Test]
        public void SetClipStart_ReanchorsAndKeepsRequestedTime()
        {
            var data = TestData.Line(0.5f, 0.5f);
            var track = new ScaleTrack();
            var clip = track.AddClip("p0", 0f, 0.2f);
            data.tracks.Add(track);
            var eval = new MotionEvaluator(data);

            MotionEditing.SetClipStart(data, eval, clip, 0.8f);
            eval.Rebuild();
            Assert.AreEqual("p1", clip.anchor);
            Assert.AreEqual(0.8f, eval.GetClipStart(0, 0), Eps);
        }

        [Test]
        public void SetSegmentDuration_MovesLaterAnchoredClipsOnly()
        {
            var data = TestData.Line(0.5f, 0.5f);
            var track = new AlphaTrack();
            track.AddClip("p0", 0.1f, 0.1f);
            track.AddClip("p2", 0f, 0.1f);
            data.tracks.Add(track);
            data.events.Add(new MotionEvent { anchor = "p1", offset = 0f });
            var eval = new MotionEvaluator(data);

            MotionEditing.SetSegmentDuration(data, eval, 0, 0.8f);
            eval.Rebuild();
            Assert.AreEqual(0.8f, eval.GetPointTime(1), Eps);
            Assert.AreEqual(1.3f, eval.GetPointTime(2), Eps);
            Assert.AreEqual(0.1f, eval.GetClipStart(0, 0), Eps, "挂在起点的片段不动");
            Assert.AreEqual(1.3f, eval.GetClipStart(0, 1), Eps, "挂在后面点上的片段跟着走");
            Assert.AreEqual(0.8f, eval.GetEventTime(0), Eps);
        }

        [Test]
        public void SetSegmentDuration_SpeedModeKeepsSpeedTiming()
        {
            var data = TestData.Line(0.5f);
            data.points[0].segment.timing = TimingMode.Speed;
            data.points[0].segment.speed = 10f;
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(1f, eval.GetSegmentDuration(0), Eps);

            MotionEditing.SetSegmentDuration(data, eval, 0, 0.25f);
            eval.Rebuild();
            Assert.AreEqual(TimingMode.Speed, data.points[0].segment.timing);
            Assert.AreEqual(40f, data.points[0].segment.speed, Eps);
            Assert.AreEqual(0.25f, eval.GetSegmentDuration(0), Eps);
        }

        [Test]
        public void InsertPointAtTime_NewPointArrivesAtThatTime()
        {
            var data = TestData.Line(1f, 1f);
            int index = MotionEditing.InsertPointAtTime(data, 0.25f);
            Assert.AreEqual(1, index);
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(0.25f, eval.GetPointTime(1), Eps);
            Assert.AreEqual(1f, eval.GetPointTime(2), Eps, "后面的点到达时间不变");
            Assert.AreEqual(2.5f, data.points[1].position.x, 1e-3f, "位置取该时刻的路径位置");

            Assert.AreEqual(-1, MotionEditing.InsertPointAtTime(data, 1f), "正好在已有点上不插入");
            Assert.AreEqual(-1, MotionEditing.InsertPointAtTime(data, 5f), "路径之外不插入");
        }

        [Test]
        public void AddClipAndEvent_AnchorAtTime()
        {
            var data = TestData.Line(0.5f, 0.5f);
            var eval = new MotionEvaluator(data);
            var track = new ScaleTrack();
            data.tracks.Add(track);

            var clip = MotionEditing.AddClip(data, eval, track, 0.6f, 0.3f);
            var e = MotionEditing.AddEvent(data, eval, 0.9f, "OnHit");
            eval.Rebuild();
            Assert.AreEqual(track.DefaultValue, clip.value);
            Assert.AreEqual(0.6f, eval.GetClipStart(0, 0), Eps);
            Assert.AreEqual("p1", e.anchor);
            Assert.AreEqual(0.9f, eval.GetEventTime(0), Eps);
        }

        [Test]
        public void OffsetForAnchor_KeepsAbsoluteTime()
        {
            var data = TestData.Line(0.5f, 0.5f);
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(-0.2f, MotionEditing.OffsetForAnchor(data, eval, 0.8f, "p2"), Eps);
            Assert.AreEqual(0.8f, MotionEditing.OffsetForAnchor(data, eval, 0.8f, "p0"), Eps);
        }

        [Test]
        public void CloneClip_IsDeepCopy()
        {
            var clip = new MotionClip { anchor = "p1", offset = 0.1f, duration = 0.2f, value = new Vector4(2f, 2f, 2f, 0f), desc = "pop" };
            clip.ease.type = EaseType.OutBack;
            var copy = MotionEditing.Clone(clip);
            copy.ease.type = EaseType.Linear;
            Assert.AreEqual(EaseType.OutBack, clip.ease.type);
            Assert.AreEqual(clip.value, copy.value);
            Assert.AreEqual("pop", copy.desc);
        }

        [Test]
        public void TryGetClipRange_ReportsTruncationAndRejectsBadIndex()
        {
            var data = TestData.Line(1f);
            var track = new AlphaTrack();
            track.AddClip("p0", 0f, 0.5f);
            track.AddClip("p0", 0.3f, 0.5f);
            data.tracks.Add(track);
            var eval = new MotionEvaluator(data);

            Assert.IsTrue(eval.TryGetClipRange(0, 0, out float start, out float end));
            Assert.AreEqual(0f, start, Eps);
            Assert.AreEqual(0.3f, end, Eps, "被后一个片段截断");
            Assert.IsFalse(eval.TryGetClipRange(0, 5, out _, out _));
            Assert.IsFalse(eval.TryGetClipRange(3, 0, out _, out _));

            track.clips.Add(new MotionClip());
            Assert.IsFalse(eval.TryGetClipRange(0, 2, out _, out _), "数据改了但还没 Rebuild");
        }

        [Test]
        public void Snap_PicksNearestWithinThreshold()
        {
            var candidates = new[] { 0f, 0.5f, 0.52f, 1f };
            Assert.AreEqual(0.52f, TimeSnapping.Snap(0.515f, candidates, 0.05f, out bool snapped), Eps);
            Assert.IsTrue(snapped);
            Assert.AreEqual(0.7f, TimeSnapping.Snap(0.7f, candidates, 0.05f, out snapped), Eps);
            Assert.IsFalse(snapped);
        }

        [Test]
        public void SnapRange_UsesCloserEdge()
        {
            var candidates = new[] { 1f };
            float start = TimeSnapping.SnapRange(0.68f, 0.3f, candidates, 0.05f, out bool snapped, out float at);
            Assert.IsTrue(snapped);
            Assert.AreEqual(0.7f, start, Eps, "终点吸到 1");
            Assert.AreEqual(1f, at, Eps);

            start = TimeSnapping.SnapRange(0.98f, 0.3f, candidates, 0.05f, out snapped, out at);
            Assert.AreEqual(1f, start, Eps, "起点吸到 1");
        }

        [Test]
        public void CollectTimes_ExcludesDraggedAndMovingItems()
        {
            var data = TestData.Line(0.5f, 0.5f);
            var track = new ScaleTrack();
            var early = track.AddClip("p0", 0.1f, 0.2f);
            var late = track.AddClip("p1", 0.1f, 0.2f);
            data.tracks.Add(track);
            var eval = new MotionEvaluator(data);

            var times = TimeSnapping.CollectTimes(data, eval, excludeClip: early);
            CollectionAssert.DoesNotContain(times, 0.1f);
            CollectionAssert.Contains(times, 0.6f);

            times = TimeSnapping.CollectTimes(data, eval, movingFromPoint: 1);
            CollectionAssert.Contains(times, 0.1f);
            CollectionAssert.DoesNotContain(times, 0.5f, "被拖动的点自己不算");
            CollectionAssert.DoesNotContain(times, 0.6f, "挂在被拖动点上的片段会一起动");
            Assert.That(late.anchor, Is.EqualTo("p1"));
        }
    }
}
