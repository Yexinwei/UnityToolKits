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
        float[] m_LengthBefore = Array.Empty<float>();
        float m_TotalLength;
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
        public float TotalDuration => m_Data.loop.count < 0 ? float.PositiveInfinity : LoopStart + LoopLength * Math.Max(1, m_Data.loop.count);

        /// <summary>循环部分的开始时间：整段循环为 0，尾段循环为最后一段路径的开始时间。</summary>
        public float LoopStart
        {
            get
            {
                if (m_Data.loop.scope != MotionLoopScope.LastSegment || m_PointTimes.Length < 2) return 0f;
                return Math.Min(m_PointTimes[m_PointTimes.Length - 2], Duration);
            }
        }

        /// <summary>每次循环的长度：从 <see cref="LoopStart"/> 到单次播放结束。</summary>
        public float LoopLength => Duration - LoopStart;

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
            m_LengthBefore = new float[segments];
            m_TotalLength = 0f;
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
                m_LengthBefore[i] = m_TotalLength;
                m_TotalLength += length;

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

        /// <summary>
        /// 把总时间（含循环）换算成单次播放内的时间。Yoyo 的奇数次循环反向播放。
        /// 尾段循环时，<see cref="LoopStart"/> 之前照常播放，之后只在尾段里循环。
        /// </summary>
        public float ToLocalTime(float time)
        {
            if (Duration <= 0f) return 0f;
            time = Math.Max(0f, Math.Min(time, TotalDuration));
            float start = LoopStart;
            float d = LoopLength;
            if (time <= start || d <= 0f) return Math.Min(time, Duration);
            return start + ToLoopTime(time - start, d);
        }

        /// <summary>循环部分内的换算：time 从循环开始算，d 为每次循环的长度。</summary>
        float ToLoopTime(float time, float d)
        {
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

        /// <summary>
        /// 带终点修正的位置：endCorrection = 实际终点 − 设计时终点（路径空间），按路径进度分摊，
        /// 起点不变、终点恰好落在实际终点，中间的弧线形状保留。终点绑定（<see cref="EndTarget"/>）用它。
        /// </summary>
        public Vector3 EvaluatePosition(float localTime, Vector3 endCorrection)
        {
            return EvaluatePosition(localTime) + endCorrection * GetPathProgress(localTime);
        }

        /// <summary>设计时终点相对起点的偏移。</summary>
        public Vector3 AuthoredEndOffset
        {
            get
            {
                var points = m_Data.points;
                return points.Count > 0 ? points[points.Count - 1].position - points[0].position : Vector3.zero;
            }
        }

        /// <summary>
        /// 路径进度：已走过的弧长 / 总弧长。0 为起点，1 为终点；Back、Elastic 等缓动过冲时会略超出 0~1。
        /// 路径总长为 0 时按时间比例。
        /// </summary>
        public float GetPathProgress(float localTime)
        {
            if (m_SegmentDurations.Length == 0) return 1f;
            if (!Locate(localTime, out int segment, out float fraction)) return 0f;
            if (m_TotalLength <= 1e-6f)
                return PathDuration > 0f ? Math.Min(1f, localTime / PathDuration) : 1f;
            return (m_LengthBefore[segment] + fraction * m_SegmentLengths[segment]) / m_TotalLength;
        }

        Vector3 EvaluatePathPoint(float localTime)
        {
            if (!Locate(localTime, out int segment, out float fraction)) return m_Data.points[0].position;
            return PositionAtFraction(segment, fraction);
        }

        /// <summary>localTime 落在哪一段、段内经缓动后的弧长比例。在起点（或没有段）时返回 false。</summary>
        bool Locate(float localTime, out int segment, out float fraction)
        {
            segment = 0;
            fraction = 0f;
            int segments = m_SegmentDurations.Length;
            if (segments == 0 || localTime <= 0f) return false;

            float segStart = 0f;
            for (int i = 0; i < segments; i++)
            {
                float d = m_SegmentDurations[i];
                bool last = i == segments - 1;
                if (localTime < segStart + d || last)
                {
                    float u = d > 0f ? (localTime - segStart) / d : 1f;
                    segment = i;
                    fraction = Easing.Evaluate(m_Data.points[i].segment?.ease, u);
                    return true;
                }
                segStart += d;
            }
            return false;
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
