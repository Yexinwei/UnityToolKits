using UnityEditor;
using UnityEngine;

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// 编辑器窗口的分区：带色条标题的卡片，让“对象 / 时间轴 / 单段编辑 / 全局编辑 / 提示与警告 / 导出 / 说明”一眼分得开。
    /// 标题后面只放状态（例如问题数），说明文字统一放在“说明”分区。
    /// 用法：Begin(...) 画内容，End()；卡片里再分组用 BeginGroup / EndGroup。中途 ExitGUI 也没关系，IMGUI 会丢弃本次剩余的布局。
    /// </summary>
    internal static class WindowSection
    {
        public static readonly Color ObjectAccent = new Color(0.62f, 0.66f, 0.74f, 1f);
        public static readonly Color TimelineAccent = new Color(0.25f, 0.78f, 0.9f, 1f);
        public static readonly Color SingleAccent = new Color(1f, 0.78f, 0.22f, 1f);
        public static readonly Color GlobalAccent = new Color(0.42f, 0.6f, 1f, 1f);
        public static readonly Color ErrorAccent = new Color(0.95f, 0.35f, 0.3f, 1f);
        public static readonly Color WarningAccent = new Color(1f, 0.65f, 0.2f, 1f);
        public static readonly Color OkAccent = new Color(0.4f, 0.8f, 0.45f, 1f);
        public static readonly Color ExportAccent = new Color(0.6f, 0.6f, 0.6f, 1f);
        public static readonly Color JsonAccent = new Color(0.3f, 0.8f, 0.7f, 1f);
        public static readonly Color CodeAccent = new Color(0.72f, 0.5f, 1f, 1f);
        public static readonly Color RunAccent = new Color(0.4f, 0.8f, 0.45f, 1f);
        public static readonly Color HelpAccent = new Color(0.55f, 0.75f, 0.95f, 1f);

        const float HeaderHeight = 22f;
        const int Pad = 6;

        static GUIStyle s_Box;
        static GUIStyle s_Title;
        static GUIStyle s_Subtitle;
        static GUIStyle s_Group;

        static GUIStyle Box => s_Box ?? (s_Box = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(Pad, Pad, 0, Pad),
            margin = new RectOffset(4, 4, 0, 0),
        });

        static GUIStyle Title => s_Title ?? (s_Title = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip });

        static GUIStyle Subtitle => s_Subtitle ?? (s_Subtitle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight });

        static GUIStyle Group => s_Group ?? (s_Group = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(10, 6, 2, 6),
            margin = new RectOffset(0, 0, 4, 4),
        });

        static Color LineColor => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.12f) : new Color(0f, 0f, 0f, 0.15f);

        public static void Begin(string title, Color accent) => Begin(title, null, accent);

        /// <summary>subtitle 是靠右的状态文字（例如“错误 1 · 警告 2”），不是说明。</summary>
        public static void Begin(string title, string subtitle, Color accent)
        {
            GUILayout.Space(5f);
            EditorGUILayout.BeginVertical(Box);
            var r = GUILayoutUtility.GetRect(10f, HeaderHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                var bar = new Rect(r.x - Pad, r.y, r.width + Pad * 2, r.height);
                EditorGUI.DrawRect(bar, new Color(accent.r, accent.g, accent.b, EditorGUIUtility.isProSkin ? 0.16f : 0.24f));
                EditorGUI.DrawRect(new Rect(bar.x, bar.y, 4f, bar.height), accent);
                EditorGUI.DrawRect(new Rect(bar.x, bar.yMax - 1f, bar.width, 1f), LineColor);
            }
            // 副标题靠右、按需让出宽度，标题用剩下的部分（太长时截断，不重叠）。
            float subWidth = string.IsNullOrEmpty(subtitle) ? 0f : Mathf.Min(Subtitle.CalcSize(new GUIContent(subtitle)).x, r.width * 0.45f);
            GUI.Label(new Rect(r.x + 2f, r.y, r.width - subWidth - 8f, r.height), title, Title);
            if (subWidth > 0f)
                GUI.Label(new Rect(r.xMax - subWidth, r.y, subWidth, r.height), subtitle, Subtitle);
            GUILayout.Space(3f);
        }

        public static void End() => EditorGUILayout.EndVertical();

        /// <summary>卡片里的一组：自己的边框、左侧色条和粗体标题，组与组之间分得开。</summary>
        public static void BeginGroup(string title, Color accent)
        {
            var rect = EditorGUILayout.BeginVertical(Group);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4f, rect.height), accent);
                EditorGUI.DrawRect(new Rect(rect.x + 4f, rect.y, rect.width - 4f, 20f), new Color(accent.r, accent.g, accent.b, EditorGUIUtility.isProSkin ? 0.1f : 0.16f));
            }
            var r = GUILayoutUtility.GetRect(10f, 18f, GUILayout.ExpandWidth(true));
            GUI.Label(r, title, EditorStyles.boldLabel);
            GUILayout.Space(2f);
        }

        public static void EndGroup() => EditorGUILayout.EndVertical();

        /// <summary>卡片里的小标题，下面带一条细线。</summary>
        public static void SubHeading(string text)
        {
            GUILayout.Space(4f);
            var r = GUILayoutUtility.GetRect(10f, 18f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), LineColor);
            GUI.Label(r, text, EditorStyles.miniBoldLabel);
            GUILayout.Space(2f);
        }
    }
}
