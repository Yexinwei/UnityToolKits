using System;
using System.Collections.Generic;

namespace Limbo.CurveMotion
{
    /// <summary>
    /// 时间轴吸附：拖动片段、事件或段分界时，靠近路径点、事件、其他片段边界或播放头就吸过去。
    /// 编辑器把像素阈值换算成秒传进来。
    /// </summary>
    public static class TimeSnapping
    {
        /// <summary>返回 candidates 中离 time 最近且距离不超过 threshold 的值；没有则原样返回。</summary>
        public static float Snap(float time, IReadOnlyList<float> candidates, float threshold, out bool snapped)
        {
            snapped = false;
            float best = time;
            float bestDistance = threshold;
            for (int i = 0; i < candidates.Count; i++)
            {
                float d = Math.Abs(candidates[i] - time);
                if (d > bestDistance) continue;
                bestDistance = d;
                best = candidates[i];
                snapped = true;
            }
            return best;
        }

        /// <summary>
        /// 整体移动区间 [start, start + length]：起点、终点都尝试吸附，取需要挪动更少的一端，返回新的起点。
        /// </summary>
        public static float SnapRange(float start, float length, IReadOnlyList<float> candidates, float threshold, out bool snapped, out float snapTime)
        {
            float s = Snap(start, candidates, threshold, out bool startSnapped);
            float e = Snap(start + length, candidates, threshold, out bool endSnapped);
            snapped = startSnapped || endSnapped;
            if (startSnapped && (!endSnapped || Math.Abs(s - start) <= Math.Abs(e - start - length)))
            {
                snapTime = s;
                return s;
            }
            if (endSnapped)
            {
                snapTime = e;
                return e - length;
            }
            snapTime = start;
            return start;
        }

        /// <summary>
        /// 收集吸附目标：0、各路径点到达时间、事件时间、所有片段的起止时间。
        /// excludeClip / excludeEvent 是正在拖动的对象，不吸自己。
        /// movingFromPoint：拖动第 n 个点的到达时间时，n 及之后的点和挂在它们上的片段、事件会一起移动，不作为目标。
        /// </summary>
        public static List<float> CollectTimes(MotionData data, MotionEvaluator eval,
            MotionClip excludeClip = null, MotionEvent excludeEvent = null, int movingFromPoint = int.MaxValue)
        {
            var times = new List<float> { 0f };
            for (int i = 0; i < data.points.Count && i < movingFromPoint; i++)
                times.Add(eval.GetPointTime(i));

            for (int i = 0; i < data.events.Count; i++)
            {
                var e = data.events[i];
                if (ReferenceEquals(e, excludeEvent) || Moves(data, e.anchor, movingFromPoint)) continue;
                times.Add(eval.GetEventTime(i));
            }

            for (int t = 0; t < data.tracks.Count; t++)
            {
                var clips = data.tracks[t]?.clips;
                if (clips == null) continue;
                for (int c = 0; c < clips.Count; c++)
                {
                    var clip = clips[c];
                    if (ReferenceEquals(clip, excludeClip) || Moves(data, clip.anchor, movingFromPoint)) continue;
                    if (!eval.TryGetClipRange(t, c, out float start, out float end)) continue;
                    times.Add(start);
                    times.Add(start + clip.duration);
                    if (end < start + clip.duration) times.Add(end);
                }
            }
            return times;
        }

        static bool Moves(MotionData data, string anchor, int movingFromPoint)
        {
            if (movingFromPoint == int.MaxValue) return false;
            return data.FindPointIndex(anchor) >= movingFromPoint;
        }
    }
}
