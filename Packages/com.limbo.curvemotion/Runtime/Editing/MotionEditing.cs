using System;
using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>
    /// 对 MotionData 的结构性编辑（插入/删除路径点、切换点类型）。
    /// 这些操作会同时维护分段时长和锚点，保证已有片段、事件的绝对时间不乱跳。
    /// 编辑器负责 Undo，这里只改数据。
    /// </summary>
    public static class MotionEditing
    {
        /// <summary>
        /// 在第 segment 段内插入一个 Auto 点。fraction 为该段弧长比例 (0~1)，
        /// 时长模式下原段时长按比例拆给前后两段，后续各点的到达时间不变。返回新点索引。
        /// </summary>
        public static int InsertPoint(MotionData data, int segment, float fraction, Vector3 position)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (segment < 0 || segment >= data.points.Count - 1) throw new ArgumentOutOfRangeException(nameof(segment));
            fraction = Mathf.Clamp01(fraction);

            var prev = data.points[segment];
            var seg = prev.segment ?? (prev.segment = new SegmentSettings());
            var point = new PathPoint
            {
                id = MotionData.NewId(),
                position = position,
                type = PointType.Auto,
                segment = Clone(seg),
            };
            point.segment.desc = null;
            if (seg.timing == TimingMode.Duration)
            {
                float total = seg.duration;
                seg.duration = total * fraction;
                point.segment.duration = total - seg.duration;
            }
            data.points.Insert(segment + 1, point);
            return segment + 1;
        }

        /// <summary>在路径末尾追加一个点，沿最后一段方向延伸 distance。返回新点索引。</summary>
        public static int AppendPoint(MotionData data, float distance)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var points = data.points;
            Vector3 position;
            if (points.Count == 0) position = Vector3.zero;
            else if (points.Count == 1) position = points[0].position + new Vector3(distance, 0f, 0f);
            else
            {
                var last = points[points.Count - 1].position;
                var dir = last - points[points.Count - 2].position;
                position = last + (dir.sqrMagnitude > 1e-8f ? dir.normalized : new Vector3(1f, 0f, 0f)) * distance;
            }

            var point = new PathPoint { id = MotionData.NewId(), position = position, type = PointType.Auto };
            // 新增的一段（原末点 → 新点）沿用上一段的计时与缓动。
            if (points.Count >= 2)
            {
                var template = points[points.Count - 2].segment ?? new SegmentSettings();
                var segment = Clone(template);
                segment.desc = null;
                points[points.Count - 1].segment = segment;
            }
            points.Add(point);
            return points.Count - 1;
        }

        /// <summary>
        /// 删除路径点。起点（索引 0）不能删。
        /// 被删点的入段与出段合并为一段（时长模式下时长相加）；
        /// 锚定在被删点上的片段和事件改挂到前一个点，并保持原来的绝对时间。
        /// </summary>
        public static bool RemovePoint(MotionData data, int index)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var points = data.points;
            if (index <= 0 || index >= points.Count) return false;

            var eval = new MotionEvaluator(data);
            var removed = points[index];
            var prev = points[index - 1];
            float shift = eval.GetPointTime(index) - eval.GetPointTime(index - 1);

            bool hasOutgoing = index < points.Count - 1;
            if (hasOutgoing && prev.segment != null && prev.segment.timing == TimingMode.Duration)
                prev.segment.duration = eval.GetSegmentDuration(index - 1) + eval.GetSegmentDuration(index);

            foreach (var track in data.tracks)
            {
                if (track?.clips == null) continue;
                foreach (var clip in track.clips)
                {
                    if (clip.anchor != removed.id) continue;
                    clip.anchor = prev.id;
                    clip.offset += shift;
                }
            }
            foreach (var e in data.events)
            {
                if (e.anchor != removed.id) continue;
                e.anchor = prev.id;
                e.offset += shift;
            }

            points.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// 切换点类型。切到 Smooth/Free 时用当前实际切线初始化存储值，曲线形状不跳变。
        /// </summary>
        public static void SetPointType(MotionData data, int index, PointType type)
        {
            var point = data.points[index];
            if (point.type == type) return;
            PathMath.ResolveTangents(data.points, index, out var inT, out var outT);
            if (type == PointType.Smooth || type == PointType.Free)
            {
                if (outT.sqrMagnitude < 1e-8f && inT.sqrMagnitude < 1e-8f)
                    DefaultTangents(data, index, out inT, out outT);
                point.outTangent = outT;
                point.inTangent = type == PointType.Smooth ? -outT : inT;
            }
            point.type = type;
        }

        /// <summary>尖角点切到带切线的类型时，给一个沿相邻点方向、长度为相邻段 1/3 的默认切线。</summary>
        static void DefaultTangents(MotionData data, int index, out Vector3 inT, out Vector3 outT)
        {
            var points = data.points;
            var p = points[index].position;
            var prev = index > 0 ? points[index - 1].position : p;
            var next = index < points.Count - 1 ? points[index + 1].position : p;
            var dir = next - prev;
            outT = dir * (1f / 6f);
            inT = -outT;
        }

        /// <summary>
        /// 在时间 time 处插入一个点（落在某段内部时），位置取该时刻的路径位置，
        /// 时长模式下按时间比例拆分原段，新点恰好在 time 到达。不在任何段内部时返回 -1。
        /// </summary>
        public static int InsertPointAtTime(MotionData data, float time)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var eval = new MotionEvaluator(data);
            for (int s = 0; s < eval.SegmentCount; s++)
            {
                float t0 = eval.GetPointTime(s), t1 = eval.GetPointTime(s + 1);
                if (time <= t0 || time >= t1) continue;
                var position = data.points[0].position + eval.EvaluatePosition(time);
                return InsertPoint(data, s, (time - t0) / (t1 - t0), position);
            }
            return -1;
        }

        /// <summary>
        /// 修改第 segment 段的时长。速度模式的段改为反算速度（仍保持速度模式），弧长为 0 时切回时长模式。
        /// 后面各点的到达时间随之平移，锚定在这些点上的片段和事件跟着移动。
        /// </summary>
        public static void SetSegmentDuration(MotionData data, MotionEvaluator eval, int segment, float duration)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (segment < 0 || segment >= data.points.Count - 1) throw new ArgumentOutOfRangeException(nameof(segment));
            var point = data.points[segment];
            var seg = point.segment ?? (point.segment = new SegmentSettings());
            duration = Math.Max(0f, duration);
            if (seg.timing == TimingMode.Speed)
            {
                float length = eval.GetSegmentLength(segment);
                if (length > 0f && duration > 0f)
                {
                    seg.speed = length / duration;
                    return;
                }
                seg.timing = TimingMode.Duration;
            }
            seg.duration = duration;
        }

        /// <summary>
        /// 时间 time 对应的锚点：到达时间 ≤ time 的最后一个路径点（同时到达取靠后的），offset = time − 该点时间。
        /// 这样片段/事件挂在它前面最近的点上，之后修改更早的段时长，它会跟着这个点移动。
        /// </summary>
        public static void AnchorAt(MotionData data, MotionEvaluator eval, float time, out string anchor, out float offset)
        {
            time = Math.Max(0f, time);
            if (data.points.Count == 0)
            {
                anchor = null;
                offset = time;
                return;
            }
            int best = 0;
            for (int i = 1; i < data.points.Count; i++)
                if (eval.GetPointTime(i) <= time + TimeEpsilon) best = i;
            anchor = data.points[best].id;
            offset = Math.Max(0f, time - eval.GetPointTime(best));
        }

        /// <summary>把片段移到绝对开始时间 start，并重新锚定到它前面最近的点。</summary>
        public static void SetClipStart(MotionData data, MotionEvaluator eval, MotionClip clip, float start)
        {
            AnchorAt(data, eval, start, out clip.anchor, out clip.offset);
        }

        /// <summary>把事件移到绝对时间 time，并重新锚定到它前面最近的点。</summary>
        public static void SetEventTime(MotionData data, MotionEvaluator eval, MotionEvent motionEvent, float time)
        {
            AnchorAt(data, eval, time, out motionEvent.anchor, out motionEvent.offset);
        }

        /// <summary>改挂到另一个锚点，保持绝对时间不变；新锚点在原时间之后时 offset 为负。</summary>
        public static float OffsetForAnchor(MotionData data, MotionEvaluator eval, float absoluteTime, string anchor)
        {
            int index = data.FindPointIndex(anchor);
            float baseTime = index >= 0 ? eval.GetPointTime(index) : 0f;
            return absoluteTime - baseTime;
        }

        /// <summary>在 start 处给轨道加一个片段，值为轨道默认值。</summary>
        public static MotionClip AddClip(MotionData data, MotionEvaluator eval, MotionTrack track, float start, float duration)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            AnchorAt(data, eval, start, out var anchor, out var offset);
            return track.AddClip(anchor, offset, Math.Max(0f, duration));
        }

        /// <summary>在 time 处加一个事件。</summary>
        public static MotionEvent AddEvent(MotionData data, MotionEvaluator eval, float time, string name)
        {
            AnchorAt(data, eval, time, out var anchor, out var offset);
            var motionEvent = new MotionEvent { anchor = anchor, offset = offset, name = name };
            data.events.Add(motionEvent);
            return motionEvent;
        }

        public static MotionClip Clone(MotionClip c)
        {
            return new MotionClip
            {
                anchor = c.anchor,
                offset = c.offset,
                duration = c.duration,
                ease = c.ease?.Clone() ?? new EaseSpec(),
                value = c.value,
                desc = c.desc,
            };
        }

        public static MotionEvent Clone(MotionEvent e)
        {
            return new MotionEvent { anchor = e.anchor, offset = e.offset, name = e.name, message = e.message };
        }

        /// <summary>
        /// 把终点放到相对起点 offset 的位置（终点跟随落点对象时用）。变化小于 epsilon 时不改，返回是否改了。
        /// </summary>
        public static bool SetEndOffset(MotionData data, Vector3 offset, float epsilon = 1e-4f)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var points = data.points;
            if (points.Count < 2) return false;
            var position = points[0].position + offset;
            var end = points[points.Count - 1];
            if ((end.position - position).sqrMagnitude <= epsilon * epsilon) return false;
            end.position = position;
            return true;
        }

        const float TimeEpsilon = 1e-4f;

        public static SegmentSettings Clone(SegmentSettings s)
        {
            return new SegmentSettings
            {
                timing = s.timing,
                duration = s.duration,
                speed = s.speed,
                ease = s.ease?.Clone() ?? new EaseSpec { type = EaseType.Linear },
                desc = s.desc,
            };
        }
    }
}
