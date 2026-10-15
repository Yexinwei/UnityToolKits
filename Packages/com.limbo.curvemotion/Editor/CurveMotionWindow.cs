using UnityEditor;
using UnityEngine;

namespace Limbo.CurveMotion.Editor
{
    /// <summary>曲线运动编辑器主窗口（时间轴与属性轨道将在 M3 实现）。</summary>
    public class CurveMotionWindow : EditorWindow
    {
        [MenuItem("Tools/Curve Motion/Editor")]
        public static void Open()
        {
            var window = GetWindow<CurveMotionWindow>();
            window.titleContent = new GUIContent("Curve Motion");
            window.minSize = new Vector2(480, 240);
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Curve Motion Editor", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Schema v{CurveMotionInfo.SchemaVersion}");
        }
    }
}
