using System;
using System.Collections.Generic;
using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>
    /// 把 <see cref="MotionData"/> 解析成可按时间采样的形式。数据改动后调用 <see cref="Rebuild"/>。
    /// 编辑器预览与导出器都用它，保证两边结果一致。
    /// </summary>
    public sealed class MotionEvaluator
    {
        struct ResolvedClip
        {
            public MotionClip clip;
            public float start;
            /// <summary>实际结束时间：被同轨道后一个片段覆盖时提前截断。</summary>
            public float end;
        }

        readonly MotionData m_Data;
        readonly List<string> m_Warnings = new List<string>();

        float[] m_SegmentDurations = Array.Empty<float>();
        float[] m_SegmentLengths = Array.Empty<float>();
        float[][] m_LengthTables = Array.Empty<float[]>();
        Vector3[] m_Controls = Array.Empty<Vector3>();
        float[] m_PointTimes = Array.Empty<float>();
        ResolvedClip[][] m_Tracks = Array.Empty<ResolvedClip[]>();
        float[] m_EventTimes = Array.Empty<float>();

        public MotionEvaluator(MotionData data)
        {
            m_Data = data ?? throw new ArgumentNullException(nameof(data));
            Rebuild();
        }

        public MotionData Data => m_Data;

        /// <summary>单次播放时长（路径、片段、事件三者的最晚时间）。</summary>
        public float Duration { get; private set; }

        /// <summary>路径部分的时长。</summary>
        public float PathDuration { get; private set; }

        /// <summary>含循环的总时长，无限循环为 +∞。</summary>
        public float TotalDuration => m_Data.loop.count < 0 ? float.PositiveInfinity : Duration * Math.Max(1, m_Data.loop.count);

        public int SegmentCount => m_SegmentDurations.Length;

        /// <summary>最近一次 Rebuild 发现的问题（如锚点丢失）。</summary>
        public IReadOnlyList<string> Warnings => m_Warnings;

        public float GetSegmentDuration(int segment) => m_SegmentDurations[segment];
        public float GetSegmentLength(int segment) => m_SegmentLengths[segment];
        public float GetPointTime(int point) => m_PointTimes[point];
        public float GetEventTime(int index) => m_EventTimes[index];
        public float GetClipStart(int track, int clip) => FindResolved(track, clip).start;
        public float GetClipEnd(int track, int clip) => FindResolved(track, clip).end;

        /// <summary>
        /// 片段的开始时间与实际结束时间（被覆盖时截断）。索引越界或数据改动后尚未 Rebuild 时返回 false，
        /// 供编辑器在绘制时容错。
        /// </summary>
        public bool TryGetClipRange(int track, int clip, out float start, out float end)
        {
            start = end = 0f;
            if (track < 0 || track >= m_Data.tracks.Count || track >= m_Tracks.Length) return false;
            var clips = m_Data.tracks[track]?.clips;
            if (clips == null || clip < 0 || clip >= clips.Count) return false;
            var target = clips[clip];
            foreach (var rc in m_Tracks[track])
            {
                if (!ReferenceEquals(rc.clip, target)) continue;
                start = rc.start;
                end = rc.end;
                return true;
            }
            return false;
        }

        public void GetSegmentControls(int segment, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
        {
            int i = segment * 4;
            p0 = m_Controls[i];
            p1 = m_Controls[i + 1];
            p2 = m_Controls[i + 2];
            p3 = m_Controls[i + 3];
        }

        /// <summary>锚点到达时间 + 偏移。锚点不存在时按起点计算。</summary>
        public float ResolveAnchorTime(string anchor, float offset)
        {
            int index = m_Data.FindPointIndex(anchor);
            float baseTime = index >= 0 && index < m_PointTimes.Length ? m_PointTimes[index] : 0f;
            return Math.Max(0f, baseTime + offset);
        }

        public void Rebuild()
        {
            m_Warnings.Clear();
            BuildPath();
            BuildTracks();
            BuildEvents();

            float duration = PathDuration;
            foreach (var track in m_Tracks)
                foreach (var rc in track)
                    duration = Math.Max(duration, rc.end);
            foreach (var t in m_EventTimes)
                duration = Math.Max(duration, t);
            Duration = duration;
        }

        void BuildPath()
        {
            var points = m_Data.points;
            int segments = Math.Max(0, points.Count - 1);
            m_SegmentDurations = new float[segments];
            m_SegmentLengths = new float[segments];
            m_LengthTables = new float[segments][];
            m_Controls = new Vector3[segments * 4];
            m_PointTimes = new float[points.Count];

            float time = 0f;
            for (int i = 0; i < segments; i++)
            {
                PathMath.GetSegmentControls(points, i, out var p0, out var p1, out var p2, out var p3);
                m_Controls[i * 4] = p0;
                m_Controls[i * 4 + 1] = p1;
                m_Controls[i * 4 + 2] = p2;
                m_Controls[i * 4 + 3] = p3;

                var table = PathMath.BuildLengthTable(p0, p1, p2, p3);
                m_LengthTables[i] = table;
                float length = table[table.Length - 1];
                m_SegmentLengths[i] = length;

                var seg = points[i].segment ?? new SegmentSettings();
                float duration = seg.timing == TimingMode.Speed
                    ? (seg.speed > 0f ? length / seg.speed : 0f)
                    : Math.Max(0f, seg.duration);
                m_SegmentDurations[i] = duration;

                m_PointTimes[i] = time;
                time += duration;
            }
            if (points.Count > 0) m_PointTimes[points.Count - 1] = time;
            PathDuration = time;
        }

        void BuildTracks()
        {
            var tracks = m_Data.tracks;
            m_Tracks = new ResolvedClip[tracks.Count][];
            for (int ti = 0; ti < tracks.Count; ti++)
            {
                var track = tracks[ti];
                var clips = track?.clips;
                if (clips == null || clips.Count == 0)
                {
                    m_Tracks[ti] = Array.Empty<ResolvedClip>();
                    continue;
                }

                var resolved = new ResolvedClip[clips.Count];
                for (int ci = 0; ci < clips.Count; ci++)
                {
                    var clip = clips[ci];
                    CheckAnchor(clip.anchor, $"轨道 {track.name} 的第 {ci + 1} 个片段");
                    float start = ResolveAnchorTime(clip.anchor, clip.offset);
                    resolved[ci] = new ResolvedClip { clip = clip, start = start, end = start + Math.Max(0f, clip.duration) };
                }

                // 按开始时间排序；同时开始时列表靠后的视为“后面的”。后面的片段覆盖前面的，前者在后者开始处截断。
                var order = new int[resolved.Length];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                Array.Sort(order, (a, b) =>
                {
                    int c = resolved[a].start.CompareTo(resolved[b].start);
                    return c != 0 ? c : a.CompareTo(b);
                });
                var sorted = new ResolvedClip[resolved.Length];
                for (int i = 0; i < order.Length; i++) sorted[i] = resolved[order[i]];
                for (int i = 0; i < sorted.Length - 1; i++)
                    if (sorted[i + 1].start < sorted[i].end)
                        sorted[i].end = sorted[i + 1].start;
                m_Tracks[ti] = sorted;
            }
        }

        void BuildEvents()
        {
            var events = m_Data.events;
            m_EventTimes = new float[events.Count];
            for (int i = 0; i < events.Count; i++)
            {
                CheckAnchor(events[i].anchor, $"事件 {events[i].name}");
                m_EventTimes[i] = ResolveAnchorTime(events[i].anchor, events[i].offset);
            }
        }

        void CheckAnchor(string anchor, string owner)
        {
            if (m_Data.FindPointIndex(anchor) < 0)
                m_Warnings.Add($"{owner} 的锚点 '{anchor}' 不存在，已按起点计算");
        }

        ResolvedClip FindResolved(int track, int clip)
        {
            var target = m_Data.tracks[track].clips[clip];
            foreach (var rc in m_Tracks[track])
                if (ReferenceEquals(rc.clip, target)) return rc;
            throw new ArgumentOutOfRangeException(nameof(clip));
        }

        /// <summary>把总时间（含循环）换算成单次播放内的时间。Yoyo 的奇数次循环反向播放。</summary>
        public float ToLocalTime(float time)
        {
            float d = Duration;
            if (d <= 0f) return 0f;
            time = Math.Max(0f, Math.Min(time, TotalDuration));
            int loop = (int)Math.Floor(time / d);
            float local;
            int count = m_Data.loop.count;
            if (count > 0 && loop >= count)
            {
                loop = count - 1;
                local = d;
            }
            else
            {
                local = time - loop * d;
            }
            if (m_Data.loop.type == MotionLoopType.Yoyo && (loop & 1) == 1) local = d - local;
            return local;
        }

        /// <summary>单次播放内 localTime 时刻相对起点（第一个路径点）的位置偏移。</summary>
        public Vector3 EvaluatePosition(float localTime)
        {
            if (m_Data.points.Count == 0) return Vector3.zero;
            return EvaluatePathPoint(localTime) - m_Data.points[0].position;
        }

        Vector3 EvaluatePathPoint(float localTime)
        {
            int segments = m_SegmentDurations.Length;
            if (segments == 0 || localTime <= 0f) return m_Data.points[0].position;

            float segStart = 0f;
            for (int i = 0; i < segments; i++)
            {
                float d = m_SegmentDurations[i];
                bool last = i == segments - 1;
                if (localTime < segStart + d || last)
                {
                    float u = d > 0f ? (localTime - segStart) / d : 1f;
                    var ease = m_Data.points[i].segment?.ease;
                    float fraction = Easing.Evaluate(ease, u);
                    return PositionAtFraction(i, fraction);
                }
                segStart += d;
            }
            return m_Controls[m_Controls.Length - 1];
        }

        /// <summary>段内弧长比例（可超出 0~1，用于 Back/Elastic 的过冲）→ 位置。</summary>
        Vector3 PositionAtFraction(int segment, float fraction)
        {
            GetSegmentControls(segment, out var p0, out var p1, out var p2, out var p3);
            var table = m_LengthTables[segment];
            if (fraction >= 0f && fraction <= 1f)
                return PathMath.Bezier(p0, p1, p2, p3, PathMath.DistanceFractionToParam(table, fraction));

            // 过冲部分（Back/Elastic 缓动）沿端点切线方向线性外推。
            float length = table[table.Length - 1];
            if (fraction > 1f)
            {
                var dir = TangentDirection(p3 - p2, p3 - p0);
                return p3 + dir * ((fraction - 1f) * length);
            }
            var startDir = TangentDirection(p1 - p0, p3 - p0);
            return p0 + startDir * (fraction * length);
        }

        static Vector3 TangentDirection(Vector3 preferred, Vector3 fallback)
        {
            if (preferred.sqrMagnitude > 1e-12f) return preferred.normalized;
            return fallback.sqrMagnitude > 1e-12f ? fallback.normalized : Vector3.zero;
        }

        /// <summary>
        /// 单次播放内 localTime 时刻某条轨道的绝对值。initial 为对象的初始属性值
        /// （缩放 = localScale，旋转 = localEulerAngles，透明度 = alpha，颜色 = rgb）。
        /// </summary>
        public Vector4 EvaluateTrack(int trackIndex, float localTime, Vector4 initial)
        {
            var track = m_Data.tracks[trackIndex];
            var current = initial;
            if (track == null || !track.enabled) return current;

            foreach (var rc in m_Tracks[trackIndex])
            {
                if (localTime < rc.start) break;
                var from = current;
                var to = track.ResolveTarget(initial, rc.clip.value);
                float t = Math.Min(localTime, rc.end);
                float duration = rc.clip.duration;
                float u = duration > 0f ? (t - rc.start) / duration : 1f;
                current = Vector4.LerpUnclamped(from, to, Easing.Evaluate(rc.clip.ease, u));
                if (localTime < rc.end) break;
            }
            return current;
        }
    }
}
