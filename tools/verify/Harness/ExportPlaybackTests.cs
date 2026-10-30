// 导出代码的回放测试：生成 C#，用 Roslyn（C# 9，与 Unity 2022.3 一致）编译，
// 在 FakeDOTween 上逐帧播放，每帧和 MotionEvaluator（编辑器预览用的求值器）对照位置和各属性。
// 只在 tools/verify 里跑（需要 Roslyn），不进包。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DG.Tweening;
using Limbo.CurveMotion.Editor.Export;
using Limbo.CurveMotion.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Limbo.CurveMotion.Tests.Harness
{
    public class ExportPlaybackTests
    {
        // 1/256 秒：二进制精确，测试数据的时长也取精确值，循环衔接处不会有浮点误差。
        const float Dt = 1f / 256f;

        static string DemoFolder => Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../../Packages/com.limbo.curvemotion/Editor/TestScene");

        // ---------------------------------------------------------------- 用例

        static IEnumerable<string> DemoFiles() =>
            new[] { "demo-coin-fly.json", "demo-coin-to-heart.json", "demo-heart-hop.json", "demo-heart-to-wallet.json" };

        [TestCaseSource(nameof(DemoFiles))]
        public void Demo_MatchesPreview(string file)
        {
            var data = MotionJson.FromJson(File.ReadAllText(Path.Combine(DemoFolder, file)));
            var run = new Run(data);
            run.Play(endTarget: null);
            run.Compare(seconds: Math.Min(run.Eval.TotalDuration, 4f) + 0.5f);
        }

        [Test]
        public void Ui_AllTracks_NoLoop()
        {
            var run = new Run(Rich(TargetKind.RectTransform));
            run.Play();
            run.Compare(run.Eval.Duration + 0.25f);
            run.AssertEvents(("Arrive", 0.75f), ("Done", 1.125f));
        }

        [Test]
        public void Scene_AllTracks_NoLoop()
        {
            var run = new Run(Rich(TargetKind.Transform, scale: 0.01f));
            run.Play();
            run.Compare(run.Eval.Duration + 0.25f);
        }

        [TestCase(3, MotionLoopType.Restart)]
        [TestCase(2, MotionLoopType.Yoyo)]
        [TestCase(3, MotionLoopType.Yoyo)]
        [TestCase(-1, MotionLoopType.Restart)]
        [TestCase(-1, MotionLoopType.Yoyo)]
        public void WholeLoop(int count, MotionLoopType type)
        {
            var data = Rich(TargetKind.RectTransform);
            data.loop = new LoopSettings { count = count, type = type, scope = MotionLoopScope.Whole };
            var run = new Run(data);
            run.Play();
            run.Compare(count < 0 ? run.Eval.Duration * 3.5f : run.Eval.TotalDuration + 0.25f);
            if (type == MotionLoopType.Restart && count > 0)
                Assert.AreEqual(count, run.Events.Count(e => e.name == "Arrive"), "每轮都触发事件");
        }

        [TestCase(3, MotionLoopType.Restart)]
        [TestCase(3, MotionLoopType.Yoyo)]
        [TestCase(-1, MotionLoopType.Restart)]
        [TestCase(-1, MotionLoopType.Yoyo)]
        public void TailLoop(int count, MotionLoopType type)
        {
            var data = Rich(TargetKind.RectTransform, binaryTiming: true);
            data.loop = new LoopSettings { count = count, type = type, scope = MotionLoopScope.LastSegment };
            var run = new Run(data);
            Assert.Greater(run.Eval.LoopStart, 0f);
            run.Play();
            run.Compare(count < 0 ? run.Eval.LoopStart + run.Eval.LoopLength * 3.5f : run.Eval.TotalDuration + 0.25f);
            if (type == MotionLoopType.Restart && count > 0)
            {
                Assert.AreEqual(1, run.Events.Count(e => e.name == "Arrive"), "循环之前的事件只触发一次");
                Assert.AreEqual(count, run.Events.Count(e => e.name == "Done"), "尾段里的事件每轮都触发");
            }
        }

        [Test]
        public void TailLoop_Infinite_KillStopsBoth()
        {
            var data = Rich(TargetKind.RectTransform, binaryTiming: true);
            data.loop = new LoopSettings { count = -1, type = MotionLoopType.Restart, scope = MotionLoopScope.LastSegment };
            var run = new Run(data);
            var seq = run.Play();
            run.Compare(run.Eval.LoopStart + 0.3f);
            seq.Kill();
            DOTween.Step(Dt);
            Assert.AreEqual(0, DOTween.ActiveCount, "Kill 返回的 Sequence 后循环部分也停了");
        }

        [Test]
        public void TailLoop_Finite_WithClipsAcrossLoopStart()
        {
            var data = Rich(TargetKind.RectTransform, binaryTiming: true);
            // 一个从尾段之前开始、跨过尾段开头的缩放片段：循环部分要从片段中间接着播。
            // 透明度：X 被 Y 截断，Y 跨过尾段开头（1 秒），循环部分里 Y 的起始值取决于 X 截断处的值。
            var alpha = (AlphaTrack)data.tracks[2];
            alpha.clips.Add(new MotionClip { anchor = "a", offset = 0.5f, duration = 0.5f, value = new Vector4(0.9f, 0f, 0f, 0f), ease = Ease(EaseType.OutQuad) });
            alpha.clips.Add(new MotionClip { anchor = "b", offset = 0.125f, duration = 0.25f, value = new Vector4(0.1f, 0f, 0f, 0f), ease = Ease(EaseType.InOutSine) });
            data.loop = new LoopSettings { count = 3, type = MotionLoopType.Restart, scope = MotionLoopScope.LastSegment };
            var run = new Run(data);
            StringAssert.Contains("接上一截", run.Code);
            run.Play();
            run.Compare(run.Eval.TotalDuration + 0.25f);
        }

        [Test]
        public void Alpha_GraphicSource_LeavesCanvasGroup()
        {
            var data = Rich(TargetKind.RectTransform);
            ((AlphaTrack)data.tracks[2]).source = AlphaSource.Graphic;
            var run = new Run(data);
            run.Play();
            var group = run.Target.GetComponent<CanvasGroup>();
            run.Compare(run.Eval.TotalDuration + 0.25f, everyFrame: () => Assert.AreEqual(0.8f, group.alpha, 1e-6f, "CanvasGroup 不动"));
        }

        [Test]
        public void Alpha_CanvasGroupSource_OnSceneObject()
        {
            var data = Rich(TargetKind.Transform, scale: 0.01f);
            ((AlphaTrack)data.tracks[2]).source = AlphaSource.CanvasGroup;
            var run = new Run(data);
            run.Play();
            var sprite = run.Target.GetComponent<SpriteRenderer>();
            run.Compare(run.Eval.TotalDuration + 0.25f, everyFrame: () => Assert.AreEqual(0.8f, sprite.color.a, 1e-6f, "SpriteRenderer 的透明度不动"));
        }

        [Test]
        public void EndBound_Scene_FollowsEndTarget()
        {
            var data = Rich(TargetKind.Transform, scale: 0.01f);
            data.endTarget = new EndTarget { bound = true, kind = TargetKind.Transform, hint = "终点" };
            var run = new Run(data);
            var end = new GameObject().transform;
            end.localPosition = new Vector3(5f, 8f, 0f);
            run.Play(end);
            run.Compare(run.Eval.Duration + 0.25f, endOffset: () => end.position - run.StartLocal);
            Assert.That((run.Target.localPosition - (Vector3)end.position).magnitude, Is.LessThan(1e-3f), "停在落点上");
        }

        [Test]
        public void EndBound_Scene_MovingEndTarget()
        {
            var data = Rich(TargetKind.Transform, scale: 0.01f);
            data.endTarget = new EndTarget { bound = true, kind = TargetKind.Transform };
            var run = new Run(data);
            var end = new GameObject().transform;
            end.localPosition = new Vector3(5f, 8f, 0f);
            run.Play(end);
            // 路径走完后落点再动就不跟了（预览会继续跟），所以只在路径期间移动落点。
            float pathEnd = run.Eval.PathDuration;
            float time = 0f;
            run.Compare(run.Eval.Duration + 0.25f, endOffset: () => end.position - run.StartLocal, everyFrame: () =>
            {
                time += Dt;
                if (time < pathEnd) end.localPosition += new Vector3(0.01f, 0f, 0f);
            });
        }

        [Test]
        public void EndBound_WithoutEndTarget_UsesAuthoredEnd()
        {
            var data = Rich(TargetKind.Transform, scale: 0.01f);
            data.endTarget = new EndTarget { bound = true, kind = TargetKind.Transform };
            var run = new Run(data);
            run.Play(endTarget: null);
            run.Compare(run.Eval.Duration + 0.25f);
        }

        [Test]
        public void EndBound_Ui_SameCanvas()
        {
            var data = Rich(TargetKind.RectTransform);
            data.endTarget = new EndTarget { bound = true, kind = TargetKind.RectTransform, hint = "钱包" };
            var canvas = GameObject.CreateUi();
            canvas.AddComponent<Canvas>();
            var panel = GameObject.CreateUi().transform;
            panel.parent = canvas.transform;
            panel.localPosition = new Vector3(100f, 50f, 0f);
            var wallet = GameObject.CreateUi().transform;
            wallet.parent = canvas.transform;
            wallet.localPosition = new Vector3(-300f, 900f, 0f);

            var run = new Run(data, parent: panel);
            run.Play(wallet);
            run.Compare(run.Eval.Duration + 0.25f, endOffset: () => panel.InverseTransformPoint(wallet.position) - run.StartLocal);
        }

        [Test]
        public void DisabledTrack_StillTakesTime()
        {
            var data = Rich(TargetKind.RectTransform);
            data.tracks.Add(new ScaleTrack
            {
                name = "停用",
                enabled = false,
                clips = { new MotionClip { anchor = "d", offset = 0.5f, duration = 0.5f, value = new Vector4(3f, 3f, 3f, 0f) } },
            });
            data.loop = new LoopSettings { count = 2, type = MotionLoopType.Restart };
            var run = new Run(data);
            StringAssert.Contains("AppendInterval", run.Code);
            run.Play();
            run.Compare(run.Eval.TotalDuration + 0.25f);
        }

        [Test]
        public void PathOnly_And_TracksOnly()
        {
            var pathOnly = Rich(TargetKind.RectTransform);
            pathOnly.tracks.Clear();
            pathOnly.events.Clear();
            var run = new Run(pathOnly);
            StringAssert.DoesNotContain("Action<string>", run.Code);
            run.Play();
            run.Compare(run.Eval.Duration + 0.25f);

            var tracksOnly = Rich(TargetKind.RectTransform);
            tracksOnly.points.Clear();
            foreach (var track in tracksOnly.tracks)
                foreach (var clip in track.clips)
                    clip.anchor = null;
            tracksOnly.events.Clear();
            run = new Run(tracksOnly);
            StringAssert.DoesNotContain("Move(", run.Code);
            run.Play();
            run.Compare(run.Eval.Duration + 0.25f);
        }

        // ---------------------------------------------------------------- 测试数据

        static EaseSpec Ease(EaseType type) => new EaseSpec { type = type };

        /// <summary>
        /// 4 个点 3 段（含回弹缓动、自定义曲线、速度计时），缩放（重叠截断、瞬时、被完全覆盖）、旋转、透明度、颜色四条轨道，两个事件。
        /// binaryTiming 时所有时长都是 1/256 的整数倍（速度计时改成时长）。
        /// </summary>
        static MotionData Rich(TargetKind kind, float scale = 1f, bool binaryTiming = false)
        {
            var data = new MotionData { name = "harness-motion", target = kind, description = "回放测试" };
            var a = data.AddPoint(new Vector3(0f, 0f, 0f) * scale, PointType.Auto);
            var b = data.AddPoint(new Vector3(200f, 300f, 0f) * scale, PointType.Auto);
            var c = data.AddPoint(new Vector3(-100f, 500f, 0f) * scale, PointType.Corner);
            var d = data.AddPoint(new Vector3(-400f, 420f, 0f) * scale, PointType.Smooth);
            a.id = "a"; b.id = "b"; c.id = "c"; d.id = "d";
            d.inTangent = new Vector3(60f, 80f, 0f) * scale;
            d.outTangent = -d.inTangent;

            a.segment = new SegmentSettings { duration = 0.75f, ease = Ease(EaseType.OutBack) };
            b.segment = new SegmentSettings
            {
                duration = 0.25f,
                ease = new EaseSpec
                {
                    type = EaseType.Custom,
                    curve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(0.6f, 0.9f, 0.5f, 0.5f), new Keyframe(1f, 1f, 0f, 0f)),
                },
            };
            c.segment = binaryTiming
                ? new SegmentSettings { duration = 0.5f, ease = Ease(EaseType.InOutSine) }
                : new SegmentSettings { timing = TimingMode.Speed, speed = 900f * scale, ease = Ease(EaseType.InOutSine) };

            data.tracks.Add(new ScaleTrack
            {
                name = "缩放",
                clips =
                {
                    new MotionClip { anchor = "a", offset = 0.125f, duration = 0.5f, value = new Vector4(1.5f, 1.5f, 1.5f, 0f), ease = Ease(EaseType.OutBack) },
                    // 在上一个结束前开始：上一个被截断。
                    new MotionClip { anchor = "b", offset = -0.25f, duration = 0.375f, value = new Vector4(0.8f, 1.2f, 1f, 0f), ease = Ease(EaseType.InOutQuad) },
                    // 瞬时
                    new MotionClip { anchor = "b", offset = 0.25f, duration = 0f, value = new Vector4(1.1f, 1.1f, 1.1f, 0f) },
                    // 同时开始的两个：前一个被完全覆盖
                    new MotionClip { anchor = "c", offset = 0f, duration = 0.25f, value = new Vector4(2f, 2f, 2f, 0f), ease = Ease(EaseType.Linear) },
                    new MotionClip { anchor = "c", offset = 0f, duration = 0.25f, value = new Vector4(1f, 1f, 1f, 0f), ease = Ease(EaseType.OutElastic) },
                },
            });
            data.tracks.Add(new RotationTrack
            {
                name = "旋转",
                clips =
                {
                    new MotionClip { anchor = "a", offset = 0f, duration = 0.5f, value = new Vector4(0f, 0f, 360f, 0f), ease = Ease(EaseType.InOutCubic) },
                    new MotionClip { anchor = "b", offset = 0f, duration = 0.5f, value = new Vector4(0f, 0f, -45f, 0f), ease = Ease(EaseType.OutBounce) },
                },
            });
            data.tracks.Add(new AlphaTrack
            {
                name = "透明度",
                clips =
                {
                    new MotionClip { anchor = "a", offset = 0.25f, duration = 0.25f, value = new Vector4(0.3f, 0f, 0f, 0f), ease = Ease(EaseType.Linear) },
                    new MotionClip { anchor = "c", offset = 0.125f, duration = 0.5f, value = new Vector4(1f, 0f, 0f, 0f), ease = Ease(EaseType.InExpo) },
                },
            });
            data.tracks.Add(new ColorTrack
            {
                name = "颜色",
                clips = { new MotionClip { anchor = "b", offset = 0.125f, duration = 0.5f, value = new Vector4(1f, 0.5f, 0.2f, 0f), ease = Ease(EaseType.OutQuad) } },
            });
            data.events.Add(new MotionEvent { anchor = "b", offset = 0f, name = "Arrive", message = "到了" });
            data.events.Add(new MotionEvent { anchor = "c", offset = binaryTiming ? 0.25f : 0.125f, name = "Done" });
            return data;
        }

        // ---------------------------------------------------------------- 回放

        sealed class Run
        {
            public readonly MotionData Data;
            public readonly MotionEvaluator Eval;
            public readonly string Code;
            public readonly List<string> Warnings = new List<string>();
            public readonly List<(string name, float time)> Events = new List<(string, float)>();
            public Transform Target;
            public Vector3 StartLocal;
            readonly MethodInfo m_Play;
            readonly Transform m_Parent;
            readonly bool m_Ui;
            float m_Time;

            Vector4 m_InitialScale, m_InitialRotation, m_InitialAlpha, m_InitialColor;

            public Run(MotionData data, Transform parent = null)
            {
                Data = data;
                Eval = new MotionEvaluator(data);
                m_Parent = parent;
                m_Ui = data.target == TargetKind.RectTransform;
                Code = DotweenExporter.GenerateWithEventStubs(data, new DotweenExportOptions { Namespace = "Harness" }, Warnings);
                Dump(data);
                var assembly = Compile(Code);
                var type = assembly.GetTypes().Single(t => t.IsPublic && t.GetMethod("Play") != null);
                m_Play = type.GetMethod("Play");
            }

            /// <summary>设了环境变量 CURVEMOTION_EXPORT_DIR 时把导出代码写到那里（各用一个命名空间），用来拿真实 DOTween 编译检查。</summary>
            static void Dump(MotionData data)
            {
                string dir = Environment.GetEnvironmentVariable("CURVEMOTION_EXPORT_DIR");
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                int n = Directory.GetFiles(dir, "*.cs").Length + 1;
                string code = DotweenExporter.GenerateWithEventStubs(data, new DotweenExportOptions { Namespace = "Check" + n });
                File.WriteAllText(Path.Combine(dir, "Export" + n + ".cs"), code);
            }

            public Sequence Play(Transform endTarget = null)
            {
                DOTween.Reset();
                var go = m_Ui ? GameObject.CreateUi() : new GameObject();
                Target = go.transform;
                if (m_Parent != null) Target.parent = m_Parent;
                Target.localPosition = m_Ui ? new Vector3(40f, -20f, 0f) : new Vector3(0.4f, -0.2f, 1f);
                Target.localScale = new Vector3(2f, 2f, 1f);
                Target.localEulerAngles = new Vector3(0f, 0f, 30f);
                if (m_Ui)
                {
                    go.AddComponent<CanvasGroup>().alpha = 0.8f;
                    go.AddComponent<Graphic>().color = new Color(0.9f, 0.9f, 0.9f, 1f);
                }
                else
                {
                    go.AddComponent<SpriteRenderer>().color = new Color(0.9f, 0.9f, 0.9f, 0.8f);
                    // 场景物体指定 CanvasGroup 时用它（透明度起点 0.6，和 SpriteRenderer 的区分开）。
                    if (AlphaSourceOf == AlphaSource.CanvasGroup) go.AddComponent<CanvasGroup>().alpha = 0.6f;
                }
                StartLocal = Target.localPosition;
                m_InitialScale = Target.localScale;
                m_InitialRotation = Target.localEulerAngles;
                m_InitialAlpha = new Vector4(Alpha(), 0f, 0f, 0f);
                m_InitialColor = new Vector4(0.9f, 0.9f, 0.9f, 0f);

                var args = new List<object>();
                foreach (var p in m_Play.GetParameters())
                {
                    switch (p.Name)
                    {
                        case "target": args.Add(Target); break;
                        case "endTarget": args.Add(endTarget); break;
                        case "worldCamera": args.Add(null); break;
                        case "onEvent": args.Add(new Action<string>(name => Events.Add((name, m_Time)))); break;
                        default: throw new InvalidOperationException("未知参数 " + p.Name);
                    }
                }
                m_Time = 0f;
                return (Sequence)m_Play.Invoke(null, args.ToArray());
            }

            /// <summary>逐帧播放 seconds 秒，每帧对照预览。endOffset 为实际落点相对起点的偏移（没有落点时为空）。</summary>
            public void Compare(float seconds, Func<Vector3> endOffset = null, Action everyFrame = null)
            {
                float pathScale = 1f;
                for (int i = 0; i < Eval.SegmentCount; i++) pathScale = Math.Max(pathScale, Eval.GetSegmentLength(i));
                float positionTolerance = 2e-4f * pathScale;
                int frames = (int)Math.Ceiling(seconds / Dt);
                for (int frame = 1; frame <= frames; frame++)
                {
                    everyFrame?.Invoke();
                    m_Time = frame * Dt;
                    DOTween.Step(Dt);
                    if (NearLoopBoundary(m_Time)) continue;

                    float local = Eval.ToLocalTime(m_Time);
                    string at = $"t = {m_Time:F4}s（单次内 {local:F4}s）";
                    if (Eval.SegmentCount > 0)
                    {
                        var correction = endOffset != null ? endOffset() - Eval.AuthoredEndOffset : Vector3.zero;
                        var expected = StartLocal + Eval.EvaluatePosition(local, correction);
                        if (m_Ui) expected.z = StartLocal.z;
                        AssertClose(expected, Target.localPosition, positionTolerance, "位置", at);
                    }
                    for (int ti = 0; ti < Data.tracks.Count; ti++)
                    {
                        var track = Data.tracks[ti];
                        if (track == null || !track.enabled) continue;
                        switch (track)
                        {
                            case ScaleTrack _:
                                AssertClose(Eval.EvaluateTrack(ti, local, m_InitialScale), Target.localScale, 1e-4f, "缩放", at);
                                break;
                            case RotationTrack _:
                                AssertClose(Eval.EvaluateTrack(ti, local, m_InitialRotation), Target.localEulerAngles, 1e-3f, "旋转", at);
                                break;
                            case AlphaTrack _:
                                Assert.AreEqual(Eval.EvaluateTrack(ti, local, m_InitialAlpha).x, Alpha(), 1e-4f, "透明度 " + at);
                                break;
                            case ColorTrack _:
                                AssertClose(Eval.EvaluateTrack(ti, local, m_InitialColor), Rgb(), 1e-4f, "颜色", at);
                                break;
                        }
                    }
                }
            }

            bool NearLoopBoundary(float time)
            {
                if (Data.loop.count == 1 || Eval.LoopLength <= 0f) return false;
                double k = (time - Eval.LoopStart) / Eval.LoopLength;
                return Math.Abs(k - Math.Round(k)) * Eval.LoopLength < 1e-4;
            }

            /// <summary>第一条透明度轨道的“作用组件”，“自动”按导出规则换成实际用的组件。</summary>
            AlphaSource AlphaSourceOf
            {
                get
                {
                    var source = Data.tracks.OfType<AlphaTrack>().FirstOrDefault()?.source ?? AlphaSource.Auto;
                    if (source != AlphaSource.Auto) return source;
                    return m_Ui ? AlphaSource.CanvasGroup : AlphaSource.SpriteRenderer;
                }
            }

            float Alpha()
            {
                var go = Target.gameObject;
                switch (AlphaSourceOf)
                {
                    case AlphaSource.CanvasGroup: return go.GetComponent<CanvasGroup>().alpha;
                    case AlphaSource.Graphic: return go.GetComponent<Graphic>().color.a;
                    default: return go.GetComponent<SpriteRenderer>().color.a;
                }
            }

            Vector3 Rgb()
            {
                var go = Target.gameObject;
                var color = m_Ui ? go.GetComponent<Graphic>().color : go.GetComponent<SpriteRenderer>().color;
                return new Vector3(color.r, color.g, color.b);
            }

            public void AssertEvents(params (string name, float time)[] expected)
            {
                Assert.AreEqual(expected.Length, Events.Count, "事件次数：" + string.Join(", ", Events));
                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.AreEqual(expected[i].name, Events[i].name);
                    Assert.That(Events[i].time, Is.InRange(expected[i].time - 1e-5f, expected[i].time + Dt + 1e-5f), "事件 " + expected[i].name + " 的时间");
                }
            }

            static void AssertClose(Vector3 expected, Vector3 actual, float tolerance, string what, string at)
            {
                float error = (expected - actual).magnitude;
                if (error > tolerance) Assert.Fail($"{what}不一致，{at}：预览 {expected}，导出 {actual}，差 {error}");
            }
        }

        // ---------------------------------------------------------------- 编译

        static readonly Dictionary<string, Assembly> s_Compiled = new Dictionary<string, Assembly>();
        static List<MetadataReference> s_References;

        static Assembly Compile(string code)
        {
            if (s_Compiled.TryGetValue(code, out var cached)) return cached;
            if (s_References == null)
            {
                var self = typeof(Vector3).Assembly.Location;
                s_References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
                    .Where(p => !string.Equals(Path.GetFileName(p), Path.GetFileName(self), StringComparison.OrdinalIgnoreCase))
                    .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
                s_References.Add(MetadataReference.CreateFromFile(self));
            }
            var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.CSharp9));
            var compilation = CSharpCompilation.Create("Exported" + s_Compiled.Count, new[] { tree }, s_References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, warningLevel: 4));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            var problems = result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).ToList();
            if (!result.Success || problems.Count > 0)
            {
                var sb = new StringBuilder("导出代码编译不通过或有警告：\n");
                foreach (var d in problems) sb.Append(d).Append('\n');
                var lines = code.Split('\n');
                for (int i = 0; i < lines.Length; i++) sb.Append((i + 1).ToString().PadLeft(4)).Append("  ").Append(lines[i]).Append('\n');
                Assert.Fail(sb.ToString());
            }
            var assembly = Assembly.Load(stream.ToArray());
            s_Compiled[code] = assembly;
            return assembly;
        }
    }
}
