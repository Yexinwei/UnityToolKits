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
    /// 曲线运动编辑器主窗口：选择资产与预览目标、时间轴（路径段、事件、属性轨道）、选中项的参数、
    /// 在场景里预览、导入导出 JSON。Scene 视图中的绘制与手柄见 <see cref="PathSceneEditor"/>，
    /// 时间轴的绘制与拖拽见 <see cref="TimelineView"/>。
    /// </summary>
    public class CurveMotionWindow : EditorWindow
    {
        [SerializeField] MotionAsset m_Asset;
        [SerializeField] Transform m_Target;
        [SerializeField] bool m_EditPath = true;
        [SerializeField] bool m_Preview;
        [SerializeField] bool m_LoopPreview;
        [SerializeField] float m_Time;
        [SerializeField] Vector2 m_Scroll;

        // 选中项：路径点（含它出发的路径段）、轨道、轨道上的片段、事件，同一时间只有一类有效。
        [SerializeField] int m_Selected = -1;
        [SerializeField] int m_SelTrack = -1;
        [SerializeField] int m_SelClip = -1;
        [SerializeField] int m_SelEvent = -1;

        [SerializeField] bool m_SnapTimeline = true;
        [SerializeField] bool m_GridSnap;
        [SerializeField] float m_GridSizeUi = 10f;
        [SerializeField] float m_GridSize3D = 0.1f;
        [SerializeField] bool m_ShowSettings = true;

        bool m_Playing;
        double m_LastTick;
        MotionEvaluator m_Eval;
        readonly MotionPreview m_PreviewState = new MotionPreview();
        readonly TimelineView m_Timeline = new TimelineView();

        // 片段/事件剪贴板，跨资产可用。
        static MotionClip s_ClipboardClip;
        static string s_ClipboardTrackType;
        static MotionEvent s_ClipboardEvent;

        [MenuItem("Tools/Curve Motion/Editor")]
        static void OpenFromMenu() => Open();

        public static CurveMotionWindow Open()
        {
            var window = GetWindow<CurveMotionWindow>();
            window.titleContent = new GUIContent("Curve Motion");
            window.minSize = new Vector2(420, 480);
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

        // ---------------------------------------------------------------- 供 PathSceneEditor / TimelineView 使用

        internal MotionAsset Asset => m_Asset;
        internal Transform Target => m_Target;
        internal bool EditPath => m_EditPath;
        internal int Selected => m_Selected;
        internal int SelectedTrack => m_SelTrack;
        internal int SelectedClip => m_SelClip;
        internal int SelectedEvent => m_SelEvent;
        internal bool SnapTimeline => m_SnapTimeline;
        internal bool IsUiTarget => m_Asset != null && m_Asset.data.target == TargetKind.RectTransform;
        internal bool HasClipboard => s_ClipboardClip != null || s_ClipboardEvent != null;

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

        /// <summary>选中片段时，返回它在单次播放中的生效时间段，Scene 视图据此高亮对应的路径。</summary>
        internal bool TryGetSelectedClipSpan(out float start, out float end, out Color color)
        {
            start = end = 0f;
            color = Color.white;
            if (m_Asset == null || m_SelClip < 0 || Evaluator == null) return false;
            if (!Evaluator.TryGetClipRange(m_SelTrack, m_SelClip, out start, out end)) return false;
            color = TimelineView.GetTrackColor(m_SelTrack);
            return true;
        }

        /// <summary>网格吸附（相对起点取整）。UI 用像素网格，3D 用 Unity 单位网格。</summary>
        internal Vector3 SnapPathPosition(Vector3 position)
        {
            if (!m_GridSnap || m_Asset == null || m_Asset.data.points.Count == 0) return position;
            var origin = m_Asset.data.points[0].position;
            return origin + SnapPathVector(position - origin);
        }

        internal Vector3 SnapPathVector(Vector3 v)
        {
            float grid = IsUiTarget ? m_GridSizeUi : m_GridSize3D;
            if (!m_GridSnap || grid <= 0f) return v;
            return new Vector3(Mathf.Round(v.x / grid) * grid, Mathf.Round(v.y / grid) * grid, Mathf.Round(v.z / grid) * grid);
        }

        internal void Select(int point)
        {
            m_Selected = point;
            m_SelTrack = m_SelClip = m_SelEvent = -1;
            SelectionChanged();
        }

        internal void SelectTrack(int track)
        {
            m_Selected = m_SelClip = m_SelEvent = -1;
            m_SelTrack = track;
            SelectionChanged();
        }

        internal void SelectClip(int track, int clip)
        {
            m_Selected = m_SelEvent = -1;
            m_SelTrack = track;
            m_SelClip = clip;
            SelectionChanged();
        }

        internal void SelectEvent(int index)
        {
            m_Selected = m_SelTrack = m_SelClip = -1;
            m_SelEvent = index;
            SelectionChanged();
        }

        internal void ClearSelection() => Select(-1);

        void SelectionChanged()
        {
            GUIUtility.keyboardControl = 0;
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

        /// <summary>增删轨道后调用：预览中要重新记录各轨道的初始值。</summary>
        void TracksChanged()
        {
            if (m_Preview && m_Asset != null) m_PreviewState.CaptureTracks(m_Asset.data);
        }

        /// <summary>拖动时间轴标尺：定位播放头，有预览目标时自动进入预览。</summary>
        internal void Scrub(float time)
        {
            m_Time = time;
            m_Playing = false;
            if (!m_Preview && m_Target != null) StartPreview();
            ApplyPreview();
            Repaint();
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------- 编辑操作（时间轴、检查器、快捷键共用）

        internal void AppendPoint()
        {
            RecordUndo("Add Path Point");
            int index = MotionEditing.AppendPoint(m_Asset.data, IsUiTarget ? 150f : 1.5f);
            MarkChanged();
            Select(index);
        }

        internal void InsertPointAtTime(float time)
        {
            RecordUndo("Insert Path Point");
            int index = MotionEditing.InsertPointAtTime(m_Asset.data, time);
            if (index < 0)
            {
                ShowNotification(new GUIContent("这里不在路径段内部"));
                return;
            }
            MarkChanged();
            Select(index);
        }

        internal void DeletePoint(int index)
        {
            if (m_Asset == null || index <= 0) return;
            RecordUndo("Delete Path Point");
            if (!MotionEditing.RemovePoint(m_Asset.data, index)) return;
            m_Selected = Mathf.Min(index - 1, m_Asset.data.points.Count - 1);
            MarkChanged();
        }

        /// <summary>删除 Scene 视图或窗口里选中的路径点。</summary>
        internal void DeleteSelected() => DeletePoint(m_Selected);

        internal void AddEventAt(float time)
        {
            RecordUndo("Add Motion Event");
            var data = m_Asset.data;
            MotionEditing.AddEvent(data, Evaluator, time, "Event" + (data.events.Count + 1));
            MarkChanged();
            SelectEvent(data.events.Count - 1);
        }

        internal void DuplicateEvent(int index)
        {
            var data = m_Asset.data;
            if (index < 0 || index >= data.events.Count) return;
            RecordUndo("Duplicate Motion Event");
            var copy = MotionEditing.Clone(data.events[index]);
            data.events.Add(copy);
            MotionEditing.SetEventTime(data, Evaluator, copy, Evaluator.GetEventTime(index) + 0.1f);
            MarkChanged();
            SelectEvent(data.events.Count - 1);
        }

        internal void DeleteEvent(int index)
        {
            var data = m_Asset.data;
            if (index < 0 || index >= data.events.Count) return;
            RecordUndo("Delete Motion Event");
            data.events.RemoveAt(index);
            m_SelEvent = -1;
            MarkChanged();
        }

        internal void AddTrack(string typeId)
        {
            if (!MotionTrackRegistry.TryCreate(typeId, out var track)) return;
            RecordUndo("Add Motion Track");
            m_Asset.data.tracks.Add(track);
            TracksChanged();
            MarkChanged();
            SelectTrack(m_Asset.data.tracks.Count - 1);
        }

        internal void DeleteTrack(int index)
        {
            var tracks = m_Asset.data.tracks;
            if (index < 0 || index >= tracks.Count) return;
            RecordUndo("Delete Motion Track");
            tracks.RemoveAt(index);
            m_SelTrack = m_SelClip = -1;
            TracksChanged();
            MarkChanged();
        }

        internal void MoveTrack(int index, int direction)
        {
            var tracks = m_Asset.data.tracks;
            int to = index + direction;
            if (index < 0 || index >= tracks.Count || to < 0 || to >= tracks.Count) return;
            RecordUndo("Reorder Motion Track");
            (tracks[index], tracks[to]) = (tracks[to], tracks[index]);
            MarkChanged();
            SelectTrack(to);
        }

        internal void AddClipAt(int trackIndex, float time)
        {
            var data = m_Asset.data;
            if (trackIndex < 0 || trackIndex >= data.tracks.Count || data.tracks[trackIndex] == null) return;
            RecordUndo("Add Motion Clip");
            var track = data.tracks[trackIndex];
            MotionEditing.AddClip(data, Evaluator, track, time, 0.3f);
            MarkChanged();
            SelectClip(trackIndex, track.clips.Count - 1);
        }

        internal void DuplicateClip(int trackIndex, int clipIndex)
        {
            var data = m_Asset.data;
            var track = trackIndex >= 0 && trackIndex < data.tracks.Count ? data.tracks[trackIndex] : null;
            if (track == null || clipIndex < 0 || clipIndex >= track.clips.Count) return;
            if (!Evaluator.TryGetClipRange(trackIndex, clipIndex, out float start, out _)) return;
            RecordUndo("Duplicate Motion Clip");
            var source = track.clips[clipIndex];
            var copy = MotionEditing.Clone(source);
            track.clips.Add(copy);
            MotionEditing.SetClipStart(data, Evaluator, copy, start + source.duration);
            MarkChanged();
            SelectClip(trackIndex, track.clips.Count - 1);
        }

        internal void DeleteClip(int trackIndex, int clipIndex)
        {
            var data = m_Asset.data;
            var track = trackIndex >= 0 && trackIndex < data.tracks.Count ? data.tracks[trackIndex] : null;
            if (track == null || clipIndex < 0 || clipIndex >= track.clips.Count) return;
            RecordUndo("Delete Motion Clip");
            track.clips.RemoveAt(clipIndex);
            m_SelClip = -1;
            MarkChanged();
        }

        void CopySelection()
        {
            var data = m_Asset.data;
            if (m_SelClip >= 0)
            {
                var track = data.tracks[m_SelTrack];
                s_ClipboardClip = MotionEditing.Clone(track.clips[m_SelClip]);
                s_ClipboardTrackType = track.TypeId;
                s_ClipboardEvent = null;
                ShowNotification(new GUIContent("已复制片段"));
            }
            else if (m_SelEvent >= 0)
            {
                s_ClipboardEvent = MotionEditing.Clone(data.events[m_SelEvent]);
                s_ClipboardClip = null;
                ShowNotification(new GUIContent("已复制事件"));
            }
        }

        /// <summary>
        /// 粘贴到 time。片段优先贴到 trackIndex 这条轨道（类型相同时），否则贴到第一条同类型轨道，没有就新建一条。
        /// </summary>
        internal void PasteAt(int trackIndex, float time)
        {
            var data = m_Asset.data;
            if (s_ClipboardEvent != null)
            {
                RecordUndo("Paste Motion Event");
                var copy = MotionEditing.Clone(s_ClipboardEvent);
                data.events.Add(copy);
                MotionEditing.SetEventTime(data, Evaluator, copy, time);
                MarkChanged();
                SelectEvent(data.events.Count - 1);
                return;
            }
            if (s_ClipboardClip == null) return;

            int target = -1;
            if (trackIndex >= 0 && trackIndex < data.tracks.Count && data.tracks[trackIndex]?.TypeId == s_ClipboardTrackType)
                target = trackIndex;
            for (int i = 0; target < 0 && i < data.tracks.Count; i++)
                if (data.tracks[i]?.TypeId == s_ClipboardTrackType) target = i;

            RecordUndo("Paste Motion Clip");
            if (target < 0)
            {
                if (!MotionTrackRegistry.TryCreate(s_ClipboardTrackType, out var created)) return;
                data.tracks.Add(created);
                target = data.tracks.Count - 1;
                TracksChanged();
            }
            var track = data.tracks[target];
            var clip = MotionEditing.Clone(s_ClipboardClip);
            track.clips.Add(clip);
            MotionEditing.SetClipStart(data, Evaluator, clip, time);
            MarkChanged();
            SelectClip(target, track.clips.Count - 1);
        }

        void DeleteSelection()
        {
            if (m_SelClip >= 0) DeleteClip(m_SelTrack, m_SelClip);
            else if (m_SelEvent >= 0) DeleteEvent(m_SelEvent);
            else if (m_Selected > 0) DeletePoint(m_Selected);
        }

        void DuplicateSelection()
        {
            if (m_SelClip >= 0) DuplicateClip(m_SelTrack, m_SelClip);
            else if (m_SelEvent >= 0) DuplicateEvent(m_SelEvent);
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

        internal void SetAsset(MotionAsset asset)
        {
            if (asset == m_Asset) return;
            StopPreview();
            m_Asset = asset;
            m_Eval = null;
            m_Selected = m_SelTrack = m_SelClip = m_SelEvent = -1;
            m_Time = 0f;
            Repaint();
            SceneView.RepaintAll();
        }

        internal void SetTarget(Transform target)
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

        void TogglePlay()
        {
            if (m_Target == null) return;
            if (m_Playing)
            {
                m_Playing = false;
                return;
            }
            if (!m_Preview) StartPreview();
            if (m_Time >= PreviewLength) m_Time = 0f;
            m_Playing = true;
            m_LastTick = EditorApplication.timeSinceStartup;
        }

        void ApplyPreview()
        {
            if (!m_Preview || !m_PreviewState.IsActive || Evaluator == null) return;
            m_PreviewState.Apply(Evaluator, LocalTime);
        }

        void ClampSelection()
        {
            var data = m_Asset != null ? m_Asset.data : null;
            int points = data != null ? data.points.Count : 0;
            if (m_Selected >= points) m_Selected = points - 1;

            int tracks = data != null ? data.tracks.Count : 0;
            if (m_SelTrack >= tracks || (m_SelTrack >= 0 && data.tracks[m_SelTrack] == null)) m_SelTrack = m_SelClip = -1;
            if (m_SelClip >= 0 && (m_SelTrack < 0 || m_SelClip >= data.tracks[m_SelTrack].clips.Count)) m_SelClip = -1;

            int events = data != null ? data.events.Count : 0;
            if (m_SelEvent >= events) m_SelEvent = -1;
        }

        // ---------------------------------------------------------------- 界面

        void OnGUI()
        {
            DrawHeader();
            if (m_Asset == null)
            {
                EditorGUILayout.HelpBox("选择一个 Motion Asset，或点“新建”。也可以在 Project 窗口双击 Motion Asset 打开。", MessageType.Info);
                if (GUILayout.Button("打开测试场景（自带示例动效）")) TestSceneBuilder.OpenTestScene();
                return;
            }

            ClampSelection();
            HandleShortcuts();
            DrawToolbar();
            m_Timeline.Draw(this);
            EditorGUILayout.LabelField("双击空白处添加，右键更多操作，拖动片段两端改时长；空格播放/暂停。", EditorStyles.wordWrappedMiniLabel);

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            DrawInspector();
            DrawMotionSettings();
            DrawWarnings();
            EditorGUILayout.EndScrollView();
            DrawFooter();
        }

        /// <summary>窗口获得焦点时的快捷键：Delete 删除、Ctrl/⌘+D 复制一份、Ctrl/⌘+C/V 复制粘贴、空格播放。</summary>
        void HandleShortcuts()
        {
            var e = Event.current;
            if (EditorGUIUtility.editingTextField) return;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space)
            {
                TogglePlay();
                e.Use();
                return;
            }
            if (e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand) return;

            bool execute = e.type == EventType.ExecuteCommand;
            bool hasClipOrEvent = m_SelClip >= 0 || m_SelEvent >= 0;
            switch (e.commandName)
            {
                case "SoftDelete":
                case "Delete":
                    if (!hasClipOrEvent && m_Selected <= 0) return;
                    if (execute) DeleteSelection();
                    break;
                case "Duplicate":
                    if (!hasClipOrEvent) return;
                    if (execute) DuplicateSelection();
                    break;
                case "Copy":
                    if (!hasClipOrEvent) return;
                    if (execute) CopySelection();
                    break;
                case "Paste":
                    if (!HasClipboard) return;
                    if (execute) PasteAt(m_SelTrack, LocalTime);
                    break;
                default:
                    return;
            }
            e.Use();
            // 执行后选中项或数据可能变了，跳过本次剩余的绘制，避免和 Layout 对不上。
            if (execute) GUIUtility.ExitGUI();
        }

        void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var asset = (MotionAsset)EditorGUILayout.ObjectField("Motion Asset", m_Asset, typeof(MotionAsset), false);
                if (asset != m_Asset)
                {
                    SetAsset(asset);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("新建", GUILayout.Width(48)))
                {
                    CreateAsset();
                    GUIUtility.ExitGUI();
                }
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

        void DrawToolbar()
        {
            EditorGUILayout.Space(2);
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
                    if (GUILayout.Button(m_Playing ? "暂停" : "播放", EditorStyles.miniButtonMid)) TogglePlay();
                }

                if (GUILayout.Button("停止", EditorStyles.miniButtonRight))
                {
                    m_Time = 0f;
                    StopPreview();
                    SceneView.RepaintAll();
                }
                m_LoopPreview = GUILayout.Toggle(m_LoopPreview, new GUIContent("循环预览", "预览一直循环播放（与动效本身的循环设置无关）"), GUILayout.Width(72));
                GUILayout.FlexibleSpace();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                m_SnapTimeline = GUILayout.Toggle(m_SnapTimeline,
                    new GUIContent("时间轴吸附", "拖动片段、事件、段分界时吸附到路径点、事件、其他片段和播放头。按住 Ctrl/⌘ 临时关闭。"),
                    EditorStyles.miniButtonLeft, GUILayout.Width(72));
                bool grid = GUILayout.Toggle(m_GridSnap,
                    new GUIContent("网格吸附", "Scene 视图里拖动路径点和切线时，相对起点按网格取整。"),
                    EditorStyles.miniButtonRight, GUILayout.Width(64));
                if (grid != m_GridSnap) { m_GridSnap = grid; SceneView.RepaintAll(); }

                using (new EditorGUI.DisabledScope(!m_GridSnap))
                {
                    GUILayout.Label("网格", EditorStyles.miniLabel, GUILayout.Width(26));
                    if (IsUiTarget) m_GridSizeUi = Mathf.Max(0.01f, EditorGUILayout.FloatField(m_GridSizeUi, GUILayout.Width(44)));
                    else m_GridSize3D = Mathf.Max(0.001f, EditorGUILayout.FloatField(m_GridSize3D, GUILayout.Width(44)));
                    GUILayout.Label(IsUiTarget ? "px" : "单位", EditorStyles.miniLabel, GUILayout.Width(28));
                }
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.Space(2);
        }

        void DrawInspector()
        {
            var data = m_Asset.data;
            if (m_SelClip >= 0) DrawClipInspector(data);
            else if (m_SelTrack >= 0) DrawTrackInspector(data);
            else if (m_SelEvent >= 0) DrawEventInspector(data);
            else if (m_Selected >= 0 && m_Selected < data.points.Count) DrawSelectedPoint();
            else
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("在时间轴上点选路径段、事件或片段来编辑；Scene 视图里点击路径点选点。", MessageType.None);
            }
        }

        // ---------------------------------------------------------------- 路径点与路径段

        void DrawSelectedPoint()
        {
            var data = m_Asset.data;
            var eval = Evaluator;
            var p = data.points[m_Selected];

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"路径点 {m_Selected}（到达 {eval.GetPointTime(m_Selected):0.###}s）", EditorStyles.boldLabel);

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

            if (m_Selected > 0 && GUILayout.Button("删除这个点")) { DeletePoint(m_Selected); GUIUtility.ExitGUI(); }

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

        // ---------------------------------------------------------------- 轨道、片段、事件

        static string TrackTitle(MotionTrack track)
        {
            string name = string.IsNullOrEmpty(track.name) ? track.DisplayName : track.name;
            return name == track.DisplayName ? name : $"{name}（{track.DisplayName}）";
        }

        static string ModeHint(MotionTrack track)
        {
            switch (track.Mode)
            {
                case ValueMode.Multiply: return "值 = 初始值 × 倍率";
                case ValueMode.Add: return "值 = 初始值 + 增量";
                default: return "值 = 填写的绝对值";
            }
        }

        void DrawTrackInspector(MotionData data)
        {
            var track = data.tracks[m_SelTrack];
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"轨道 · {TrackTitle(track)}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("名称", track.name ?? "");
            bool enabled = EditorGUILayout.Toggle("启用", track.enabled);
            if (EditorGUI.EndChangeCheck())
            {
                RecordUndo("Edit Motion Track");
                track.name = name;
                track.enabled = enabled;
                MarkChanged();
            }
            EditorGUILayout.LabelField("类型", $"{track.DisplayName}（{track.TypeId}），{ModeHint(track)}");
            EditorGUILayout.LabelField("片段数", track.clips.Count.ToString());

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("在播放头处加片段")) { AddClipAt(m_SelTrack, LocalTime); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("上移", GUILayout.Width(44))) { MoveTrack(m_SelTrack, -1); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("下移", GUILayout.Width(44))) { MoveTrack(m_SelTrack, 1); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("删除轨道", GUILayout.Width(64))) { DeleteTrack(m_SelTrack); GUIUtility.ExitGUI(); }
            }
        }

        void DrawClipInspector(MotionData data)
        {
            var eval = Evaluator;
            var track = data.tracks[m_SelTrack];
            var clip = track.clips[m_SelClip];
            if (clip.ease == null) clip.ease = new EaseSpec();
            eval.TryGetClipRange(m_SelTrack, m_SelClip, out float start, out float end);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"片段 · {TrackTitle(track)}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            string anchor = AnchorPopup(clip.anchor, data, eval);
            float offset = EditorGUILayout.FloatField(new GUIContent("偏移（秒）", "开始时间 = 锚点到达时间 + 偏移。改段时长时片段跟着锚点走。"), clip.offset);
            float duration = Mathf.Max(0f, EditorGUILayout.FloatField("时长（秒）", clip.duration));
            var easeType = (EaseType)EditorGUILayout.EnumPopup("缓动", clip.ease.type);
            AnimationCurve curve = clip.ease.curve;
            if (easeType == EaseType.Custom)
                curve = EditorGUILayout.CurveField("自定义曲线", curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var value = ClipValueField(track, clip.value);
            string desc = EditorGUILayout.TextField(new GUIContent("描述", "写给 AI 看的一句话，例如“落地压扁”"), clip.desc ?? "");
            if (EditorGUI.EndChangeCheck())
            {
                RecordUndo("Edit Motion Clip");
                if (anchor != clip.anchor) offset = MotionEditing.OffsetForAnchor(data, eval, start, anchor);
                clip.anchor = anchor;
                clip.offset = offset;
                clip.duration = duration;
                clip.ease.type = easeType;
                clip.ease.curve = easeType == EaseType.Custom ? curve : clip.ease.curve;
                clip.value = value;
                clip.desc = desc;
                MarkChanged();
            }

            EditorGUILayout.LabelField(ModeHint(track), EditorStyles.miniLabel);
            bool truncated = end < start + clip.duration - 1e-4f;
            EditorGUILayout.LabelField("生效时间", truncated
                ? $"{start:0.###}s → {end:0.###}s（原到 {start + clip.duration:0.###}s，后面被同轨道的片段覆盖）"
                : $"{start:0.###}s → {end:0.###}s");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制一份")) { DuplicateClip(m_SelTrack, m_SelClip); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("删除片段")) { DeleteClip(m_SelTrack, m_SelClip); GUIUtility.ExitGUI(); }
            }
        }

        static Vector4 ClipValueField(MotionTrack track, Vector4 v)
        {
            if (track is ColorTrack)
            {
                var c = EditorGUILayout.ColorField(new GUIContent("颜色"), new Color(v.x, v.y, v.z, 1f), true, false, false);
                return new Vector4(c.r, c.g, c.b, v.w);
            }
            if (track is AlphaTrack)
            {
                v.x = EditorGUILayout.Slider("透明度", v.x, 0f, 1f);
                return v;
            }

            string label = track is ScaleTrack ? "缩放倍率" : track is RotationTrack ? "旋转增量（度）"
                : track.Mode == ValueMode.Multiply ? "倍率" : track.Mode == ValueMode.Add ? "增量" : "目标值";
            switch (Mathf.Clamp(track.Dimension, 1, 4))
            {
                case 1:
                    v.x = EditorGUILayout.FloatField(label, v.x);
                    return v;
                case 2:
                    var v2 = EditorGUILayout.Vector2Field(label, new Vector2(v.x, v.y));
                    return new Vector4(v2.x, v2.y, v.z, v.w);
                case 3:
                    var v3 = EditorGUILayout.Vector3Field(label, new Vector3(v.x, v.y, v.z));
                    return new Vector4(v3.x, v3.y, v3.z, v.w);
                default:
                    return EditorGUILayout.Vector4Field(label, v);
            }
        }

        void DrawEventInspector(MotionData data)
        {
            var eval = Evaluator;
            var motionEvent = data.events[m_SelEvent];
            float time = eval.GetEventTime(m_SelEvent);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"事件（{time:0.###}s）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("导出时先生成一条日志占位，接入正式项目后替换成实际回调。", EditorStyles.wordWrappedMiniLabel);

            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("名称", motionEvent.name ?? "");
            string message = EditorGUILayout.TextField(new GUIContent("日志内容", "占位日志里输出的文字"), motionEvent.message ?? "");
            string anchor = AnchorPopup(motionEvent.anchor, data, eval);
            float offset = EditorGUILayout.FloatField(new GUIContent("偏移（秒）", "触发时间 = 锚点到达时间 + 偏移"), motionEvent.offset);
            if (EditorGUI.EndChangeCheck())
            {
                RecordUndo("Edit Motion Event");
                if (anchor != motionEvent.anchor) offset = MotionEditing.OffsetForAnchor(data, eval, time, anchor);
                motionEvent.name = name;
                motionEvent.message = message;
                motionEvent.anchor = anchor;
                motionEvent.offset = offset;
                MarkChanged();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制一份")) { DuplicateEvent(m_SelEvent); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("删除事件")) { DeleteEvent(m_SelEvent); GUIUtility.ExitGUI(); }
            }
        }

        /// <summary>锚点下拉框。改锚点时调用方负责换算偏移，保持绝对时间不变。</summary>
        static string AnchorPopup(string anchor, MotionData data, MotionEvaluator eval)
        {
            if (data.points.Count == 0) return anchor;
            int index = data.FindPointIndex(anchor);
            var options = new string[data.points.Count];
            for (int i = 0; i < options.Length; i++)
                options[i] = $"点 {i}（{eval.GetPointTime(i):0.###}s）";
            int selected = EditorGUILayout.Popup(
                new GUIContent("锚点", index < 0 ? "原锚点已不存在，暂按起点计算" : "跟随哪个路径点的到达时间"),
                Mathf.Max(0, index), options);
            return selected == index ? anchor : data.points[selected].id;
        }

        // ---------------------------------------------------------------- 动效设置

        void DrawMotionSettings()
        {
            var data = m_Asset.data;
            EditorGUILayout.Space();
            m_ShowSettings = EditorGUILayout.Foldout(m_ShowSettings, "动效设置（名称、描述、循环）", true);
            if (!m_ShowSettings) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField("名称", data.name ?? "");
                EditorGUILayout.LabelField(new GUIContent("描述", "整段动效写给 AI 看的说明"));
                string description = EditorGUILayout.TextArea(data.description ?? "", GUILayout.MinHeight(36));

                bool infinite = data.loop.count < 0;
                int count = Mathf.Max(1, data.loop.count);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(infinite))
                        count = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("播放次数", "1 = 不循环"), count));
                    infinite = EditorGUILayout.ToggleLeft("无限", infinite, GUILayout.Width(52));
                }
                MotionLoopType loopType;
                using (new EditorGUI.DisabledScope(!infinite && count == 1))
                    loopType = (MotionLoopType)EditorGUILayout.EnumPopup(new GUIContent("循环方式", "Restart 每次从头播 / Yoyo 来回播"), data.loop.type);

                if (EditorGUI.EndChangeCheck())
                {
                    RecordUndo("Edit Motion Settings");
                    data.name = name;
                    data.description = description;
                    data.loop.count = infinite ? -1 : count;
                    data.loop.type = loopType;
                    MarkChanged();
                }

                var eval = Evaluator;
                EditorGUILayout.LabelField("时长", $"单次 {eval.Duration:0.###}s，路径 {eval.PathDuration:0.###}s" +
                    (data.loop.count == 1 ? "" : data.loop.count < 0 ? "，无限循环" : $"，共 {data.loop.count} 次 {eval.TotalDuration:0.###}s"));
            }
        }

        void DrawWarnings()
        {
            var eval = Evaluator;
            var warnings = new List<string>(eval.Warnings);
            if (m_Preview)
                foreach (var name in m_PreviewState.UnsupportedTracks)
                    warnings.Add($"预览目标上没有轨道 {name} 需要的组件，该轨道不预览");
            for (int i = 0; i < m_Asset.data.tracks.Count; i++)
                if (m_Asset.data.tracks[i] == null)
                    warnings.Add($"第 {i + 1} 条轨道的类型找不到（脚本被删或改名），可以在时间轴右键删除它");
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
            m_Selected = m_SelTrack = m_SelClip = m_SelEvent = -1;
            MarkChanged();
            if (warnings.Count > 0) EditorUtility.DisplayDialog("导入完成，但有警告", string.Join("\n", warnings), "确定");
        }
    }
}
