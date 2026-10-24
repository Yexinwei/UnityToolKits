using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using L = Limbo.CurveMotion.Editor.Export.CSharpLiterals;

namespace Limbo.CurveMotion.Editor.Export
{
    public sealed class DotweenExportOptions
    {
        /// <summary>类名；为空时由动效名生成，例如 demo-coin-fly → DemoCoinFlyMotion。</summary>
        public string ClassName;

        /// <summary>命名空间；为空时放在全局命名空间。</summary>
        public string Namespace;
    }

    /// <summary>
    /// 把 <see cref="MotionData"/> 导出成一个可以直接放进工程的 C# 静态类（需要 DOTween）。
    /// 生成的 Play(target, …) 以对象当前状态为起点和初始值，创建并播放一个 Sequence，播放结果与编辑器预览一致：
    /// <list type="bullet">
    /// <item>路径：每段一个 DOVirtual.Float，按段时长和缓动驱动段内弧长比例，位置用与预览相同的贝塞尔 + 弧长表计算；</item>
    /// <item>属性片段：Sequence.Insert 到片段开始时间，从当前值补间到目标值；被后一个片段覆盖的部分截掉，缓动按原时长计算；</item>
    /// <item>事件：InsertCallback，默认打印日志，可传入回调；</item>
    /// <item>循环：整段循环用 SetLoops；尾段循环把最后一段（及同时间的片段、事件）放进单独循环的子 Sequence；</item>
    /// <item>终点绑定：Play 多一个落点参数，起点不动，整条路径按弧长进度修正到落点（UI 与 3D 之间经屏幕坐标换算）。</item>
    /// </list>
    /// 生成代码只依赖 UnityEngine 和 DOTween（UI 的透明度、颜色另需 UnityEngine.UI）。
    /// </summary>
    public static class DotweenExporter
    {
        const string Seq = "seq";
        const string Loop = "loop";
        const string Callback = "!callback:";

        /// <summary>
        /// 瞬间变化用的极短时长。导出代码里不出现 0 时长的补间：DOTween 把它当作一直处于完成状态，
        /// 倒放（Yoyo）和 Restart 倒回时不会还原，和预览不一致。
        /// </summary>
        const float Instant = 0.0001f;

        /// <summary>循环次数：-1 为无限，其余至少 1。</summary>
        static int LoopCount(MotionData data) => data.loop.count < 0 ? -1 : Math.Max(1, data.loop.count);

        struct Window
        {
            public string sequence;
            public float start, end;
        }

        sealed class Item
        {
            public string sequence;
            public float time;
            public float duration;
            public string comment;
            public string statement;
        }

        public static string Generate(MotionData data, DotweenExportOptions options = null, List<string> warnings = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            options ??= new DotweenExportOptions();
            warnings ??= new List<string>();
            var eval = new MotionEvaluator(data);
            foreach (var w in eval.Warnings) warnings.Add(w);

            bool ui = data.target == TargetKind.RectTransform;
            var ctx = new DotweenCodeContext(data.target);
            ctx.Using("DG.Tweening");
            ctx.Using("UnityEngine");

            string className = !string.IsNullOrWhiteSpace(options.ClassName) ? options.ClassName.Trim() : L.ClassName(data.name);
            if (!L.IsIdentifier(className))
            {
                warnings.Add($"类名 {className} 不是合法的 C# 标识符，已改用 CurveMotion");
                className = "CurveMotion";
            }

            int segments = eval.SegmentCount;
            var end = data.endTarget ?? new EndTarget();
            bool bound = end.bound && segments > 0;
            float loopStart = eval.LoopStart;
            int loopCount = LoopCount(data);
            bool tailLoop = loopCount != 1 && loopStart > 0f;
            var windows = tailLoop
                ? new[] { new Window { sequence = Seq, start = 0f, end = loopStart }, new Window { sequence = Loop, start = loopStart, end = float.PositiveInfinity } }
                : new[] { new Window { sequence = Seq, start = 0f, end = float.PositiveInfinity } };

            var items = new List<Item>();
            int curveCount = 0;
            var curveFields = new Dictionary<EaseSpec, string>();

            string EaseArg(EaseSpec ease)
            {
                if (ease != null && ease.type == EaseType.Custom)
                {
                    if (ease.curve == null || ease.curve.length == 0) return "Ease.Linear";
                    if (curveFields.TryGetValue(ease, out var field)) return field;
                    field = "Curve" + ++curveCount;
                    curveFields.Add(ease, field);
                    ctx.Helper("curve-" + field, "    static readonly AnimationCurve " + field + " = " + CurveLiteral(ease.curve) + ";");
                    ctx.Helper("part-curve", PartCurveHelper);
                    return field;
                }
                return "Ease." + (ease?.type ?? EaseType.Linear);
            }

            // 完整播放的缓动；自定义曲线按 0~1 归一化求值，与预览一致（DOTween 自己的 AnimationCurve 缓动会按最后一个关键帧的时间缩放）。
            string FullEase(EaseSpec ease, float duration)
            {
                if (duration <= 0f) return "Ease.Linear";
                string arg = EaseArg(ease);
                return arg.StartsWith("Ease.", StringComparison.Ordinal) ? arg : "Part(" + arg + ", " + L.Float(duration) + ")";
            }

            string PartEase(EaseSpec ease, float fullDuration, float offset)
            {
                string arg = EaseArg(ease);
                if (arg.StartsWith("Ease.", StringComparison.Ordinal)) ctx.Helper("part-ease", PartEaseHelper);
                return "Part(" + arg + ", " + L.Float(fullDuration) + (offset > 0f ? ", " + L.Float(offset) : "") + ")";
            }

            Window WindowAt(float time)
            {
                foreach (var w in windows)
                    if (time >= w.start && time < w.end) return w;
                return windows[windows.Length - 1];
            }

            // ---- 路径
            if (segments > 0)
            {
                for (int i = 0; i < segments; i++)
                {
                    float t0 = eval.GetPointTime(i);
                    float d = eval.GetSegmentDuration(i);
                    var w = tailLoop && i == segments - 1 ? windows[1] : windows[0];
                    var seg = data.points[i].segment ?? new SegmentSettings();
                    items.Add(new Item
                    {
                        sequence = w.sequence,
                        time = t0 - w.start,
                        duration = d > 0f ? d : Instant,
                        comment = "第 " + (i + 1) + " 段：" + MotionDescriber.SegmentOrManual(data, eval, i),
                        statement = d > 0f
                            ? "DOVirtual.Float(0f, 1f, " + L.Float(d) + ", x => Move(" + i + ", x)).SetEase(" + FullEase(seg.ease, d) + ")"
                            : "DOVirtual.Float(0f, 1f, " + L.Float(Instant) + ", _ => Move(" + i + ", 1f)).SetEase(Ease.Linear)",
                    });
                }
            }

            // ---- 属性轨道
            var seenTypes = new HashSet<Type>();
            for (int ti = 0; ti < data.tracks.Count; ti++)
            {
                var track = data.tracks[ti];
                if (track == null)
                {
                    warnings.Add($"第 {ti + 1} 条轨道的类型找不到，未导出");
                    continue;
                }
                string trackName = string.IsNullOrEmpty(track.name) ? track.DisplayName : track.name;
                if (!track.enabled || track.clips.Count == 0) continue;
                if (!TrackCodeGenRegistry.TryGet(track.GetType(), out var gen))
                {
                    warnings.Add($"轨道 {trackName}（{track.TypeId}）没有 DOTween 代码生成器，未导出。可以写一个带 [TrackCodeGen] 的 TrackCodeGen 子类。");
                    continue;
                }
                if (!seenTypes.Add(track.GetType()))
                    warnings.Add($"有多条 {track.DisplayName} 轨道：预览里后面的轨道整段生效，导出代码里各轨道只在自己的片段期间生效，结果可能不同。建议合并成一条。");

                var binding = gen.Bind(ctx, track);
                EmitTrack(data, eval, ti, track, trackName, binding, windows, items, FullEase, PartEase);
            }

            // ---- 事件
            if (data.events.Count > 0) ctx.Helper("fire", FireHelper);
            for (int i = 0; i < data.events.Count; i++)
            {
                var e = data.events[i];
                float time = eval.GetEventTime(i);
                var w = WindowAt(time);
                items.Add(new Item
                {
                    sequence = w.sequence,
                    time = time - w.start,
                    comment = "事件：" + MotionDescriber.Event(data, eval, i),
                    statement = Callback + "() => Fire(onEvent, " + L.String(string.IsNullOrEmpty(e.name) ? "Event" : e.name) + ", " + L.String(e.message ?? "") + ")",
                });
            }

            // ---- 组装
            var sb = new StringBuilder();
            WriteHeader(sb, data);
            if (data.events.Count > 0) ctx.Using("System");
            if (segments > 0) AddPathHelpers(ctx, eval, data, bound);

            // Play() 方法体先写到一边：变量声明在生成片段时才会登记。
            var body = new StringBuilder();
            WritePlayBody(body, ctx, data, eval, items, segments, bound, tailLoop, loopStart, loopCount, ui);

            foreach (var ns in ctx.Usings) sb.Append("using ").Append(ns).Append(";\n");
            sb.Append('\n');
            string indent = "";
            if (!string.IsNullOrWhiteSpace(options.Namespace))
            {
                sb.Append("namespace ").Append(options.Namespace.Trim()).Append("\n{\n");
                indent = "    ";
            }

            var cls = new StringBuilder();
            WriteClassDoc(cls, data, eval, className, ui, bound);
            cls.Append("public static class ").Append(className).Append("\n{\n");
            cls.Append("    /// <summary>单次播放时长（秒），不含循环。</summary>\n");
            cls.Append("    public const float Duration = ").Append(L.Float(eval.Duration)).Append(";\n\n");
            WritePlaySignature(cls, data, ui, bound, tailLoop && loopCount < 0);
            cls.Append("    {\n");
            foreach (var decl in ctx.Declarations) cls.Append("        ").Append(decl).Append('\n');
            cls.Append(body);
            cls.Append("    }\n");
            foreach (var helper in ctx.Helpers) cls.Append('\n').Append(helper).Append('\n');
            cls.Append("}\n");

            foreach (var line in cls.ToString().TrimEnd('\n').Split('\n'))
            {
                if (line.Length == 0) { sb.Append('\n'); continue; }
                sb.Append(indent).Append(line).Append('\n');
            }
            if (indent.Length > 0) sb.Append("}\n");
            return sb.ToString().TrimEnd('\n') + "\n";
        }

        static void EmitTrack(MotionData data, MotionEvaluator eval, int ti, MotionTrack track, string trackName, TrackBinding binding,
            Window[] windows, List<Item> items, Func<EaseSpec, float, string> fullEase, Func<EaseSpec, float, float, string> partEase)
        {
            int dim = Math.Max(1, Math.Min(4, track.Dimension));
            string tweenOf(string to, string duration) => binding.Tween != null
                ? binding.Tween(to, duration)
                : "DOTween.To(() => " + binding.Read + ", v => { " + binding.Write("v") + "; }, " + to + ", " + duration + ")";

            // 片段按开始时间排序（同时开始时列表靠后的算后面），与求值器一致。
            var order = new List<int>();
            for (int ci = 0; ci < track.clips.Count; ci++) order.Add(ci);
            order.Sort((a, b) =>
            {
                int c = eval.GetClipStart(ti, a).CompareTo(eval.GetClipStart(ti, b));
                return c != 0 ? c : a.CompareTo(b);
            });

            // 当前值 = A ⊙ 初始值 + B（逐分量）。片段目标对初始值都是仿射的，所以任意时刻的值都能这样表示。
            var stateA = Vector4.one;
            var stateB = Vector4.zero;
            bool first = true;
            foreach (int ci in order)
            {
                var clip = track.clips[ci];
                float s = eval.GetClipStart(ti, ci);
                float e = eval.GetClipEnd(ti, ci);
                float d = Math.Max(0f, clip.duration);
                var toB = track.ResolveTarget(Vector4.zero, clip.value);
                var toA = track.ResolveTarget(Vector4.one, clip.value) - toB;
                string to = Compose(binding, toA, toB, dim);
                var fromA = stateA;
                var fromB = stateB;

                float k = d > 0f ? Easing.Evaluate(clip.ease, (e - s) / d) : 1f;
                stateA = Vector4.LerpUnclamped(fromA, toA, k);
                stateB = Vector4.LerpUnclamped(fromB, toB, k);

                bool overridden = d > 0f && e <= s + 1e-6f;
                if (overridden) continue;

                string desc = MotionDescriber.ClipOrManual(data, eval, ti, ci);
                string comment = (first ? trackName + "：" : "") + desc;

                if (d <= 0f)
                {
                    // 瞬间变化。在序列开头：一直写目标值；否则在它之前放一个极短的补间，倒放、循环倒回时能还原成之前的值。
                    var w = FindWindow(windows, s);
                    float rel = s - w.start;
                    var item = new Item { sequence = w.sequence, comment = comment };
                    if (rel <= 0f)
                    {
                        item.time = 0f;
                        item.duration = Instant;
                        item.statement = "DOVirtual.Float(0f, 1f, " + L.Float(Instant) + ", _ => { " + binding.Write(to) + "; }).SetEase(Ease.Linear)";
                    }
                    else
                    {
                        item.time = rel >= Instant ? rel - Instant : 0f;
                        item.duration = rel >= Instant ? Instant : rel;
                        item.statement = tweenOf(to, L.Float(item.duration)) + ".SetEase(Ease.Linear)";
                    }
                    items.Add(item);
                    first = false;
                    continue;
                }

                foreach (var w in windows)
                {
                    float lo = Math.Max(s, w.start);
                    float hi = Math.Min(e, w.end);
                    if (hi <= lo) continue;
                    string len = L.Float(hi - lo);
                    string statement;
                    if (lo <= s)
                    {
                        bool full = hi >= s + d - 1e-6f;
                        statement = tweenOf(to, full ? L.Float(d) : len) + ".SetEase(" + (full ? fullEase(clip.ease, d) : partEase(clip.ease, d, 0f)) + ")";
                    }
                    else
                    {
                        // 从片段中间开始（尾段循环把片段切成两截）：显式给出片段开始时的值，缓动从片段内的偏移处接着算。
                        string from = Compose(binding, fromA, fromB, dim);
                        statement = "DOVirtual.Float(0f, 1f, " + len + ", x => { " + binding.Write(LerpExpr(dim, from, to, "x")) + "; }).SetEase(" +
                                    partEase(clip.ease, d, lo - s) + ")";
                        comment += "（接上一截）";
                    }
                    items.Add(new Item { sequence = w.sequence, time = lo - w.start, duration = hi - lo, comment = comment, statement = statement });
                    first = false;
                }
            }
        }

        static Window FindWindow(Window[] windows, float time)
        {
            foreach (var w in windows)
                if (time >= w.start && time < w.end) return w;
            return windows[windows.Length - 1];
        }

        /// <summary>A ⊙ 初始值 + B 写成代码，常见情况化简：初始值本身、纯倍率、纯增量、常量。</summary>
        static string Compose(TrackBinding binding, Vector4 a, Vector4 b, int dim)
        {
            bool aOne = true, aZero = true, bZero = true;
            for (int i = 0; i < dim; i++)
            {
                if (Math.Abs(a[i] - 1f) > 1e-6f) aOne = false;
                if (Math.Abs(a[i]) > 1e-6f) aZero = false;
                if (Math.Abs(b[i]) > 1e-6f) bZero = false;
            }
            if (aZero) return L.Value(b, dim);
            string initial = binding.Initial != null ? binding.Initial() : binding.Read;
            string scaled = aOne ? initial : dim == 1 ? initial + " * " + L.Value(a, 1) : L.ValueType(dim) + ".Scale(" + initial + ", " + L.Value(a, dim) + ")";
            return bZero ? scaled : scaled + " + " + L.Value(b, dim);
        }

        static string LerpExpr(int dim, string a, string b, string t) =>
            (dim == 1 ? "Mathf" : L.ValueType(dim)) + ".LerpUnclamped(" + a + ", " + b + ", " + t + ")";

        static void WriteHeader(StringBuilder sb, MotionData data)
        {
            sb.Append("// <auto-generated>\n");
            sb.Append("// 由 Curve Motion（").Append(CurveMotionInfo.PackageName).Append("）从动效“").Append(L.Comment(data.name)).Append("”导出，需要 DOTween。\n");
            sb.Append("// 要改动效请在 Curve Motion 编辑器里改，再重新导出覆盖这个文件。\n");
            sb.Append("// </auto-generated>\n");
        }

        static void WriteClassDoc(StringBuilder sb, MotionData data, MotionEvaluator eval, string className, bool ui, bool bound)
        {
            sb.Append("/// <summary>\n");
            if (!string.IsNullOrWhiteSpace(data.description))
                foreach (var line in data.description.Split('\n'))
                    sb.Append("/// ").Append(Xml(line.TrimEnd('\r'))).Append('\n');
            sb.Append("/// ").Append(Xml(MotionDescriber.Summary(data, eval))).Append('\n');
            sb.Append("/// 用法：").Append(className).Append(".Play(").Append(ui ? "rectTransform" : "transform").Append(bound ? ", endTarget" : "").Append(");\n");
            sb.Append("/// </summary>\n");
        }

        static void WritePlaySignature(StringBuilder sb, MotionData data, bool ui, bool bound, bool infiniteTail)
        {
            var end = data.endTarget;
            sb.Append("    /// <summary>\n");
            sb.Append("    /// 在 target 上播放：以它当前的").Append(ui ? "位置（anchoredPosition）" : "位置（localPosition）").Append("为起点，当前的缩放、旋转、透明度、颜色为初始值。\n");
            sb.Append("    /// 返回的 Sequence 已开始播放；停止用 target.DOKill() 或 Kill 返回值，对象销毁时自动停止。\n");
            if (bound)
                sb.Append("    /// 路径走完后落点对象再移动，不会再跟随。\n");
            if (infiniteTail)
                sb.Append("    /// 尾段无限循环：返回的 Sequence 只含循环之前的部分，播完后不自动销毁，Kill 它时循环部分一起停。\n");
            sb.Append("    /// </summary>\n");
            sb.Append("    /// <param name=\"target\">要动的对象。</param>\n");
            if (bound)
            {
                sb.Append("    /// <param name=\"endTarget\">落点对象").Append(string.IsNullOrEmpty(end.hint) ? "" : "（" + Xml(end.hint) + "）")
                    .Append("：路径终点对准它，对象移动时跟着修正。为空时按设计时的终点。</param>\n");
                sb.Append("    /// <param name=\"worldCamera\">UI 与 3D 之间换算屏幕位置用的相机，为空时用 Camera.main。</param>\n");
            }
            if (data.events.Count > 0)
                sb.Append("    /// <param name=\"onEvent\">事件回调，参数是事件名；为空时打印日志。</param>\n");

            sb.Append("    public static Sequence Play(").Append(ui ? "RectTransform" : "Transform").Append(" target");
            if (bound) sb.Append(", Transform endTarget = null, Camera worldCamera = null");
            if (data.events.Count > 0) sb.Append(", Action<string> onEvent = null");
            sb.Append(")\n");
        }

        static void WritePlayBody(StringBuilder sb, DotweenCodeContext ctx, MotionData data, MotionEvaluator eval, List<Item> items,
            int segments, bool bound, bool tailLoop, float loopStart, int loopCount, bool ui)
        {
            string t = ctx.Target;
            if (segments > 0)
            {
                ctx.Declare("start", t + (ui ? ".anchoredPosition" : ".localPosition"));
                if (bound)
                {
                    ctx.Declare("startLocal", t + ".localPosition");
                    ctx.Declare("correction", "Vector3.zero");
                }
                sb.Append('\n');
                sb.Append("        // 路径上第 segment 段、段内弧长比例 fraction（缓动后的值）处的位置。\n");
                sb.Append("        void Move(int segment, float fraction)\n");
                sb.Append("        {\n");
                if (bound)
                {
                    sb.Append("            if (endTarget != null && TryGetEndOffset(target, startLocal, endTarget, worldCamera, out var end)) correction = end - AuthoredEnd;\n");
                    sb.Append("            var offset = PathOffset(segment, fraction) + correction * PathProgress(segment, fraction);\n");
                }
                else
                {
                    sb.Append("            var offset = PathOffset(segment, fraction);\n");
                }
                sb.Append(ui ? "            target.anchoredPosition = start + new Vector2(offset.x, offset.y);\n" : "            target.localPosition = start + offset;\n");
                sb.Append("        }\n");
            }

            string loopType = "LoopType." + (data.loop.type == MotionLoopType.Yoyo ? "Yoyo" : "Restart");
            bool infiniteTail = tailLoop && loopCount < 0;
            const string NewSequence = "DOTween.Sequence().SetTarget(target).SetLink(target.gameObject).SetEase(Ease.Linear)";
            sb.Append('\n');
            if (tailLoop)
            {
                sb.Append("        // 尾段循环：").Append(MotionDescriber.Seconds(loopStart)).Append(" 秒之前播一次，之后的部分")
                    .Append(infiniteTail ? "单独无限循环" : "循环 " + loopCount + " 次").Append("。\n");
                if (infiniteTail)
                {
                    // 先建 loop：前面播完那一帧它还没轮到，从下一帧开始走，与前面衔接。
                    sb.Append("        var loop = ").Append(NewSequence).Append(".SetLoops(-1, ").Append(loopType).Append(").Pause();\n");
                    sb.Append("        var seq = ").Append(NewSequence).Append(".SetAutoKill(false);\n");
                }
                else
                {
                    sb.Append("        var seq = ").Append(NewSequence).Append(";\n");
                    sb.Append("        var loop = DOTween.Sequence().SetEase(Ease.Linear);\n");
                }
            }
            else
            {
                sb.Append("        var seq = ").Append(NewSequence).Append(";\n");
            }

            // 按序列、时间排序（同时间保持生成顺序：路径、轨道、事件）。
            var sorted = new List<Item>(items);
            var index = new Dictionary<Item, int>();
            for (int i = 0; i < items.Count; i++) index[items[i]] = i;
            sorted.Sort((a, b) =>
            {
                int c = (a.sequence == Seq ? 0 : 1).CompareTo(b.sequence == Seq ? 0 : 1);
                if (c != 0) return c;
                c = a.time.CompareTo(b.time);
                return c != 0 ? c : index[a].CompareTo(index[b]);
            });

            float seqLength = tailLoop ? loopStart : eval.Duration;
            float loopLength = eval.Duration - loopStart;
            float seqEnd = 0f, loopEnd = 0f;
            string current = null;
            foreach (var item in sorted)
            {
                if (item.sequence != current)
                {
                    if (current == Seq) Pad(sb, Seq, seqEnd, seqLength);
                    current = item.sequence;
                    if (current == Loop) sb.Append("\n        // ---- 循环部分（时间从尾段开始算）\n");
                }
                if (!string.IsNullOrEmpty(item.comment)) sb.Append("        // ").Append(L.Comment(item.comment)).Append('\n');
                if (item.statement.StartsWith(Callback, StringComparison.Ordinal))
                    sb.Append("        ").Append(item.sequence).Append(".InsertCallback(").Append(L.Float(item.time)).Append(", ")
                        .Append(item.statement.Substring(Callback.Length)).Append(");\n");
                else
                    sb.Append("        ").Append(item.sequence).Append(".Insert(").Append(L.Float(item.time)).Append(", ").Append(item.statement).Append(");\n");
                float itemEnd = item.time + item.duration;
                if (item.sequence == Seq) seqEnd = Math.Max(seqEnd, itemEnd);
                else loopEnd = Math.Max(loopEnd, itemEnd);
            }
            if (current == Seq || current == null) Pad(sb, Seq, seqEnd, seqLength);
            if (tailLoop) Pad(sb, Loop, loopEnd, loopLength);

            sb.Append('\n');
            if (infiniteTail)
            {
                sb.Append("        // 前面播完时开始循环；Kill 返回的 seq 时循环部分一起停。\n");
                sb.Append("        seq.InsertCallback(").Append(L.Float(loopStart)).Append(", () => loop.Play());\n");
                sb.Append("        seq.OnKill(() => loop.Kill());\n");
            }
            else if (tailLoop)
            {
                sb.Append("        seq.Insert(").Append(L.Float(loopStart)).Append(", loop.SetLoops(").Append(loopCount).Append(", ").Append(loopType).Append("));\n");
            }
            else if (loopCount != 1)
            {
                sb.Append("        seq.SetLoops(").Append(loopCount).Append(", ").Append(loopType).Append(");\n");
            }
            sb.Append("        return seq;\n");
        }

        /// <summary>序列里最晚的补间比单次时长短时（停用的轨道、没导出的轨道也占时间）补一段空白，让循环和预览同步。</summary>
        static void Pad(StringBuilder sb, string sequence, float end, float length)
        {
            if (length - end <= 1e-5f) return;
            sb.Append("        ").Append(sequence).Append(".AppendInterval(").Append(L.Float(length - end)).Append("); // 补齐到 ")
                .Append(MotionDescriber.Seconds(length)).Append(" 秒\n");
        }

        static void AddPathHelpers(DotweenCodeContext ctx, MotionEvaluator eval, MotionData data, bool bound)
        {
            var origin = data.points[0].position;
            var sb = new StringBuilder();
            sb.Append("    // ---------------------------------------------------------------- 路径\n");
            sb.Append("    // 与编辑器预览同一算法：每段一条三次贝塞尔曲线，段内按弧长匀速，缓动作用在段内的弧长比例上。\n\n");
            sb.Append("    /// <summary>每段 4 个控制点，相对起点。</summary>\n");
            sb.Append("    static readonly Vector3[] Controls =\n    {\n");
            for (int i = 0; i < eval.SegmentCount; i++)
            {
                eval.GetSegmentControls(i, out var p0, out var p1, out var p2, out var p3);
                sb.Append("        ").Append(L.Vector3(p0 - origin)).Append(", ").Append(L.Vector3(p1 - origin)).Append(", ")
                    .Append(L.Vector3(p2 - origin)).Append(", ").Append(L.Vector3(p3 - origin)).Append(", // 第 ").Append(i + 1).Append(" 段\n");
            }
            sb.Append("    };\n");
            if (bound)
            {
                sb.Append("\n    /// <summary>设计时的终点（相对起点）。实际落点与它的差按路径进度分摊到整条路径上。</summary>\n");
                sb.Append("    static readonly Vector3 AuthoredEnd = ").Append(L.Vector3(eval.AuthoredEndOffset)).Append(";\n");
            }
            sb.Append(PathFunctions);
            if (bound) sb.Append(PathProgressFunction);
            ctx.Helper("path", sb.ToString().TrimEnd('\n'));
            if (bound) ctx.Helper("end-offset", EndOffsetHelper);
        }

        static string CurveLiteral(AnimationCurve curve)
        {
            var sb = new StringBuilder("new AnimationCurve(");
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var k = keys[i];
                sb.Append("new Keyframe(").Append(L.Float(k.time)).Append(", ").Append(L.Float(k.value)).Append(", ")
                    .Append(L.Float(k.inTangent)).Append(", ").Append(L.Float(k.outTangent)).Append(')');
            }
            return sb.Append(')').ToString();
        }

        static string Xml(string s) => L.Comment(s).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        const string PathFunctions = @"
    const int Samples = 64;
    static float[][] s_Lengths;
    static float[] s_LengthBefore;
    static float s_TotalLength;

    static Vector3 Bezier(int segment, float u)
    {
        int i = segment * 4;
        float v = 1f - u;
        return Controls[i] * (v * v * v) + Controls[i + 1] * (3f * v * v * u) + Controls[i + 2] * (3f * v * u * u) + Controls[i + 3] * (u * u * u);
    }

    /// <summary>每段的累积弧长表（Samples 个采样）。</summary>
    static void EnsureLengths()
    {
        if (s_Lengths != null) return;
        int segments = Controls.Length / 4;
        var lengths = new float[segments][];
        var before = new float[segments];
        float total = 0f;
        for (int s = 0; s < segments; s++)
        {
            var table = new float[Samples + 1];
            var prev = Controls[s * 4];
            for (int i = 1; i <= Samples; i++)
            {
                var p = Bezier(s, i / (float)Samples);
                table[i] = table[i - 1] + Vector3.Distance(prev, p);
                prev = p;
            }
            lengths[s] = table;
            before[s] = total;
            total += table[Samples];
        }
        s_LengthBefore = before;
        s_TotalLength = total;
        s_Lengths = lengths;
    }

    /// <summary>弧长比例 → 贝塞尔参数，使段内匀速。</summary>
    static float ToParam(float[] table, float fraction)
    {
        float total = table[Samples];
        if (total <= 0f) return Mathf.Clamp01(fraction);
        float target = Mathf.Clamp01(fraction) * total;
        int lo = 0, hi = Samples;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (table[mid] < target) lo = mid; else hi = mid;
        }
        float span = table[hi] - table[lo];
        float local = span > 0f ? (target - table[lo]) / span : 0f;
        return (lo + local) / Samples;
    }

    /// <summary>第 segment 段、弧长比例 fraction 处相对起点的位置。Back、Elastic 等缓动超出 0~1 时沿端点切线外推。</summary>
    static Vector3 PathOffset(int segment, float fraction)
    {
        EnsureLengths();
        var table = s_Lengths[segment];
        if (fraction >= 0f && fraction <= 1f) return Bezier(segment, ToParam(table, fraction));
        int i = segment * 4;
        float length = table[Samples];
        if (fraction > 1f) return Controls[i + 3] + Direction(Controls[i + 3] - Controls[i + 2], Controls[i + 3] - Controls[i]) * ((fraction - 1f) * length);
        return Controls[i] + Direction(Controls[i + 1] - Controls[i], Controls[i + 3] - Controls[i]) * (fraction * length);
    }

    static Vector3 Direction(Vector3 preferred, Vector3 fallback)
    {
        if (preferred.sqrMagnitude > 1e-12f) return preferred.normalized;
        return fallback.sqrMagnitude > 1e-12f ? fallback.normalized : Vector3.zero;
    }
";

        const string PathProgressFunction = @"
    /// <summary>整条路径上走过的弧长比例：0 为起点，1 为终点。</summary>
    static float PathProgress(int segment, float fraction)
    {
        EnsureLengths();
        if (s_TotalLength <= 1e-6f) return (segment + Mathf.Clamp01(fraction)) / (Controls.Length / 4);
        return (s_LengthBefore[segment] + fraction * s_Lengths[segment][Samples]) / s_TotalLength;
    }
";

        const string EndOffsetHelper = @"    // ---------------------------------------------------------------- 落点
    // 与编辑器预览相同的换算：同类对象按世界坐标；UI 与 3D 之间经屏幕坐标，
    // UI 用所在 Canvas 的相机（Overlay 为 null），3D 用世界相机，3D 动效对象保持起点离相机的距离。

    /// <summary>落点对象相对动效对象起点的偏移（动效对象父节点空间）。换算不了时返回 false。</summary>
    static bool TryGetEndOffset(Transform mover, Vector3 startLocal, Transform end, Camera worldCamera, out Vector3 offset)
    {
        offset = Vector3.zero;
        if (mover == null || end == null) return false;
        if (worldCamera == null) worldCamera = Camera.main;
        var parent = mover.parent;
        bool endUi = IsUi(end);

        if (IsUi(mover))
        {
            var parentRect = parent as RectTransform;
            if (parentRect == null) return false;
            Vector2 local;
            if (endUi && SameRootCanvas(mover, end))
            {
                local = parentRect.InverseTransformPoint(end.position);
            }
            else
            {
                if (!TryGetScreenPoint(end, worldCamera, out var screen)) return false;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screen, CanvasCamera(mover), out local)) return false;
            }
            offset = new Vector3(local.x - startLocal.x, local.y - startLocal.y, 0f);
            return true;
        }

        Vector3 world;
        if (!endUi)
        {
            world = end.position;
        }
        else
        {
            if (worldCamera == null || !TryGetScreenPoint(end, worldCamera, out var screen)) return false;
            var startWorld = parent != null ? parent.TransformPoint(startLocal) : startLocal;
            float depth = worldCamera.WorldToScreenPoint(startWorld).z;
            if (depth <= 0f) return false;
            world = worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
        }
        var localPosition = parent != null ? parent.InverseTransformPoint(world) : world;
        offset = localPosition - startLocal;
        return true;
    }

    /// <summary>挂在屏幕空间 Canvas 下的 RectTransform 算 UI；World Space Canvas 里的按 3D 处理。</summary>
    static bool IsUi(Transform t)
    {
        if (!(t is RectTransform)) return false;
        var canvas = t.GetComponentInParent<Canvas>();
        return canvas != null && canvas.rootCanvas.renderMode != RenderMode.WorldSpace;
    }

    static Camera CanvasCamera(Transform ui)
    {
        var canvas = ui != null ? ui.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return null;
        canvas = canvas.rootCanvas;
        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    static bool SameRootCanvas(Transform a, Transform b)
    {
        var ca = a.GetComponentInParent<Canvas>();
        var cb = b.GetComponentInParent<Canvas>();
        return ca != null && cb != null && ca.rootCanvas == cb.rootCanvas;
    }

    static bool TryGetScreenPoint(Transform t, Camera worldCamera, out Vector2 screen)
    {
        screen = default;
        if (IsUi(t))
        {
            screen = RectTransformUtility.WorldToScreenPoint(CanvasCamera(t), t.position);
            return true;
        }
        if (worldCamera == null) return false;
        var p = worldCamera.WorldToScreenPoint(t.position);
        if (p.z <= 0f) return false;
        screen = p;
        return true;
    }";

        const string FireHelper = @"    // ---------------------------------------------------------------- 事件
    static void Fire(Action<string> onEvent, string name, string message)
    {
        if (onEvent != null) onEvent(name);
        else Debug.Log(string.IsNullOrEmpty(message) ? ""[Curve Motion] "" + name : ""[Curve Motion] "" + name + "": "" + message);
    }";

        const string PartEaseHelper = @"    /// <summary>片段被截断或从中间开始时用：按片段的完整时长求缓动值，offset 是这一截在片段里的开始时间。</summary>
    static EaseFunction Part(Ease ease, float fullDuration, float offset = 0f) =>
        (time, duration, overshootOrAmplitude, period) => DOVirtual.EasedValue(0f, 1f, Mathf.Clamp01((offset + time) / fullDuration), ease);";

        const string PartCurveHelper = @"    /// <summary>自定义曲线缓动：曲线的时间和值都在 0~1，按片段的完整时长求值（offset 同上）。</summary>
    static EaseFunction Part(AnimationCurve curve, float fullDuration, float offset = 0f) =>
        (time, duration, overshootOrAmplitude, period) => curve.Evaluate(Mathf.Clamp01((offset + time) / fullDuration));";
    }
}
