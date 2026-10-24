using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>
    /// 自动生成写给人和 AI 看的一句话描述：路径段、属性片段、事件和整个动效。
    /// 手填了 desc 的地方以手填为准（<see cref="SegmentOrManual"/>、<see cref="ClipOrManual"/>），没填时用这里生成的。
    /// </summary>
    public static class MotionDescriber
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string SegmentOrManual(MotionData data, MotionEvaluator eval, int segment)
        {
            var desc = data.points[segment].segment?.desc;
            return string.IsNullOrWhiteSpace(desc) ? Segment(data, eval, segment) : desc;
        }

        public static string ClipOrManual(MotionData data, MotionEvaluator eval, int track, int clip)
        {
            var desc = data.tracks[track]?.clips[clip].desc;
            return string.IsNullOrWhiteSpace(desc) ? Clip(data, eval, track, clip) : desc;
        }

        /// <summary>例如“弧线向左上移动 (-419, 1451) px（向右拱起），0.66 秒（速度 2200 px/秒），OutQuad（减速停下）”。</summary>
        public static string Segment(MotionData data, MotionEvaluator eval, int segment)
        {
            bool ui = data.target == TargetKind.RectTransform;
            string unit = ui ? "px" : "单位";
            eval.GetSegmentControls(segment, out var p0, out var p1, out var p2, out var p3);
            var delta = p3 - p0;
            float distance = delta.magnitude;
            float length = eval.GetSegmentLength(segment);
            float scale = Math.Max(distance, length);

            var sb = new StringBuilder();
            if (scale <= (ui ? 0.5f : 0.005f))
            {
                sb.Append("原地停留");
            }
            else if (distance <= length * 0.05f)
            {
                sb.Append("绕一圈回到原处");
            }
            else
            {
                var mid = PathMath.Bezier(p0, p1, p2, p3, 0.5f);
                var bulge = mid - (p0 + p3) * 0.5f;
                bool curved = bulge.magnitude > distance * 0.05f;
                sb.Append(curved ? "弧线" : "直线");
                sb.Append("向").Append(Direction(delta)).Append("移动 ").Append(Vector(delta, ui ? 0 : 2, !IsFlat(delta))).Append(' ').Append(unit);
                if (curved) sb.Append("（向").Append(Direction(bulge)).Append("拱起）");
            }

            var seg = data.points[segment].segment ?? new SegmentSettings();
            sb.Append("，").Append(Seconds(eval.GetSegmentDuration(segment))).Append(" 秒");
            if (seg.timing == TimingMode.Speed) sb.Append("（速度 ").Append(Num(seg.speed, ui ? 0 : 2)).Append(' ').Append(unit).Append("/秒）");
            sb.Append("，").Append(Ease(seg.ease));
            return sb.ToString();
        }

        /// <summary>例如“0.1–0.3 秒：缩放到初始的 ×1.2，OutBack（冲过头再回弹）”。</summary>
        public static string Clip(MotionData data, MotionEvaluator eval, int track, int clip)
        {
            var t = data.tracks[track];
            if (t == null) return "";
            var c = t.clips[clip];
            float start = eval.GetClipStart(track, clip);
            float end = eval.GetClipEnd(track, clip);
            var sb = new StringBuilder();
            if (c.duration <= 0f)
            {
                sb.Append(Seconds(start)).Append(" 秒：瞬间").Append(t.DescribeValue(c.value));
                if (!t.enabled) sb.Append("（轨道已停用）");
                return sb.ToString();
            }
            sb.Append(Seconds(start)).Append('–').Append(Seconds(start + c.duration)).Append(" 秒：");
            sb.Append(t.DescribeValue(c.value)).Append("，").Append(Ease(c.ease));
            if (end < start + c.duration - 1e-4f)
                sb.Append(end <= start + 1e-4f ? "（被同轨道后一个片段完全覆盖）" : "（" + Seconds(end) + " 秒起由同轨道下一个片段接管）");
            if (!t.enabled) sb.Append("（轨道已停用）");
            return sb.ToString();
        }

        /// <summary>例如“0.9 秒触发事件 OnCoinArrive：金币到了”。</summary>
        public static string Event(MotionData data, MotionEvaluator eval, int index)
        {
            var e = data.events[index];
            var sb = new StringBuilder();
            sb.Append(Seconds(eval.GetEventTime(index))).Append(" 秒触发事件 ").Append(string.IsNullOrEmpty(e.name) ? "Event" : e.name);
            if (!string.IsNullOrEmpty(e.message)) sb.Append("：").Append(e.message);
            return sb.ToString();
        }

        /// <summary>整个动效的一句话概括：对象类型、时长、路径、属性、事件、循环、终点。</summary>
        public static string Summary(MotionData data, MotionEvaluator eval)
        {
            bool ui = data.target == TargetKind.RectTransform;
            var sb = new StringBuilder();
            sb.Append(ui ? "UI 动效" : "3D/2D 物体动效").Append("，单次 ").Append(Seconds(eval.Duration)).Append(" 秒");

            int segments = eval.SegmentCount;
            if (segments > 0)
            {
                float length = 0f;
                for (int i = 0; i < segments; i++) length += eval.GetSegmentLength(i);
                sb.Append("：路径 ").Append(segments).Append(" 段，总长 ").Append(Num(length, ui ? 0 : 2)).Append(ui ? " px" : " 单位");
            }
            else
            {
                sb.Append("：没有路径");
            }

            var names = new List<string>();
            foreach (var track in data.tracks)
                if (track != null && track.enabled && track.clips.Count > 0 && !names.Contains(track.DisplayName))
                    names.Add(track.DisplayName);
            if (names.Count > 0) sb.Append("，属性变化 ").Append(string.Join("、", names));
            if (data.events.Count > 0) sb.Append("，事件 ").Append(data.events.Count).Append(" 个");

            sb.Append("；").Append(Loop(data, eval));
            var end = data.endTarget;
            if (end != null && end.bound)
                sb.Append("；终点对准落点对象").Append(string.IsNullOrEmpty(end.hint) ? "" : "“" + end.hint + "”")
                    .Append(end.kind != data.target ? "（跨 UI/3D，按屏幕位置对准）" : "");
            sb.Append('。');
            return sb.ToString();
        }

        public static string Loop(MotionData data, MotionEvaluator eval)
        {
            var loop = data.loop;
            if (loop.count == 1) return "不循环";
            string times = loop.count < 0 ? "无限循环" : "循环 " + loop.count + " 次";
            string yoyo = loop.type == MotionLoopType.Yoyo ? "（来回）" : "";
            if (loop.scope == MotionLoopScope.LastSegment && eval.LoopStart > 0f)
                return "前 " + Seconds(eval.LoopStart) + " 秒播一次，之后最后一段" + times + yoyo;
            return "整段" + times + yoyo;
        }

        /// <summary>缓动名 + 中文说明，例如“OutQuad（减速停下）”。</summary>
        public static string Ease(EaseSpec ease)
        {
            var type = ease?.type ?? EaseType.Linear;
            return type == EaseType.Linear ? "Linear（匀速）" : type + "（" + EaseMeaning(type) + "）";
        }

        static string EaseMeaning(EaseType type)
        {
            string name = type.ToString();
            if (type == EaseType.Custom) return "自定义曲线";
            bool inOut = name.StartsWith("InOut", StringComparison.Ordinal);
            bool isIn = !inOut && name.StartsWith("In", StringComparison.Ordinal);
            if (name.EndsWith("Back", StringComparison.Ordinal)) return inOut ? "两端都带回弹" : isIn ? "先往回收再冲出" : "冲过头再回弹";
            if (name.EndsWith("Elastic", StringComparison.Ordinal)) return inOut ? "两端弹性抖动" : isIn ? "起步弹性抖动" : "到达后弹性抖动";
            if (name.EndsWith("Bounce", StringComparison.Ordinal)) return inOut ? "两端弹跳" : isIn ? "起步弹跳" : "落地弹跳";
            return inOut ? "缓起缓停" : isIn ? "加速启动" : "减速停下";
        }

        /// <summary>8 个方向：右、右上、上、左上、左、左下、下、右下；Z 分量为主时说 +Z / -Z。</summary>
        public static string Direction(Vector3 v)
        {
            float ax = Math.Abs(v.x), ay = Math.Abs(v.y), az = Math.Abs(v.z);
            if (az > ax && az > ay) return v.z > 0f ? " +Z 方向" : " -Z 方向";
            if (ax <= 1e-6f && ay <= 1e-6f) return "原处";
            double angle = Math.Atan2(v.y, v.x) * 180.0 / Math.PI;
            if (angle < 0) angle += 360.0;
            string[] names = { "右", "右上", "上", "左上", "左", "左下", "下", "右下" };
            return names[(int)Math.Floor((angle + 22.5) / 45.0) % 8];
        }

        static bool IsFlat(Vector3 v) => Math.Abs(v.z) <= 1e-4f;

        static string Vector(Vector3 v, int decimals, bool withZ)
        {
            return withZ
                ? "(" + Num(v.x, decimals) + ", " + Num(v.y, decimals) + ", " + Num(v.z, decimals) + ")"
                : "(" + Num(v.x, decimals) + ", " + Num(v.y, decimals) + ")";
        }

        /// <summary>秒数：最多 3 位小数，去掉末尾的 0。</summary>
        public static string Seconds(float seconds) => Num(seconds, 3);

        /// <summary>数字：最多 decimals 位小数，去掉末尾的 0，-0 写成 0。</summary>
        public static string Num(float value, int decimals)
        {
            double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded) < Math.Pow(10, -decimals) * 0.5) rounded = 0;
            string s = rounded.ToString("F" + decimals, Invariant);
            if (s.Contains(".")) s = s.TrimEnd('0').TrimEnd('.');
            return s;
        }

        /// <summary>颜色写成 RGB 和十六进制，例如“RGB(1, 0.5, 0.5) #FF8080”。</summary>
        public static string Rgb(float r, float g, float b)
        {
            int R = (int)Math.Round(Mathf.Clamp01(r) * 255f), G = (int)Math.Round(Mathf.Clamp01(g) * 255f), B = (int)Math.Round(Mathf.Clamp01(b) * 255f);
            return "RGB(" + Num(r, 2) + ", " + Num(g, 2) + ", " + Num(b, 2) + ") #" + R.ToString("X2", Invariant) + G.ToString("X2", Invariant) + B.ToString("X2", Invariant);
        }
    }
}
