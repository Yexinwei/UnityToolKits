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
