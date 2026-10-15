using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Limbo.CurveMotion.Tests
{
    public class TrackEvaluationTests
    {
        const float Eps = 1e-4f;

        static MotionClip Linear(MotionClip clip)
        {
            clip.ease.type = EaseType.Linear;
            return clip;
        }

        [Test]
        public void Registry_FindsBuiltinTracks()
        {
            var ids = MotionTrackRegistry.TypeIds.ToList();
            CollectionAssert.IsSupersetOf(ids, new[] { "scale", "rotation", "alpha", "color" });
            Assert.IsTrue(MotionTrackRegistry.TryCreate("scale", out var track));
            Assert.IsInstanceOf<ScaleTrack>(track);
            Assert.AreEqual("scale", track.TypeId);
        }

        [Test]
        public void Clip_StartFollowsAnchorPoint()
        {
            var data = TestData.Line(0.4f, 1f);
            var track = new AlphaTrack();
            track.AddClip("p1", 0.1f, 0.2f);
            data.tracks.Add(track);
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(0.5f, eval.GetClipStart(0, 0), Eps);

            data.points[0].segment.duration = 1f;
            eval.Rebuild();
            Assert.AreEqual(1.1f, eval.GetClipStart(0, 0), Eps);
        }

        [Test]
        public void ScaleClip_MultipliesInitialValue()
        {
            var data = TestData.Line(1f);
            var track = new ScaleTrack();
            Linear(track.AddClip("p0", 0f, 1f)).value = new Vector4(1.5f, 1.5f, 1.5f, 0f);
            data.tracks.Add(track);
            var eval = new MotionEvaluator(data);
            var initial = new Vector4(2f, 2f, 2f, 0f);
            Assert.AreEqual(2f, eval.EvaluateTrack(0, 0f, initial).x, Eps);
            Assert.AreEqual(2.5f, eval.EvaluateTrack(0, 0.5f, initial).x, Eps);
            Assert.AreEqual(3f, eval.EvaluateTrack(0, 1f, initial).x, Eps);
            Assert.AreEqual(3f, eval.EvaluateTrack(0, 5f, initial).x, Eps);
        }

        [Test]
        public void RotationClip_AddsDelta()
        {
            var data = TestData.Line(1f);
            var track = new RotationTrack();
            Linear(track.AddClip("p0", 0f, 1f)).value = new Vector4(0f, 0f, 90f, 0f);
            data.tracks.Add(track);
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(100f, eval.EvaluateTrack(0, 1f, new Vector4(0f, 0f, 10f, 0f)).z, Eps);
        }

        [Test]
        public void SequentialClips_ChainFromCurrentValue()
        {
            var data = TestData.Line(2f);
            var track = new AlphaTrack();
            Linear(track.AddClip("p0", 0f, 1f)).value = new Vector4(0f, 0f, 0f, 0f);
            Linear(track.AddClip("p0", 1.5f, 0.5f)).value = new Vector4(1f, 0f, 0f, 0f);
            data.tracks.Add(track);
            var eval = new MotionEvaluator(data);
            var initial = new Vector4(1f, 0f, 0f, 0f);
            Assert.AreEqual(0.5f, eval.EvaluateTrack(0, 0.5f, initial).x, Eps);
            Assert.AreEqual(0f, eval.EvaluateTrack(0, 1.25f, initial).x, Eps, "两个片段之间保持前一个的结果");
            Assert.AreEqual(0.5f, eval.EvaluateTrack(0, 1.75f, initial).x, Eps);
        }

        [Test]
        public void OverlappingClip_OverridesAndTruncatesEarlier()
        {
            var data = TestData.Line(2f);
            var track = new AlphaTrack();
            Linear(track.AddClip("p0", 0f, 1f)).value = new Vector4(2f, 0f, 0f, 0f);
            Linear(track.AddClip("p0", 0.5f, 0.5f)).value = new Vector4(0f, 0f, 0f, 0f);
            data.tracks.Add(track);
            var eval = new MotionEvaluator(data);
            var initial = new Vector4(1f, 0f, 0f, 0f);

            Assert.AreEqual(0.5f, eval.GetClipEnd(0, 0), Eps, "前一个片段在后一个开始处截断");
            Assert.AreEqual(1.5f, eval.EvaluateTrack(0, 0.5f, initial).x, Eps);
            // 后一个片段从 1.5 走向 0，一半处 = 0.75
            Assert.AreEqual(0.75f, eval.EvaluateTrack(0, 0.75f, initial).x, Eps);
            Assert.AreEqual(0f, eval.EvaluateTrack(0, 1f, initial).x, Eps);
        }

        [Test]
        public void DisabledTrack_ReturnsInitial()
        {
            var data = TestData.Line(1f);
            var track = new AlphaTrack { enabled = false };
            Linear(track.AddClip("p0", 0f, 1f)).value = Vector4.zero;
            data.tracks.Add(track);
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(1f, eval.EvaluateTrack(0, 1f, new Vector4(1f, 0f, 0f, 0f)).x, Eps);
        }

        [Test]
        public void MissingAnchor_FallsBackToStartWithWarning()
        {
            var data = TestData.Line(1f);
            var track = new AlphaTrack();
            track.AddClip("gone", 0.2f, 0.1f);
            data.tracks.Add(track);
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(0.2f, eval.GetClipStart(0, 0), Eps);
            Assert.AreEqual(1, eval.Warnings.Count);
        }

        [Test]
        public void Duration_IncludesClipsAndEventsBeyondPath()
        {
            var data = TestData.Line(1f);
            var track = new ScaleTrack();
            track.AddClip("p1", 0.5f, 0.5f);
            data.tracks.Add(track);
            data.events.Add(new MotionEvent { anchor = "p1", offset = 0.2f });
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(2f, eval.Duration, Eps);
            Assert.AreEqual(1.2f, eval.GetEventTime(0), Eps);
        }

        [Test]
        public void YoyoLoop_PlaysBackwardOnOddLoops()
        {
            var data = TestData.Line(1f);
            data.loop.count = 2;
            data.loop.type = MotionLoopType.Yoyo;
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(2f, eval.TotalDuration, Eps);
            Assert.AreEqual(0.25f, eval.ToLocalTime(0.25f), Eps);
            Assert.AreEqual(0.75f, eval.ToLocalTime(1.25f), Eps);
            Assert.AreEqual(0f, eval.ToLocalTime(2f), Eps);
        }

        [Test]
        public void RestartLoop_WrapsAndClampsAtEnd()
        {
            var data = TestData.Line(1f);
            data.loop.count = 3;
            var eval = new MotionEvaluator(data);
            Assert.AreEqual(0.5f, eval.ToLocalTime(2.5f), Eps);
            Assert.AreEqual(1f, eval.ToLocalTime(10f), Eps);
        }

        [Test]
        public void InfiniteLoop_NeverClamps()
        {
            var data = TestData.Line(1f);
            data.loop.count = -1;
            var eval = new MotionEvaluator(data);
            Assert.IsTrue(float.IsPositiveInfinity(eval.TotalDuration));
            Assert.AreEqual(0.5f, eval.ToLocalTime(100.5f), Eps);
        }
    }
}
