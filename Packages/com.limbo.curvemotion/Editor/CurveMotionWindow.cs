using System;
using System.Collections.Generic;
using System.IO;
using Limbo.CurveMotion.Serialization;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// 曲线运动编辑器主窗口：选择资产与预览目标、编辑路径点与分段、在场景里预览、导入导出 JSON。
    /// Scene 视图中的绘制与手柄见 <see cref="PathSceneEditor"/>。属性轨道的时间轴在 M3 实现。
    /// </summary>
    public class CurveMotionWindow : EditorWindow
    {
        [SerializeField] MotionAsset m_Asset;
        [SerializeField] Transform m_Target;
        [SerializeField] bool m_EditPath = true;
        [SerializeField] bool m_Preview;
        [SerializeField] bool m_LoopPreview;
        [SerializeField] int m_Selected = -1;
        [SerializeField] float m_Time;
        [SerializeField] Vector2 m_Scroll;

        bool m_Playing;
        double m_LastTick;
        MotionEvaluator m_Eval;
        readonly MotionPreview m_PreviewState = new MotionPreview();

        [MenuItem("Tools/Curve Motion/Editor")]
        static void OpenFromMenu() => Open();

        public static CurveMotionWindow Open()
        {
            var window = GetWindow<CurveMotionWindow>();
            window.titleContent = new GUIContent("Curve Motion");
            window.minSize = new Vector2(360, 320);
            window.Show();
            return window;
        }

        /// <summary>双击 Motion Asset 时打开编辑器。</summary>
        [OnOpenAsset]
        static bool OnOpenAsset(int instanceId, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceId) as MotionAsset;
            if (asset == null) return false;
            Open().SetAsset(asset);
            return true;
        }

        // ---------------------------------------------------------------- 供 PathSceneEditor 使用

        internal MotionAsset Asset => m_Asset;
        internal Transform Target => m_Target;
        internal bool EditPath => m_EditPath;
        internal int Selected => m_Selected;
        internal bool IsUiTarget => m_Asset != null && m_Asset.data.target == TargetKind.RectTransform;

        internal MotionEvaluator Evaluator
        {
            get
            {
                if (m_Asset == null) return null;
                if (m_Eval == null || m_Eval.Data != m_Asset.data) m_Eval = new MotionEvaluator(m_Asset.data);
                return m_Eval;
            }
        }

        /// <summary>单次播放内的当前时间。</summary>
        internal float LocalTime => Evaluator?.ToLocalTime(m_Time) ?? 0f;

        /// <summary>路径点坐标 → 世界坐标的矩阵。起点对齐目标对象的初始位置。</summary>
        internal Matrix4x4 PathMatrix
        {
            get
            {
                var points = m_Asset != null ? m_Asset.data.points : null;
                var origin = points != null && points.Count > 0 ? points[0].position : Vector3.zero;
                return m_PreviewState.GetPathMatrix(m_Target) * Matrix4x4.Translate(-origin);
            }
        }

        internal void Select(int index)
        {
            m_Selected = index;
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>修改数据前调用，记录 Undo。</summary>
        internal void RecordUndo(string name)
        {
            Undo.RecordObject(m_Asset, name);
        }

        /// <summary>修改数据后调用：刷新求值器、预览和界面。</summary>
        internal void MarkChanged()
        {
            EditorUtility.SetDirty(m_Asset);
            Evaluator?.Rebuild();
            ClampSelection();
            ApplyPreview();
            Repaint();
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------- 生命周期

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            // 域重载后预览状态已丢失，避免把中途的变换当成初始值。
            m_Preview = false;
            m_Playing = false;
        }

        void OnDisable()
        {
            StopPreview();
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
        }

        void OnSceneGUI(SceneView view)
        {
            if (m_Asset == null) return;
            PathSceneEditor.Draw(this);
        }

        void OnUndoRedo()
        {
            m_Eval = null;
            ClampSelection();
            if (m_Preview && m_Asset != null) m_PreviewState.CaptureTracks(m_Asset.data);
            ApplyPreview();
            Repaint();
            SceneView.RepaintAll();
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) StopPreview();
        }

        // 保存场景前还原对象，避免把预览中的位置、缩放等存进场景。
        void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => StopPreview();

        void OnEditorUpdate()
        {
            if (!m_Playing || m_Asset == null) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - m_LastTick);
            m_LastTick = now;
            m_Time += dt;
            float end = PreviewLength;
            if (m_Time >= end)
            {
                if (m_LoopPreview && end > 0f) m_Time %= end;
                else { m_Time = end; m_Playing = false; }
            }
            ApplyPreview();
            Repaint();
            SceneView.RepaintAll();
        }

        float PreviewLength
        {
            get
            {
                var eval = Evaluator;
                if (eval == null) return 0f;
                if (!float.IsInfinity(eval.TotalDuration)) return eval.TotalDuration;
                return eval.Duration * (m_Asset.data.loop.type == MotionLoopType.Yoyo ? 2f : 1f);
            }
        }

        void SetAsset(MotionAsset asset)
        {
            if (asset == m_Asset) return;
            StopPreview();
            m_Asset = asset;
            m_Eval = null;
            m_Selected = -1;
            m_Time = 0f;
            Repaint();
            SceneView.RepaintAll();
        }

        void SetTarget(Transform target)
        {
            if (target == m_Target) return;
            bool wasPreviewing = m_Preview;
            StopPreview();
            m_Target = target;
            if (m_Target != null && m_Asset != null)
            {
                var kind = m_Target is RectTransform ? TargetKind.RectTransform : TargetKind.Transform;
                if (m_Asset.data.target != kind)
                {
                    RecordUndo("Change Motion Target Kind");
                    m_Asset.data.target = kind;
                    MarkChanged();
                }
            }
            if (wasPreviewing) StartPreview();
            SceneView.RepaintAll();
        }

        void StartPreview()
        {
            if (m_Target == null || m_Asset == null) return;
            m_PreviewState.Begin(m_Target, m_Asset.data);
            m_Preview = true;
            ApplyPreview();
        }

        void StopPreview()
        {
            m_Playing = false;
            m_Preview = false;
            m_PreviewState.End();
        }

        void ApplyPreview()
        {
            if (!m_Preview || !m_PreviewState.IsActive || Evaluator == null) return;
            m_PreviewState.Apply(Evaluator, LocalTime);
        }

        void ClampSelection()
        {
            int count = m_Asset != null ? m_Asset.data.points.Count : 0;
            if (m_Selected >= count) m_Selected = count - 1;
        }

        // ---------------------------------------------------------------- 界面

        void OnGUI()
        {
            DrawHeader();
            if (m_Asset == null)
            {
                EditorGUILayout.HelpBox("选择一个 Motion Asset，或点“新建”。也可以在 Project 窗口双击 Motion Asset 打开。", MessageType.Info);
                return;
            }

            DrawPlayback();
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            DrawPointList();
            DrawSelectedPoint();
            DrawWarnings();
            EditorGUILayout.EndScrollView();
            DrawFooter();
        }

        void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var asset = (MotionAsset)EditorGUILayout.ObjectField("Motion Asset", m_Asset, typeof(MotionAsset), false);
                if (asset != m_Asset) SetAsset(asset);
                if (GUILayout.Button("新建", GUILayout.Width(48))) CreateAsset();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var target = (Transform)EditorGUILayout.ObjectField(
                    new GUIContent("预览目标", "场景里的对象。路径从它的当前位置开始绘制，预览时在它身上播放，结束自动还原。"),
                    m_Target, typeof(Transform), true);
                if (target != m_Target) SetTarget(target);
                using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
                {
                    if (GUILayout.Button("用选中", GUILayout.Width(48))) SetTarget(Selection.activeTransform);
                }
            }

            if (m_Asset != null)
            {
                EditorGUILayout.LabelField("对象类型",
                    m_Asset.data.target == TargetKind.RectTransform ? "UI（anchoredPosition，单位像素）" : "Transform（localPosition，单位 Unity 单位）");
            }
        }

        void DrawPlayback()
        {
            var eval = Evaluator;
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                bool edit = GUILayout.Toggle(m_EditPath, new GUIContent("编辑路径", "在 Scene 视图中显示路径手柄"), EditorStyles.miniButtonLeft);
                if (edit != m_EditPath) { m_EditPath = edit; SceneView.RepaintAll(); }

                using (new EditorGUI.DisabledScope(m_Target == null))
                {
                    bool preview = GUILayout.Toggle(m_Preview, new GUIContent("预览", m_Target == null ? "先指定预览目标" : "在预览目标上应用动效"), EditorStyles.miniButtonMid);
                    if (preview != m_Preview)
                    {
                        if (preview) StartPreview(); else StopPreview();
                    }

                    if (GUILayout.Button(m_Playing ? "暂停" : "播放", EditorStyles.miniButtonMid))
                    {
                        if (m_Playing) m_Playing = false;
                        else
                        {
                            if (!m_Preview) StartPreview();
                            if (m_Time >= PreviewLength) m_Time = 0f;
                            m_Playing = true;
                            m_LastTick = EditorApplication.timeSinceStartup;
                        }
                    }
                }

                if (GUILayout.Button("停止", EditorStyles.miniButtonRight))
                {
                    m_Time = 0f;
                    StopPreview();
                    SceneView.RepaintAll();
                }
                m_LoopPreview = GUILayout.Toggle(m_LoopPreview, "循环", GUILayout.Width(44));
            }

            EditorGUI.BeginChangeCheck();
            float length = PreviewLength;
            float t = EditorGUILayout.Slider("时间", Mathf.Min(m_Time, length), 0f, Mathf.Max(length, 0.0001f));
            if (EditorGUI.EndChangeCheck())
            {
                m_Time = t;
                m_Playing = false;
                ApplyPreview();
                SceneView.RepaintAll();
            }
            EditorGUILayout.LabelField("时长", $"单次 {eval.Duration:0.###}s，路径 {eval.PathDuration:0.###}s" +
                (m_Asset.data.loop.count == 1 ? "" : m_Asset.data.loop.count < 0 ? "，无限循环" : $"，共 {m_Asset.data.loop.count} 次"));
        }

        void DrawPointList()
        {
            var data = m_Asset.data;
            var eval = Evaluator;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"路径点（{data.points.Count}）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Scene 视图：点击选点，拖动手柄移动；按住 Shift 点击路径插入点；Delete 删除选中点。", EditorStyles.wordWrappedMiniLabel);

            for (int i = 0; i < data.points.Count; i++)
            {
                var p = data.points[i];
                string label = $"{i}   {(i == 0 ? "起点" : p.type.ToString())}   t = {eval.GetPointTime(i):0.###}s";
                bool selected = i == m_Selected;
                if (GUILayout.Toggle(selected, label, EditorStyles.miniButton) != selected) Select(selected ? -1 : i);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("末尾加点"))
                {
                    RecordUndo("Add Path Point");
                    int index = MotionEditing.AppendPoint(data, IsUiTarget ? 150f : 1.5f);
                    MarkChanged();
                    Select(index);
                }
                using (new EditorGUI.DisabledScope(m_Selected <= 0))
                {
                    if (GUILayout.Button("删除选中点")) DeleteSelected();
                }
            }
        }

        internal void DeleteSelected()
        {
            if (m_Asset == null || m_Selected <= 0) return;
            RecordUndo("Delete Path Point");
            if (MotionEditing.RemovePoint(m_Asset.data, m_Selected))
            {
                m_Selected--;
                MarkChanged();
            }
        }

        void DrawSelectedPoint()
        {
            var data = m_Asset.data;
            if (m_Selected < 0 || m_Selected >= data.points.Count) return;
            var eval = Evaluator;
            var p = data.points[m_Selected];

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"点 {m_Selected}（id {p.id}）", EditorStyles.boldLabel);

            if (m_Selected == 0)
            {
                EditorGUILayout.HelpBox("起点固定在对象的初始位置，要改起点请直接移动对象。", MessageType.None);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                var pos = EditorGUILayout.Vector3Field("位置（相对起点）", p.position - data.points[0].position);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordUndo("Move Path Point");
                    if (IsUiTarget) pos.z = 0f;
                    p.position = data.points[0].position + pos;
                    MarkChanged();
                }
            }

            EditorGUI.BeginChangeCheck();
            var type = (PointType)EditorGUILayout.EnumPopup(new GUIContent("点类型",
                "Corner 尖角 / Smooth 平滑（切线镜像）/ Free 自由（两侧切线独立）/ Auto 自动平滑"), p.type);
            if (EditorGUI.EndChangeCheck())
            {
                RecordUndo("Change Point Type");
                MotionEditing.SetPointType(data, m_Selected, type);
                MarkChanged();
            }

            if (p.type == PointType.Free)
            {
                EditorGUI.BeginChangeCheck();
                var inT = EditorGUILayout.Vector3Field("入切线", p.inTangent);
                if (EditorGUI.EndChangeCheck()) { RecordUndo("Edit Tangent"); p.inTangent = inT; MarkChanged(); }
            }
            if (p.type == PointType.Free || p.type == PointType.Smooth)
            {
                EditorGUI.BeginChangeCheck();
                var outT = EditorGUILayout.Vector3Field("出切线", p.outTangent);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordUndo("Edit Tangent");
                    p.outTangent = outT;
                    if (p.type == PointType.Smooth) p.inTangent = -outT;
                    MarkChanged();
                }
            }

            if (m_Selected < data.points.Count - 1) DrawSegment(p, m_Selected, eval);
        }

        void DrawSegment(PathPoint p, int index, MotionEvaluator eval)
        {
            var seg = p.segment ?? (p.segment = new SegmentSettings());
            if (seg.ease == null) seg.ease = new EaseSpec { type = EaseType.Linear };

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"路径段 {index} → {index + 1}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var timing = (TimingMode)EditorGUILayout.EnumPopup(new GUIContent("计时方式", "Duration 填时长 / Speed 填速度，按弧长自动算时长"), seg.timing);
            float duration = seg.duration, speed = seg.speed;
            if (timing == TimingMode.Duration) duration = Mathf.Max(0f, EditorGUILayout.FloatField("时长（秒）", seg.duration));
            else speed = Mathf.Max(0f, EditorGUILayout.FloatField(IsUiTarget ? "速度（像素/秒）" : "速度（单位/秒）", seg.speed));
            var easeType = (EaseType)EditorGUILayout.EnumPopup("缓动", seg.ease.type);
            AnimationCurve curve = seg.ease.curve;
            if (easeType == EaseType.Custom)
                curve = EditorGUILayout.CurveField("自定义曲线", curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f));
            string desc = EditorGUILayout.TextField(new GUIContent("描述", "写给 AI 看的一句话，例如“向左上弹起”"), seg.desc ?? "");
            if (EditorGUI.EndChangeCheck())
            {
                RecordUndo("Edit Path Segment");
                // 从时长切到速度时，用当前时长反推速度，避免段时长突变。
                if (timing == TimingMode.Speed && seg.timing == TimingMode.Duration && seg.duration > 0f)
                    speed = eval.GetSegmentLength(index) / seg.duration;
                if (timing == TimingMode.Duration && seg.timing == TimingMode.Speed)
                    duration = eval.GetSegmentDuration(index);
                seg.timing = timing;
                seg.duration = duration;
                seg.speed = speed;
                seg.ease.type = easeType;
                seg.ease.curve = easeType == EaseType.Custom ? curve : seg.ease.curve;
                seg.desc = desc;
                MarkChanged();
            }

            EditorGUILayout.LabelField("实际时长 / 弧长",
                $"{eval.GetSegmentDuration(index):0.###}s / {eval.GetSegmentLength(index):0.##}{(IsUiTarget ? " px" : "")}");
        }

        void DrawWarnings()
        {
            var eval = Evaluator;
            var warnings = new List<string>(eval.Warnings);
            if (m_Preview)
                foreach (var name in m_PreviewState.UnsupportedTracks)
                    warnings.Add($"预览目标上没有轨道 {name} 需要的组件，该轨道不预览");
            if (warnings.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(string.Join("\n", warnings), MessageType.Warning);
        }

        void DrawFooter()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制 JSON"))
                {
                    EditorGUIUtility.systemCopyBuffer = MotionJson.ToJson(m_Asset.data);
                    ShowNotification(new GUIContent("JSON 已复制到剪贴板"));
                }
                if (GUILayout.Button("导出 JSON…")) ExportJson();
                if (GUILayout.Button("导入 JSON…")) ImportJson();
            }
        }

        // ---------------------------------------------------------------- 资产与 JSON

        void CreateAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject("新建 Motion Asset", "NewMotion", "asset", "选择保存位置");
            if (string.IsNullOrEmpty(path)) return;

            var asset = CreateInstance<MotionAsset>();
            bool ui = m_Target == null || m_Target is RectTransform;
            float unit = ui ? 100f : 1f;
            var data = asset.data;
            data.name = Path.GetFileNameWithoutExtension(path);
            data.target = ui ? TargetKind.RectTransform : TargetKind.Transform;
            data.AddPoint(Vector3.zero);
            data.AddPoint(new Vector3(1.5f, 2f, 0f) * unit);
            data.AddPoint(new Vector3(3f, 0f, 0f) * unit);

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            SetAsset(asset);
            EditorGUIUtility.PingObject(asset);
        }

        void ExportJson()
        {
            string path = EditorUtility.SaveFilePanel("导出 JSON", "", (m_Asset.data.name ?? "Motion") + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, MotionJson.ToJson(m_Asset.data));
            ShowNotification(new GUIContent("已导出"));
        }

        void ImportJson()
        {
            string path = EditorUtility.OpenFilePanel("导入 JSON", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            var warnings = new List<string>();
            MotionData imported;
            try
            {
                imported = MotionJson.FromJson(File.ReadAllText(path), warnings);
            }
            catch (Exception e) when (e is FormatException || e is IOException)
            {
                EditorUtility.DisplayDialog("导入失败", e.Message, "确定");
                return;
            }

            StopPreview();
            RecordUndo("Import Motion JSON");
            m_Asset.data = imported;
            m_Eval = null;
            m_Selected = -1;
            MarkChanged();
            if (warnings.Count > 0) EditorUtility.DisplayDialog("导入完成，但有警告", string.Join("\n", warnings), "确定");
        }
    }
}
