using System;
using System.Collections.Generic;
using Limbo.CurveMotion.Editor.Export;
using NUnit.Framework;
using UnityEngine;

namespace Limbo.CurveMotion.Tests
{
    /// <summary>
    /// 导出代码的结构检查。播放结果与预览是否一致由 tools/verify 的回放测试逐帧对照（需要 Roslyn，只在命令行跑）。
    /// </summary>
    public class DotweenExporterTests
    {
        static MotionData Line(TargetKind kind, params float[] durations)
        {
            var data = new MotionData { name = "coin-fly", target = kind };
            for (int i = 0; i <= durations.Length; i++)
            {
                var p = data.AddPoint(new Vector3(i * 100f, 0f, 0f), PointType.Corner);
                p.id = "p" + i;
                if (i < durations.Length) p.segment.duration = durations[i];
            }
            return data;
        }

        static string Generate(MotionData data, List<string> warnings = null, DotweenExportOptions options = null) =>
            DotweenExporter.Generate(data, options, warnings);

        [Test]
        public void Ui_PlaySignatureAndPath()
        {
            var code = Generate(Line(TargetKind.RectTransform, 0.5f, 0.25f));
            StringAssert.Contains("public static class CoinFlyMotion", code);
            StringAssert.Contains("public const float Duration = 0.75f;", code);
            StringAssert.Contains("public static Sequence Play(RectTransform target)", code);
            StringAssert.Contains("var start = target.anchoredPosition;", code);
            StringAssert.Contains("seq.Insert(0f, DOVirtual.Float(0f, 1f, 0.5f, x => Move(0, x)).SetEase(Ease.Linear));", code);
            StringAssert.Contains("seq.Insert(0.5f, DOVirtual.Float(0f, 1f, 0.25f, x => Move(1, x)).SetEase(Ease.Linear));", code);
            StringAssert.DoesNotContain("SetLoops", code);
            StringAssert.DoesNotContain("using UnityEngine.UI;", code, "没有透明度、颜色轨道时不需要 UGUI");
        }

        [Test]
        public void Scene_UsesLocalPosition()
        {
            var code = Generate(Line(TargetKind.Transform, 1f));
            StringAssert.Contains("public static Sequence Play(Transform target)", code);
            StringAssert.Contains("target.localPosition = start + offset;", code);
        }

        [Test]
        public void Output_IsDeterministic()
        {
            var data = Line(TargetKind.RectTransform, 0.5f, 0.25f);
            Assert.AreEqual(Generate(data), Generate(data));
        }

        [Test]
        public void Namespace_WrapsClass()
        {
            var code = Generate(Line(TargetKind.RectTransform, 0.5f), options: new DotweenExportOptions { Namespace = "Game.Fx", ClassName = "CoinFly" });
            StringAssert.Contains("namespace Game.Fx\n{\n", code);
            StringAssert.Contains("    public static class CoinFly\n", code);
            StringAssert.EndsWith("    }\n}\n", code);
        }

        [Test]
        public void InvalidClassName_FallsBackWithWarning()
        {
            var warnings = new List<string>();
            var code = Generate(Line(TargetKind.RectTransform, 0.5f), warnings, new DotweenExportOptions { ClassName = "class" });
            StringAssert.Contains("public static class CurveMotion", code);
            AssertHas(warnings, "不是合法的 C# 标识符");
        }

        [Test]
        public void SegmentEase_CustomCurveUsesNormalizedHelper()
        {
            var data = Line(TargetKind.RectTransform, 0.5f);
            data.points[0].segment.ease = new EaseSpec { type = EaseType.Custom, curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)) };
            var code = Generate(data);
            StringAssert.Contains("static readonly AnimationCurve Curve1 = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));", code);
            StringAssert.Contains(".SetEase(Part(Curve1, 0.5f))", code);
            StringAssert.Contains("static EaseFunction Part(AnimationCurve curve", code);
        }

        [Test]
        public void Clips_RelativeTargetsAndTruncation()
        {
            var data = Line(TargetKind.RectTransform, 1f);
            var scale = new ScaleTrack();
            scale.AddClip("p0", 0f, 0.5f).value = new Vector4(1.5f, 1.5f, 1f, 0f);
            scale.AddClip("p0", 0.25f, 0.5f).value = new Vector4(1f, 1f, 1f, 0f);
            data.tracks.Add(scale);
            var code = Generate(data);
            StringAssert.Contains("var scale0 = target.localScale;", code);
            // 前一个片段在 0.25 秒被截断：时长 0.25，缓动按完整 0.5 秒计算。
            StringAssert.Contains("seq.Insert(0f, target.DOScale(Vector3.Scale(scale0, new Vector3(1.5f, 1.5f, 1f)), 0.25f).SetEase(Part(Ease.OutQuad, 0.5f)));", code);
            StringAssert.Contains("seq.Insert(0.25f, target.DOScale(scale0, 0.5f).SetEase(Ease.OutQuad));", code);
            StringAssert.Contains("static EaseFunction Part(Ease ease", code);
        }

        [Test]
        public void Clips_FullyOverriddenAreSkipped()
        {
            var data = Line(TargetKind.RectTransform, 1f);
            var alpha = new AlphaTrack();
            alpha.AddClip("p0", 0.2f, 0.3f).value = new Vector4(0.5f, 0f, 0f, 0f);
            alpha.AddClip("p0", 0.2f, 0.3f).value = new Vector4(0f, 0f, 0f, 0f);
            data.tracks.Add(alpha);
            var code = Generate(data);
            StringAssert.DoesNotContain(", 0.5f, 0.3f)", code);
            StringAssert.Contains("SetAlpha(canvasGroup, graphic, v); }, 0f, 0.3f)", code);
            StringAssert.Contains("using UnityEngine.UI;", code);
        }

        [Test]
        public void InstantClip_NeverUsesZeroDuration()
        {
            var data = Line(TargetKind.RectTransform, 1f);
            var scale = new ScaleTrack();
            scale.AddClip("p0", 0f, 0f).value = new Vector4(0f, 0f, 0f, 0f);
            scale.AddClip("p0", 0.5f, 0f).value = new Vector4(2f, 2f, 2f, 0f);
            data.tracks.Add(scale);
            var code = Generate(data);
            StringAssert.DoesNotContain(", 0f).SetEase", code);
            StringAssert.Contains("seq.Insert(0f, DOVirtual.Float(0f, 1f, 0.0001f, _ => { target.localScale = new Vector3(0f, 0f, 0f); }).SetEase(Ease.Linear));", code);
            StringAssert.Contains("seq.Insert(0.4999f, target.DOScale(Vector3.Scale(scale0, new Vector3(2f, 2f, 2f)), 0.0001f).SetEase(Ease.Linear));", code);
        }

        [Test]
        public void Rotation_TracksEulerInVariable()
        {
            var data = Line(TargetKind.Transform, 1f);
            var rotation = new RotationTrack();
            rotation.AddClip("p0", 0f, 1f).value = new Vector4(0f, 0f, 720f, 0f);
            data.tracks.Add(rotation);
            var code = Generate(data);
            StringAssert.Contains("var rotation0 = target.localEulerAngles;", code);
            StringAssert.Contains("DOTween.To(() => rotation, v => { target.localEulerAngles = rotation = v; }, rotation0 + new Vector3(0f, 0f, 720f), 1f)", code);
        }

        [Test]
        public void Alpha_Source_PicksComponent()
        {
            var data = Line(TargetKind.RectTransform, 1f);
            var alpha = new AlphaTrack { source = AlphaSource.Graphic };
            alpha.AddClip("p0", 0f, 1f).value = new Vector4(0f, 0f, 0f, 0f);
            data.tracks.Add(alpha);
            var code = Generate(data);
            StringAssert.Contains("var graphic = target.GetComponent<Graphic>();", code);
            StringAssert.Contains("static void SetAlpha(Graphic graphic, float alpha)", code);
            StringAssert.DoesNotContain("CanvasGroup", code, "指定 Graphic 时不碰 CanvasGroup");

            alpha.source = AlphaSource.CanvasGroup;
            data.target = TargetKind.Transform;
            code = Generate(data);
            StringAssert.Contains("var canvasGroup = target.GetComponent<CanvasGroup>();", code);
            StringAssert.Contains("static void SetAlpha(CanvasGroup group, float alpha)", code);
            StringAssert.DoesNotContain("SpriteRenderer", code, "场景物体也能指定 CanvasGroup");
            StringAssert.DoesNotContain("using UnityEngine.UI;", code);

            alpha.source = AlphaSource.SpriteRenderer;
            data.target = TargetKind.RectTransform;
            code = Generate(data);
            StringAssert.Contains("static void SetAlpha(SpriteRenderer sprite, float alpha)", code);
            StringAssert.DoesNotContain("using UnityEngine.UI;", code);
        }

        [Test]
        public void SceneAlphaAndColor_UseSpriteRenderer()
        {
            var data = Line(TargetKind.Transform, 1f);
            var alpha = new AlphaTrack();
            alpha.AddClip("p0", 0f, 1f).value = new Vector4(0f, 0f, 0f, 0f);
            var color = new ColorTrack();
            color.AddClip("p0", 0f, 1f).value = new Vector4(1f, 0f, 0f, 0f);
            data.tracks.Add(alpha);
            data.tracks.Add(color);
            var code = Generate(data);
            StringAssert.Contains("var sprite = target.GetComponent<SpriteRenderer>();", code);
            StringAssert.Contains("static float GetAlpha(SpriteRenderer sprite)", code);
            StringAssert.Contains("static Vector3 GetRgb(SpriteRenderer sprite)", code);
            StringAssert.DoesNotContain("using UnityEngine.UI;", code);
        }

        [Test]
        public void Events_AddCallbackParameter()
        {
            var data = Line(TargetKind.RectTransform, 1f);
            data.events.Add(new MotionEvent { anchor = "p1", name = "OnArrive", message = "到了\"钱包\"" });
            var code = Generate(data);
            StringAssert.Contains("public static Sequence Play(RectTransform target, Action<string> onEvent = null)", code);
            StringAssert.Contains("seq.InsertCallback(1f, () => { OnArrive(target); onEvent?.Invoke(\"OnArrive\"); });", code);
            StringAssert.Contains("public static partial class", code);
            StringAssert.Contains("using System;", code);
        }

        [TestCase("coin land", "OnCoinLand")]
        [TestCase("OnArrive", "OnArrive")]
        [TestCase("once", "OnOnce")]
        [TestCase("", "OnEvent")]
        [TestCase("2nd hit", "On2ndHit")]
        [TestCase("落地", "On落地")]
        public void Events_MethodName(string eventName, string expected)
        {
            Assert.AreEqual(expected, EventStubs.MethodName(eventName));
        }

        [Test]
        public void EventStubs_GenerateAndMerge()
        {
            var data = Line(TargetKind.RectTransform, 1f);
            data.events.Add(new MotionEvent { anchor = "p1", name = "land", message = "播放 FX_CoinBurst" });
            data.events.Add(new MotionEvent { anchor = "p0", name = "land" });
            string stubs = EventStubs.Generate(data, "CoinFly", "Game.Fx");
            StringAssert.Contains("public static partial class CoinFly", stubs);
            StringAssert.Contains("static void OnLand(Transform target)", stubs);
            StringAssert.Contains("/// 特效说明：播放 FX_CoinBurst", stubs);
            StringAssert.Contains("Debug.LogWarning(\"<color=#FF8C00><b>【动效事件未接入】</b></color> CoinFly · land（1s）\\n特效说明：播放 FX_CoinBurst\\n在 CoinFly.Events.cs 的 OnLand 里实现\", target);", stubs);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(stubs, "static void OnLand").Count);

            // 用户改过方法体；之后加了新事件、删了旧事件。
            string edited = stubs.Replace("// TODO 在这里写事件要做的事，写好后删掉下面这行提示。", "PlayFx(); // mine");
            data.events.Clear();
            data.events.Add(new MotionEvent { anchor = "p1", name = "bounce" });
            var merged = EventStubs.Merge(edited, data, "CoinFly", "Game.Fx");
            CollectionAssert.AreEqual(new[] { "OnBounce" }, merged.added);
            CollectionAssert.AreEqual(new[] { "OnLand" }, merged.unused);
            StringAssert.Contains("PlayFx(); // mine", merged.code);
            StringAssert.Contains("static void OnBounce(Transform target)", merged.code);
            Assert.Less(merged.code.IndexOf("OnBounce"), merged.code.LastIndexOf("    }\n}"));
            Assert.IsTrue(merged.code.TrimEnd().EndsWith("    }\n}"));

            var again = EventStubs.Merge(merged.code, data, "CoinFly", "Game.Fx");
            Assert.AreEqual(merged.code, again.code);
            Assert.AreEqual(0, again.added.Count);
        }

        [TestCase(3, MotionLoopType.Restart, "seq.SetLoops(3, LoopType.Restart);")]
        [TestCase(-1, MotionLoopType.Yoyo, "seq.SetLoops(-1, LoopType.Yoyo);")]
        [TestCase(0, MotionLoopType.Yoyo, null)]
        public void WholeLoop(int count, MotionLoopType type, string expected)
        {
            var data = Line(TargetKind.RectTransform, 0.5f, 0.5f);
            data.loop = new LoopSettings { count = count, type = type, scope = MotionLoopScope.Whole };
            var code = Generate(data);
            if (expected != null) StringAssert.Contains(expected, code);
            else StringAssert.DoesNotContain("SetLoops", code);
        }

        [Test]
        public void TailLoop_Finite_NestsLoopSequence()
        {
            var data = Line(TargetKind.RectTransform, 0.5f, 0.25f);
            data.loop = new LoopSettings { count = 3, type = MotionLoopType.Yoyo, scope = MotionLoopScope.LastSegment };
            var code = Generate(data);
            StringAssert.Contains("var loop = DOTween.Sequence().SetEase(Ease.Linear);", code);
            StringAssert.Contains("loop.Insert(0f, DOVirtual.Float(0f, 1f, 0.25f, x => Move(1, x))", code);
            StringAssert.Contains("seq.Insert(0.5f, loop.SetLoops(3, LoopType.Yoyo));", code);
        }

        [Test]
        public void TailLoop_Infinite_StartsLoopAfterHead()
        {
            var data = Line(TargetKind.RectTransform, 0.5f, 0.25f);
            data.loop = new LoopSettings { count = -1, type = MotionLoopType.Restart, scope = MotionLoopScope.LastSegment };
            var code = Generate(data);
            Assert.Less(code.IndexOf("var loop = ", StringComparison.Ordinal), code.IndexOf("var seq = ", StringComparison.Ordinal), "先建 loop，衔接处不多走一帧");
            StringAssert.Contains(".SetLoops(-1, LoopType.Restart).Pause();", code);
            StringAssert.Contains(".SetAutoKill(false);", code);
            StringAssert.Contains("seq.InsertCallback(0.5f, () => loop.Play());", code);
            StringAssert.Contains("seq.OnKill(() => loop.Kill());", code);
        }

        [Test]
        public void TailLoop_SplitsClipAcrossLoopStart()
        {
            var data = Line(TargetKind.RectTransform, 0.5f, 0.5f);
            var scale = new ScaleTrack();
            scale.AddClip("p0", 0.25f, 0.5f).value = new Vector4(2f, 2f, 2f, 0f);
            data.tracks.Add(scale);
            data.loop = new LoopSettings { count = 2, scope = MotionLoopScope.LastSegment };
            var code = Generate(data);
            StringAssert.Contains("seq.Insert(0.25f, target.DOScale(Vector3.Scale(scale0, new Vector3(2f, 2f, 2f)), 0.25f).SetEase(Part(Ease.OutQuad, 0.5f)));", code);
            StringAssert.Contains("（接上一截）", code);
            StringAssert.Contains("loop.Insert(0f, DOVirtual.Float(0f, 1f, 0.25f, x => { target.localScale = Vector3.LerpUnclamped(scale0, Vector3.Scale(scale0, new Vector3(2f, 2f, 2f)), x); }).SetEase(Part(Ease.OutQuad, 0.5f, 0.25f)));", code);
        }

        [Test]
        public void EndBound_AddsEndTargetParameters()
        {
            var data = Line(TargetKind.RectTransform, 0.5f);
            data.endTarget = new EndTarget { bound = true, kind = TargetKind.Transform, hint = "钱包" };
            var code = Generate(data);
            StringAssert.Contains("public static Sequence Play(RectTransform target, Transform endTarget = null, Camera worldCamera = null)", code);
            StringAssert.Contains("static readonly Vector3 AuthoredEnd = new Vector3(100f, 0f, 0f);", code);
            StringAssert.Contains("correction = end - AuthoredEnd;", code);
            StringAssert.Contains("static bool TryGetEndOffset(", code);
            StringAssert.Contains("落点对象（钱包）", code);
        }

        [Test]
        public void DisabledTrack_PadsSequence()
        {
            var data = Line(TargetKind.RectTransform, 0.5f);
            var scale = new ScaleTrack { enabled = false };
            scale.AddClip("p1", 0f, 0.5f);
            data.tracks.Add(scale);
            var code = Generate(data);
            StringAssert.DoesNotContain("DOScale", code);
            StringAssert.Contains("seq.AppendInterval(0.5f);", code);
        }

        [Test]
        public void ManualDesc_GoesIntoComments()
        {
            var data = Line(TargetKind.RectTransform, 0.5f);
            data.points[0].segment.desc = "弹出\n第二行 */ 不会截断注释";
            var code = Generate(data);
            StringAssert.Contains("// 第 1 段：弹出 第二行 * / 不会截断注释", code);
        }

        [Test]
        public void UnsupportedAndDuplicateTracks_Warn()
        {
            var data = Line(TargetKind.RectTransform, 0.5f);
            var custom = new NoCodeGenTrack { name = "自定义" };
            custom.AddClip("p0", 0f, 0.5f);
            data.tracks.Add(custom);
            var a = new ScaleTrack();
            a.AddClip("p0", 0f, 0.5f);
            var b = new ScaleTrack();
            b.AddClip("p0", 0f, 0.5f);
            data.tracks.Add(a);
            data.tracks.Add(b);
            var warnings = new List<string>();
            Generate(data, warnings);
            AssertHas(warnings, "没有 DOTween 代码生成器");
            AssertHas(warnings, "有多条 Scale 轨道");
        }

        [Test]
        public void Registry_FindsBuiltinGenerators()
        {
            foreach (var type in new[] { typeof(ScaleTrack), typeof(RotationTrack), typeof(AlphaTrack), typeof(ColorTrack) })
                Assert.IsTrue(TrackCodeGenRegistry.TryGet(type, out _), type.Name);
            Assert.IsFalse(TrackCodeGenRegistry.TryGet(typeof(NoCodeGenTrack), out _));
        }

        [TestCase("demo-coin-fly", "DemoCoinFlyMotion")]
        [TestCase("coin fly", "CoinFlyMotion")]
        [TestCase("CoinMotion", "CoinMotion")]
        [TestCase("3d heart", "Motion3dHeartMotion")]
        [TestCase("金币", "金币Motion")]
        [TestCase("", "CurveMotion")]
        [TestCase("--", "CurveMotion")]
        public void ClassName_FromMotionName(string name, string expected)
        {
            Assert.AreEqual(expected, CSharpLiterals.ClassName(name));
            Assert.IsTrue(CSharpLiterals.IsIdentifier(expected));
        }

        [TestCase(0f, "0f")]
        [TestCase(0.5f, "0.5f")]
        [TestCase(-12f, "-12f")]
        [TestCase(0.1f, "0.1f")]
        [TestCase(float.NaN, "0f")]
        public void FloatLiteral(float value, string expected) => Assert.AreEqual(expected, CSharpLiterals.Float(value));

        [Test]
        public void FloatLiteral_RoundTrips()
        {
            foreach (float v in new[] { 1f / 3f, 0.60041f, 1451.0001f, 1e-6f, 123456.79f })
            {
                string s = CSharpLiterals.Float(v);
                StringAssert.EndsWith("f", s);
                Assert.AreEqual(v, float.Parse(s.TrimEnd('f'), System.Globalization.CultureInfo.InvariantCulture), s);
            }
        }

        [Test]
        public void StringLiteral_Escapes()
        {
            Assert.AreEqual("\"a\\\"b\\\\c\\nd\"", CSharpLiterals.String("a\"b\\c\nd"));
        }

        static void AssertHas(List<string> warnings, string text) =>
            Assert.IsTrue(warnings.Exists(w => w.Contains(text)), "没有包含“" + text + "”的警告：\n" + string.Join("\n", warnings));

        /// <summary>没有对应代码生成器的轨道（不加 MotionTrack 特性，免得出现在编辑器的轨道菜单里）。</summary>
        [Serializable]
        class NoCodeGenTrack : MotionTrack
        {
            public override int Dimension => 1;
            public override ValueMode Mode => ValueMode.Absolute;
            public override Vector4 DefaultValue => Vector4.zero;
        }
    }
}
