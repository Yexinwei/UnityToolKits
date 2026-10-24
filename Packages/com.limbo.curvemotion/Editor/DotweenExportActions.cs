using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Limbo.CurveMotion.Editor.Export;
using UnityEditor;
using UnityEngine;

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// 窗口里“复制 DOTween 代码 / 导出 C# / Play 模式试播”的实现，以及导出前能提前发现的问题。
    /// 包本身不引用 DOTween，试播通过反射调用导出的类。
    /// </summary>
    static class DotweenExportActions
    {
        const string NamespaceKey = "Limbo.CurveMotion.ExportNamespace";
        const string FolderKey = "Limbo.CurveMotion.ExportFolder";
        const string ExportedClassKey = "Limbo.CurveMotion.ExportedClass.";

        static MotionPreview s_RunState = new MotionPreview();
        static object s_RunSequence;
        static bool? s_HasDotween;

        [InitializeOnLoadMethod]
        static void Init()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change != PlayModeStateChange.ExitingPlayMode) return;
                s_RunState = new MotionPreview();
                s_RunSequence = null;
            };
        }

        /// <summary>导出代码的命名空间（整个工程共用，存在 EditorPrefs）。为空时放在全局命名空间。</summary>
        public static string Namespace
        {
            get => EditorPrefs.GetString(NamespaceKey, "");
            set => EditorPrefs.SetString(NamespaceKey, value ?? "");
        }

        /// <summary>工程里有没有 DOTween（找 DG.Tweening.DOTween 类型）。脚本重新编译后静态变量清空，会重新检查。</summary>
        public static bool HasDotween
        {
            get
            {
                if (s_HasDotween == null)
                {
                    s_HasDotween = false;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        if (assembly.GetType("DG.Tweening.DOTween", false) != null) { s_HasDotween = true; break; }
                }
                return s_HasDotween.Value;
            }
        }

        public static string ClassName(MotionData data) => CSharpLiterals.ClassName(data.name);

        static string Generate(MotionData data, List<string> warnings) =>
            DotweenExporter.Generate(data, new DotweenExportOptions { Namespace = Namespace.Trim() }, warnings);

        public static void Copy(EditorWindow window, MotionData data)
        {
            var warnings = new List<string>();
            EditorGUIUtility.systemCopyBuffer = Generate(data, warnings);
            string note = HasDotween ? "" : "（本工程没装 DOTween）";
            window.ShowNotification(new GUIContent(warnings.Count > 0 ? $"已复制{note}，有 {warnings.Count} 条警告" : "DOTween 代码已复制" + note));
            Report(warnings);
        }

        public static void Export(EditorWindow window, MotionData data)
        {
            if (!HasDotween && !EditorUtility.DisplayDialog("没找到 DOTween",
                    "这个工程里没找到 DOTween。导出的代码放进 Assets 会编译报错，可以导出到工程外再拷到装了 DOTween 的工程里。", "仍然导出", "取消"))
                return;

            string folder = EditorPrefs.GetString(FolderKey, "");
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                // 测试工程里默认放到测试场景旁边，方便在 Play 模式里试播。
                string test = Path.Combine(Application.dataPath, "CurveMotionTest");
                folder = Directory.Exists(test) ? test : Application.dataPath;
            }
            string path = EditorUtility.SaveFilePanel("导出 DOTween 代码", folder, ClassName(data) + ".cs", "cs");
            if (string.IsNullOrEmpty(path)) return;

            var warnings = new List<string>();
            // 类名跟文件名走，方便在 Project 里找。
            string className = Path.GetFileNameWithoutExtension(path);
            if (!CSharpLiterals.IsIdentifier(className)) className = null;
            string code = DotweenExporter.Generate(data, new DotweenExportOptions { ClassName = className, Namespace = Namespace.Trim() }, warnings);
            File.WriteAllText(path, code);
            EditorPrefs.SetString(FolderKey, Path.GetDirectoryName(path));
            string ns = Namespace.Trim();
            EditorPrefs.SetString(ExportedClassKey + data.name, (ns.Length > 0 ? ns + "." : "") + (className ?? ClassName(data)));

            string assetPath = ToAssetPath(path);
            if (assetPath != null)
            {
                AssetDatabase.ImportAsset(assetPath);
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath));
            }
            window.ShowNotification(new GUIContent(warnings.Count > 0 ? $"已导出，有 {warnings.Count} 条警告" : "已导出 " + Path.GetFileName(path)));
            Report(warnings);
        }

        /// <summary>
        /// Play 模式里用导出的类在预览目标上真的跑一遍 DOTween，和编辑器预览对比。
        /// 再点一次先停掉上一次、把对象还原到第一次试播前的状态再播。
        /// </summary>
        public static void RunInPlayMode(EditorWindow window, MotionData data, Transform target, Transform endTarget, Camera camera)
        {
            var type = FindExportedType(data, out string fullName);
            if (type == null)
            {
                window.ShowNotification(new GUIContent($"没找到导出的类 {fullName}：先“导出 C#…”到 Assets 下，退出 Play 模式等编译完再试"));
                return;
            }
            var play = type.GetMethod("Play", BindingFlags.Public | BindingFlags.Static);
            if (play == null) { window.ShowNotification(new GUIContent(fullName + " 里没有 Play 方法")); return; }

            var parameters = play.GetParameters();
            if (parameters.Length == 0 || !parameters[0].ParameterType.IsInstanceOfType(target))
            {
                window.ShowNotification(new GUIContent(parameters.Length > 0 && parameters[0].ParameterType == typeof(RectTransform)
                    ? "这个动效是 UI 动效，预览目标要是 RectTransform" : "预览目标的类型不对"));
                return;
            }

            StopRun();
            s_RunState.Begin(target, data);

            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                switch (parameters[i].Name)
                {
                    case "target": args[i] = target; break;
                    case "endTarget": args[i] = endTarget; break;
                    case "worldCamera": args[i] = camera; break;
                    default: args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null; break;
                }
            }
            try
            {
                s_RunSequence = play.Invoke(null, args);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
                window.ShowNotification(new GUIContent("导出的代码运行出错，见 Console"));
            }
        }

        /// <summary>
        /// 试播中的对象记下的起始 localPosition。试播时对象在动，窗口算路径位置和终点跟随要用起始位置，
        /// 否则会把移动中的位置当起点，改坏路径终点。
        /// </summary>
        public static bool TryGetRunStart(Transform target, out Vector3 start)
        {
            start = default;
            if (target == null || !s_RunState.IsActive || s_RunState.Target != target) return false;
            start = s_RunState.GetStartLocalPosition(target);
            return true;
        }

        /// <summary>停掉试播，对象还原到试播前的状态。</summary>
        public static void StopRun()
        {
            Kill(s_RunSequence);
            s_RunSequence = null;
            s_RunState.End();
        }

        /// <summary>按“导出 C#…”时记下的类名找，找不到再按默认类名找。</summary>
        static Type FindExportedType(MotionData data, out string fullName)
        {
            string ns = Namespace.Trim();
            string fallback = (ns.Length > 0 ? ns + "." : "") + ClassName(data);
            fullName = EditorPrefs.GetString(ExportedClassKey + data.name, fallback);
            foreach (var name in new[] { fullName, fallback })
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType(name, false);
                    if (type != null) { fullName = name; return type; }
                }
            return null;
        }

        /// <summary>反射调用 DOTween 的 TweenExtensions.Kill(Tween, bool)。</summary>
        static void Kill(object tween)
        {
            if (tween == null) return;
            var assembly = tween.GetType().Assembly;
            var tweenType = assembly.GetType("DG.Tweening.Tween");
            var kill = assembly.GetType("DG.Tweening.TweenExtensions")?.GetMethod("Kill", new[] { tweenType, typeof(bool) });
            kill?.Invoke(null, new[] { tween, (object)false });
        }

        static void Report(List<string> warnings)
        {
            if (warnings.Count == 0) return;
            Debug.LogWarning("[Curve Motion] 导出 DOTween 代码时的警告：\n" + string.Join("\n", warnings));
        }

        /// <summary>工程 Assets 下的绝对路径转成 "Assets/…"，工程外返回 null。</summary>
        static string ToAssetPath(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (!full.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase)) return null;
            return "Assets" + full.Substring(assets.Length);
        }

        /// <summary>不用生成代码就能发现的导出问题，放进窗口的“提示与警告”。</summary>
        public static void CollectProblems(MotionData data, List<(MessageType type, string text)> list)
        {
            var seen = new HashSet<Type>();
            foreach (var track in data.tracks)
            {
                if (track == null || !track.enabled || track.clips.Count == 0) continue;
                string name = string.IsNullOrEmpty(track.name) ? track.DisplayName : track.name;
                if (!TrackCodeGenRegistry.TryGet(track.GetType(), out _))
                    list.Add((MessageType.Warning, $"轨道 {name}（{track.TypeId}）没有 DOTween 代码生成器，导出代码时会跳过。"));
                else if (!seen.Add(track.GetType()))
                    list.Add((MessageType.Warning, $"有多条 {track.DisplayName} 轨道：导出的 DOTween 代码里各轨道只在自己的片段期间生效，可能和预览不同，建议合并成一条。"));
            }
        }
    }
}
