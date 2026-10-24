using System;
using System.Collections.Generic;
using System.IO;
using Limbo.CurveMotion.Serialization;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
#if !UNITY_2021_2_OR_NEWER
using UnityEditor.Experimental.SceneManagement;
#endif

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// 曲线运动编辑器主窗口：选择资产、预览目标与落点对象、时间轴（路径段、事件、属性轨道）、选中项的参数、
    /// 在场景里预览、导入导出 JSON。Scene 视图中的绘制与手柄见 <see cref="PathSceneEditor"/>，
    /// 时间轴的绘制与拖拽见 <see cref="TimelineView"/>。
    /// </summary>
    public class CurveMotionWindow : EditorWindow
    {
        [SerializeField] MotionAsset m_Asset;
        [SerializeField] Transform m_Target;
        [SerializeField] Transform m_EndTarget;
        [SerializeField] Camera m_WorldCamera;
        [SerializeField] bool m_EditPath = true;
        // 预览开关：开着时 Scene 视图显示路径，并把动效作用在预览目标上；关掉隐藏路径、对象还原。
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
        [SerializeField] Vector2 m_ProblemScroll;

        bool m_Playing;
        bool m_EndSyncFailed;
        bool m_RestoringContext;
        MotionAsset m_PendingContext; // 请求了回到上次的场景/预制体但还没打开（例如预制体模式下一帧才就绪）
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
        internal Transform EndObject => m_EndTarget;
        internal bool EditPath => m_EditPath;
        internal int Selected => m_Selected;
        internal int SelectedTrack => m_SelTrack;
        internal int SelectedClip => m_SelClip;
        internal int SelectedEvent => m_SelEvent;
        internal bool SnapTimeline => m_SnapTimeline;
        internal bool IsUiTarget => m_Asset != null && m_Asset.data.target == TargetKind.RectTransform;
        internal bool HasClipboard => s_ClipboardClip != null || s_ClipboardEvent != null;

        /// <summary>终点正跟随落点对象：终点位置由落点对象决定，不能手动改。</summary>
        internal bool IsEndLocked => m_Asset != null && m_Target != null && m_EndTarget != null && EndBinding.bound
            && m_Asset.data.points.Count >= 2 && !m_EndTarget.IsChildOf(m_Target);

        /// <summary>预览目标与落点对象一个是 UI、一个是 3D，终点按屏幕位置对准。</summary>
        internal bool IsCrossSpace => MixedSpace.IsCrossSpace(m_Target, m_EndTarget);

        /// <summary>
        /// 预览目标在当前打开的场景/预制体里，才能预览、在 Scene 视图显示路径。
        /// 例如目标在场景里、却进了某个预制体，这时路径画在预制体模式里没有意义。
        /// </summary>
        bool CanPreview => m_Asset != null && m_Target != null && InCurrentStage(m_Target);

        static bool InCurrentStage(Transform target) =>
            StageUtility.GetStageHandle(target.gameObject) == StageUtility.GetCurrentStageHandle();

        /// <summary>Scene 视图里画不画路径：预览中，或 Play 模式里正在试播导出代码。</summary>
        bool ShowPathInScene => CanPreview && (m_PreviewState.IsActive || DotweenExportActions.TryGetRunStart(m_Target, out _));

        internal EndTarget EndBinding => m_Asset.data.endTarget ?? (m_Asset.data.endTarget = new EndTarget());

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
                return m_Target == null ? Matrix4x4.identity : MotionPreview.GetPathMatrix(m_Target, StartLocalPosition) * Matrix4x4.Translate(-origin);
            }
        }

        /// <summary>预览目标的起始 localPosition：预览或 Play 模式试播中是开始时记下的值，否则是当前值。</summary>
        Vector3 StartLocalPosition => DotweenExportActions.TryGetRunStart(m_Target, out var runStart)
            ? runStart
            : m_PreviewState.GetStartLocalPosition(m_Target);

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

        /// <summary>拖动时间轴标尺：定位播放头，能预览时自动打开预览。</summary>
        internal void Scrub(float time)
        {
            m_Time = time;
            m_Playing = false;
            if (!m_Preview) StartPreview();
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
            AssemblyReloadEvents.beforeAssemblyReload += SuspendPreview;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            PrefabStage.prefabSaving += OnPrefabSaving;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
            // 域重载前已经还原了对象（SuspendPreview），预览开关保留，SyncPreviewState 在下一帧重新开始预览。
            m_Playing = false;
        }

        void OnDisable()
        {
            // 域重载也会走这里：只还原对象，不关预览开关，重载后接着预览。
            m_Playing = false;
            SuspendPreview();
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= SuspendPreview;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            PrefabStage.prefabSaving -= OnPrefabSaving;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
        }

        void OnSceneGUI(SceneView view)
        {
            if (m_Asset == null || !ShowPathInScene) return;
            PathSceneEditor.Draw(this);
        }

        void OnUndoRedo()
        {
            // 撤销“设置落点对象”时窗口状态也会回退，预览要与之对齐。
            if (m_Preview && (!m_PreviewState.IsActive || m_PreviewState.Target != m_Target)) StopPreview();
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

        // 保存场景前还原对象，避免把预览中的位置、缩放等存进场景。预览开关不动，下一帧接着预览。
        void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => SuspendPreview();

        // 预制体模式里同理；退出预制体模式时预览目标会被销毁，先还原。
        void OnPrefabSaving(GameObject root)
        {
            if (m_PreviewState.IsActive && root != null && m_PreviewState.Target.IsChildOf(root.transform)) SuspendPreview();
        }

        void OnPrefabStageClosing(PrefabStage stage)
        {
            if (m_PreviewState.IsActive && stage != null && stage.IsPartOfPrefabContents(m_PreviewState.Target.gameObject)) StopPreview();
        }

        void OnEditorUpdate()
        {
            SyncPreviewState();
            SyncEndToTarget();
            if (!m_Playing || m_Asset == null) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - m_LastTick);
            m_LastTick = now;
            m_Time += dt;
            float end = PreviewLength;
            if (m_Time >= end)
            {
                // 无限循环在循环部分里来回绕（尾段自循环不再重播前面），有限次数的从头重播。
                float from = float.IsInfinity(Evaluator.TotalDuration) ? Evaluator.LoopStart : 0f;
                if (m_LoopPreview && end - from > 0f) m_Time = from + (m_Time - from) % (end - from);
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
                return eval.LoopStart + eval.LoopLength * (m_Asset.data.loop.type == MotionLoopType.Yoyo ? 2f : 1f);
            }
        }

        /// <summary>
        /// 切换资产，并按资产里记的上次编辑位置填回预览目标、落点对象和相机。
        /// 那个场景/预制体没打开时，开着“自动”就回到那里（下一帧打开，有未保存修改会先问）。再次打开同一个资产也会重试。
        /// </summary>
        internal void SetAsset(MotionAsset asset)
        {
            if (asset != m_Asset)
            {
                m_PendingContext = null;
                StopPreview();
                m_Asset = asset;
                m_Eval = null;
                m_Selected = m_SelTrack = m_SelClip = m_SelEvent = -1;
                m_Time = 0f;
                // 落点对象和相机跟着资产走；预览目标没记录时保留当前的。
                m_EndTarget = null;
                m_WorldCamera = null;
            }
            RestoreContext(EditContextStore.AutoOpen);
            SyncEndToTarget();
            Repaint();
            SceneView.RepaintAll();
        }

        MotionEditContext EditContext => m_Asset != null && m_Asset.editContext != null && !m_Asset.editContext.IsEmpty ? m_Asset.editContext : null;

        void RestoreContext(bool allowOpen)
        {
            var ctx = EditContext;
            if (ctx == null) return;
            if (EditContextStore.IsOpen(ctx.Main))
            {
                ApplyContext(ctx);
                return;
            }
            if (!allowOpen || EditorApplication.isPlayingOrWillChangePlaymode || string.IsNullOrEmpty(EditContextStore.ContainerPath(ctx.Main))) return;
            // 在 OnGUI 或打开资产的回调里切场景不安全，放到下一帧。
            var asset = m_Asset;
            EditorApplication.delayCall += () =>
            {
                if (this != null && m_Asset == asset) OpenContext();
            };
        }

        /// <summary>填回记录里能找到的对象。onlyMissing：只补窗口里空着的（撤销删除、对象重新出现时）。填回不算修改，不写资产。</summary>
        void ApplyContext(MotionEditContext ctx, bool onlyMissing = false)
        {
            var mover = onlyMissing && m_Target != null ? null : EditContextStore.Resolve(ctx.mover);
            var end = onlyMissing && m_EndTarget != null ? null : EditContextStore.Resolve(ctx.endTarget);
            var cameraTransform = onlyMissing && m_WorldCamera != null ? null : EditContextStore.Resolve(ctx.camera);
            m_RestoringContext = true;
            try
            {
                if (end != null) m_EndTarget = end;
                if (cameraTransform != null) m_WorldCamera = cameraTransform.GetComponent<Camera>();
                if (mover != null) SetTarget(mover);
            }
            finally
            {
                m_RestoringContext = false;
            }
        }

        void FinishOpenContext(MotionEditContext ctx)
        {
            m_PendingContext = null;
            ApplyContext(ctx);
            SyncEndToTarget();
            if (m_Target != null) Selection.activeTransform = m_Target;
            Repaint();
            SceneView.RepaintAll();
        }

        // 记录里的对象被删后又撤销回来、或换了场景后出现时，自动补回窗口；请求过回到上次的地方、那里刚打开时，整套填回。
        void OnHierarchyChanged()
        {
            var ctx = EditContext;
            if (ctx == null || !EditContextStore.IsOpen(ctx.Main)) return;
            if (m_PendingContext != null && m_PendingContext == m_Asset)
            {
                FinishOpenContext(ctx);
                return;
            }
            bool missing = (m_Target == null && !ctx.mover.IsEmpty) || (m_EndTarget == null && !ctx.endTarget.IsEmpty)
                || (m_WorldCamera == null && !ctx.camera.IsEmpty);
            if (!missing) return;
            var before = (m_Target, m_EndTarget, m_WorldCamera);
            ApplyContext(ctx, onlyMissing: true);
            if (before == (m_Target, m_EndTarget, m_WorldCamera)) return;
            SyncEndToTarget();
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>回到上次编辑的场景/预制体，并填回预览目标、落点对象和相机。</summary>
        void OpenContext()
        {
            var ctx = EditContext;
            if (ctx == null) return;
            StopPreview();
            m_PendingContext = null;
            if (EditContextStore.Open(ctx.Main)) FinishOpenContext(ctx);
            else m_PendingContext = m_Asset;
            Repaint();
            SceneView.RepaintAll();
        }

        internal void SetTarget(Transform target)
        {
            if (target == m_Target) return;
            bool wasPreviewing = m_Preview;
            StopPreview();
            m_Target = target;
            // 先记录再记 Undo：撤销类型变化时不把“上次编辑”也退回去（窗口里的预览目标不撤销）。
            SaveMover();
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
            SyncEndToTarget();
            if (wasPreviewing) StartPreview();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 指定落点对象：动效改为终点绑定，路径终点自动对准它。可以是 UI 也可以是 3D 物体，类型不同时经屏幕坐标换算。
        /// 传 null 只清掉预览用的对象，动效仍保留终点绑定（运行时由代码传入落点）。
        /// </summary>
        internal void SetEndTarget(Transform end)
        {
            if (end == m_EndTarget) return;
            if (end != null && m_Target != null && end.IsChildOf(m_Target))
            {
                ShowNotification(new GUIContent("落点对象不能是预览目标本身或它的子节点"));
                return;
            }
            // 窗口也记进 Undo，撤销时落点对象和绑定一起回去。
            if (m_Asset != null) Undo.RecordObjects(new UnityEngine.Object[] { m_Asset, this }, "Set End Target");
            m_EndTarget = end;
            if (end != null && m_Asset != null)
            {
                var binding = EndBinding;
                var kind = MixedSpace.IsUi(end) ? TargetKind.RectTransform : TargetKind.Transform;
                if (!binding.bound || binding.kind != kind || string.IsNullOrEmpty(binding.hint))
                {
                    binding.bound = true;
                    binding.kind = kind;
                    if (string.IsNullOrEmpty(binding.hint)) binding.hint = end.name;
                    MarkChanged();
                }
            }
            SaveContext(EditContextStore.Slots.End);
            SyncEndToTarget();
            Repaint();
            SceneView.RepaintAll();
        }

        void SetWorldCamera(Camera camera)
        {
            if (camera == m_WorldCamera) return;
            m_WorldCamera = camera;
            SaveContext(EditContextStore.Slots.Camera);
            SyncEndToTarget();
        }

        /// <summary>把用户刚改的那一项（连同所在的场景/预制体）记进资产，有变化才写。填回记录时不写。</summary>
        void SaveContext(EditContextStore.Slots slots)
        {
            if (m_Asset == null || m_RestoringContext) return;
            m_PendingContext = null; // 用户已经在别处指定了对象，之后打开那个场景也不再覆盖
            EditContextStore.Save(m_Asset, slots, m_Target, m_EndTarget, m_WorldCamera);
        }

        /// <summary>记预览目标。换到了别的场景/预制体时，落点对象和相机的旧记录属于原来的地方，按窗口当前的值一起更新。</summary>
        void SaveMover()
        {
            if (m_Asset == null || m_RestoringContext) return;
            var slots = EditContextStore.Slots.Mover;
            var main = EditContext?.Main;
            var fresh = EditContextStore.Capture(m_Target);
            if (main != null && !fresh.IsEmpty && (fresh.container != main.container || fresh.prefab != main.prefab))
                slots = EditContextStore.Slots.All;
            SaveContext(slots);
        }

        /// <summary>
        /// 终点跟随落点对象：每帧把最后一个路径点放到落点对象的位置（相对起点），所见即所得。
        /// 这是编辑器里的做法；运行时和导出代码不改路径，而是按路径进度把终点偏差分摊到整条路径上（结果一样）。
        /// 这里的改动不记 Undo：它由落点位置推导出来，撤销后下一帧会重新对准。
        /// </summary>
        void SyncEndToTarget()
        {
            if (!IsEndLocked)
            {
                m_EndSyncFailed = false;
                return;
            }
            var startLocal = StartLocalPosition;
            var camera = MixedSpace.ResolveWorldCamera(m_WorldCamera);
            bool ok = MixedSpace.TryGetEndOffset(m_Target, startLocal, m_EndTarget, camera, out var offset);
            if (ok != !m_EndSyncFailed)
            {
                m_EndSyncFailed = !ok;
                Repaint();
            }
            if (!ok) return;
            if (!MotionEditing.SetEndOffset(m_Asset.data, offset, IsUiTarget ? 0.01f : 1e-4f)) return;
            EditorUtility.SetDirty(m_Asset);
            Evaluator?.Rebuild();
            ApplyPreview();
            Repaint();
            SceneView.RepaintAll();
        }

        void StartPreview()
        {
            if (!CanPreview) return;
            DotweenExportActions.StopRun();
            m_PreviewState.Begin(m_Target, m_Asset.data);
            m_Preview = true;
            SaveMover();
            ApplyPreview();
        }

        void StopPreview()
        {
            m_Playing = false;
            bool wasActive = m_PreviewState.IsActive;
            m_Preview = false;
            m_PreviewState.End();
            if (wasActive) RepaintGameView();
            SceneView.RepaintAll();
        }

        /// <summary>只还原对象、不关预览开关（保存前用），<see cref="SyncPreviewState"/> 下一帧接着预览。</summary>
        void SuspendPreview()
        {
            if (!m_PreviewState.IsActive) return;
            m_PreviewState.End();
            RepaintGameView();
        }

        /// <summary>
        /// 让对象上的预览跟着开关和“能不能预览”走：进了别的场景/预制体先还原对象、暂停播放，
        /// 回来后（或保存完）在当前播放头位置接着预览。
        /// </summary>
        void SyncPreviewState()
        {
            bool want = m_Preview && CanPreview;
            bool changed = false;
            if (m_PreviewState.IsActive && (!want || m_PreviewState.Target != m_Target))
            {
                m_Playing = false;
                m_PreviewState.End();
                RepaintGameView();
                changed = true;
            }
            if (want && !m_PreviewState.IsActive)
            {
                m_PreviewState.Begin(m_Target, m_Asset.data);
                ApplyPreview();
                changed = true;
            }
            if (!changed) return;
            Repaint();
            SceneView.RepaintAll();
        }

        void TogglePlay()
        {
            if (!CanPreview) return;
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
            RepaintGameView();
        }

        static readonly Type s_GameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");

        /// <summary>非播放模式下改了对象，Game 视图不会自己刷新；UI 和 3D 叠在一起的效果要在 Game 视图里看。</summary>
        static void RepaintGameView()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (s_GameViewType == null) return;
            foreach (var view in Resources.FindObjectsOfTypeAll(s_GameViewType))
                (view as EditorWindow)?.Repaint();
        }

        static void OpenGameView()
        {
            if (EditorApplication.ExecuteMenuItem("Window/General/Game")) return;
            if (s_GameViewType != null) GetWindow(s_GameViewType).Show();
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
            WindowSection.Begin("对象", "动效资产、在谁身上预览、飞到哪里", WindowSection.ObjectAccent);
            DrawHeader();
            if (m_Asset == null)
            {
                EditorGUILayout.HelpBox("选择一个 Motion Asset，或点“新建”。也可以在 Project 窗口双击 Motion Asset 打开。", MessageType.Info);
                if (GUILayout.Button("打开测试场景（自带示例动效）")) TestSceneBuilder.OpenTestScene();
                WindowSection.End();
                return;
            }
            WindowSection.End();

            ClampSelection();
            HandleShortcuts();

            WindowSection.Begin("时间轴", "双击空白处添加，右键更多操作，空格播放/暂停", WindowSection.TimelineAccent);
            DrawToolbar();
            m_Timeline.Draw(this);
            WindowSection.End();

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            DrawSingleEdit();
            DrawGlobalEdit();
            GUILayout.Space(4f);
            EditorGUILayout.EndScrollView();

            DrawProblems();
            DrawFooter();
            GUILayout.Space(4f);
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
            DrawContextRow();

            using (new EditorGUILayout.HorizontalScope())
            {
                var target = (Transform)EditorGUILayout.ObjectField(
                    new GUIContent("预览目标", "场景里的对象。打开“预览”后，路径从它的当前位置开始画在 Scene 视图里，动效在它身上播放，关掉预览自动还原。"),
                    m_Target, typeof(Transform), true);
                // 换对象会改变下面显示哪些行（相机、提示），跳过本次剩余的绘制。
                if (target != m_Target) { SetTarget(target); GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
                {
                    if (GUILayout.Button("用选中", GUILayout.Width(48))) { SetTarget(Selection.activeTransform); GUIUtility.ExitGUI(); }
                }
            }

            if (m_Asset == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                var end = (Transform)EditorGUILayout.ObjectField(
                    new GUIContent("落点对象", "可选。指定后路径终点自动对准它，例如 3D 物体飞到 UI 图标上、UI 图片飞到 3D 模型上。" +
                        "UI 与 3D 之间按屏幕位置对准，效果在 Game 视图里看。"),
                    m_EndTarget, typeof(Transform), true);
                if (end != m_EndTarget) { SetEndTarget(end); GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
                {
                    if (GUILayout.Button("用选中", GUILayout.Width(48))) { SetEndTarget(Selection.activeTransform); GUIUtility.ExitGUI(); }
                }
            }

            if (IsCrossSpace)
            {
                var camera = (Camera)EditorGUILayout.ObjectField(
                    new GUIContent("3D 相机", "UI 与 3D 换算屏幕位置用的相机。留空自动用 Main Camera。"),
                    m_WorldCamera, typeof(Camera), true);
                if (camera != m_WorldCamera) SetWorldCamera(camera);
            }

            EditorGUILayout.LabelField("对象类型",
                m_Asset.data.target == TargetKind.RectTransform ? "UI（anchoredPosition，单位像素）" : "Transform（localPosition，单位 Unity 单位）");
        }

        /// <summary>“上次编辑”一行：资产里记着的场景/预制体，没打开时可一键回去；“自动”控制打开资产时是否自动回去。</summary>
        void DrawContextRow()
        {
            var ctx = EditContext;
            if (ctx == null) return;
            var main = ctx.Main;
            string path = EditContextStore.ContainerPath(main);
            bool missing = string.IsNullOrEmpty(path);
            bool open = !missing && EditContextStore.IsOpen(main);
            string text = missing ? "场景/预制体已不存在"
                : (main.prefab ? "预制体 " : "场景 ") + Path.GetFileNameWithoutExtension(path) + (open ? "（已打开）" : "");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent("上次编辑", "资产里记着上次在哪个场景/预制体里编辑，以及预览目标、落点对象和相机。打开或拖入资产时据此填回。"),
                    new GUIContent(text, missing ? "" : path), EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(missing || open))
                {
                    if (GUILayout.Button(new GUIContent("打开", "打开上次编辑的场景/预制体，并填回预览目标、落点对象和相机"), EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        OpenContext();
                        GUIUtility.ExitGUI();
                    }
                }
                bool auto = GUILayout.Toggle(EditContextStore.AutoOpen,
                    new GUIContent("自动", "打开或拖入资产时，自动回到上次编辑的场景/预制体（本机设置）"), GUILayout.Width(44));
                if (auto != EditContextStore.AutoOpen) EditContextStore.AutoOpen = auto;
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool edit = GUILayout.Toggle(m_EditPath, new GUIContent("编辑路径", "预览时在 Scene 视图显示路径点和切线手柄，可拖动编辑。打开时如果没在预览，会顺带打开预览。"), EditorStyles.miniButtonLeft);
                if (edit != m_EditPath)
                {
                    m_EditPath = edit;
                    if (edit && !m_Preview) StartPreview();
                    SceneView.RepaintAll();
                }

                string previewTip = m_Target == null ? "先指定预览目标"
                    : !CanPreview ? "预览目标不在当前打开的场景/预制体里。回到那里后会接着预览。"
                    : "打开：Scene 视图显示路径，动效按播放头作用在预览目标上（0 秒时就是对象原样，拖播放头或点播放才看得到变化）。关掉：隐藏路径，对象还原。";
                using (new EditorGUI.DisabledScope(!CanPreview))
                {
                    bool preview = GUILayout.Toggle(m_Preview, new GUIContent("预览", previewTip), EditorStyles.miniButtonMid);
                    if (preview != m_Preview)
                    {
                        if (preview) StartPreview(); else StopPreview();
                    }
                    if (GUILayout.Button(m_Playing ? "暂停" : "播放", EditorStyles.miniButtonMid)) TogglePlay();
                }

                if (GUILayout.Button(new GUIContent("停止", "停止播放，回到 0 秒（不关预览）"), EditorStyles.miniButtonRight))
                {
                    m_Playing = false;
                    m_Time = 0f;
                    ApplyPreview();
                    Repaint();
                    SceneView.RepaintAll();
                }
                m_LoopPreview = GUILayout.Toggle(m_LoopPreview, new GUIContent("循环预览", "预览一直循环播放（与动效本身的循环设置无关）"), GUILayout.Width(72));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Game 视图", "打开 Game 视图。UI 和 3D 叠在一起的实际效果在这里看，预览时实时刷新。"),
                        EditorStyles.miniButton, GUILayout.Width(72)))
                    OpenGameView();
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

        /// <summary>单段编辑：只改选中的那一项（路径点与它出发的路径段、片段、轨道、事件）。</summary>
        void DrawSingleEdit()
        {
            var data = m_Asset.data;
            WindowSection.Begin("单段编辑 · " + SelectionName(data), "只改选中的这一项", WindowSection.SingleAccent);
            if (m_SelClip >= 0) DrawClipInspector(data);
            else if (m_SelTrack >= 0) DrawTrackInspector(data);
            else if (m_SelEvent >= 0) DrawEventInspector(data);
            else if (m_Selected >= 0 && m_Selected < data.points.Count) DrawSelectedPoint();
            else EditorGUILayout.LabelField("在时间轴上点选路径段、事件或片段；或在 Scene 视图里点击路径点。", EditorStyles.wordWrappedMiniLabel);
            WindowSection.End();
        }

        string SelectionName(MotionData data)
        {
            if (m_SelClip >= 0) return "片段";
            if (m_SelTrack >= 0) return "轨道";
            if (m_SelEvent >= 0) return "事件";
            if (m_Selected >= 0 && m_Selected < data.points.Count)
                return m_Selected < data.points.Count - 1 ? $"路径点 {m_Selected} 与路径段 {m_Selected}→{m_Selected + 1}" : $"路径点 {m_Selected}（终点）";
            return "未选中";
        }

        // ---------------------------------------------------------------- 路径点与路径段

        void DrawSelectedPoint()
        {
            var data = m_Asset.data;
            var eval = Evaluator;
            var p = data.points[m_Selected];

            WindowSection.SubHeading($"路径点 {m_Selected}（到达 {eval.GetPointTime(m_Selected):0.###}s）");

            if (m_Selected == 0)
            {
                EditorGUILayout.HelpBox("起点固定在对象的初始位置，要改起点请直接移动对象。", MessageType.None);
            }
            else if (IsEndLocked && m_Selected == data.points.Count - 1)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Vector3Field("位置（相对起点）", p.position - data.points[0].position);
                EditorGUILayout.HelpBox($"终点跟随落点对象“{m_EndTarget.name}”。要手动改，先清空落点对象，或在动效设置里取消终点绑定。", MessageType.None);
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

            WindowSection.SubHeading($"路径段 {index} → {index + 1}");

            EditorGUI.BeginChangeCheck();
            var timing = (TimingMode)EditorGUILayout.EnumPopup(new GUIContent("计时方式", "Duration 填时长 / Speed 填速度，按弧长自动算时长"), seg.timing);
            float duration = seg.duration, speed = seg.speed;
            if (timing == TimingMode.Duration) duration = Mathf.Max(0f, EditorGUILayout.FloatField("时长（秒）", seg.duration));
            else speed = Mathf.Max(0f, EditorGUILayout.FloatField(IsUiTarget ? "速度（像素/秒）" : "速度（单位/秒）", seg.speed));
            var easeType = (EaseType)EditorGUILayout.EnumPopup("缓动", seg.ease.type);
            AnimationCurve curve = seg.ease.curve;
            if (easeType == EaseType.Custom)
                curve = EditorGUILayout.CurveField("自定义曲线", curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f));
            string desc = EditorGUILayout.TextField(new GUIContent("描述", "写给 AI 看的一句话，例如“向左上弹起”。留空时用下面自动生成的描述。"), seg.desc ?? "");
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

            AutoDescHint(seg.desc, () => MotionDescriber.Segment(m_Asset.data, eval, index));
            EditorGUILayout.LabelField("实际时长 / 弧长",
                $"{eval.GetSegmentDuration(index):0.###}s / {eval.GetSegmentLength(index):0.##}{(IsUiTarget ? " px" : "")}");

            if (index != eval.SegmentCount - 1) return;
            var loop = m_Asset.data.loop;
            if (loop.scope == MotionLoopScope.LastSegment && loop.count != 1)
            {
                EditorGUILayout.LabelField($"这是尾段，正在自循环（{LoopCountText(loop.count)}，{loop.type}），在下方全局编辑里修改。", EditorStyles.wordWrappedMiniLabel);
            }
            else if (GUILayout.Button(new GUIContent("让这一段自循环", "前面只播一次，之后只循环这最后一段（全局编辑里的循环范围改为尾段，次数默认无限）")))
            {
                RecordUndo("Loop Last Segment");
                loop.scope = MotionLoopScope.LastSegment;
                if (loop.count == 1) loop.count = -1;
                MarkChanged();
                GUIUtility.ExitGUI();
            }
        }

        static string LoopCountText(int count) => count < 0 ? "无限次" : $"共 {count} 次";

        /// <summary>描述留空时，在下面用小字显示自动生成的描述（导出 JSON 和代码注释用的就是它）。</summary>
        static void AutoDescHint(string manual, Func<string> auto)
        {
            if (!string.IsNullOrWhiteSpace(manual)) return;
            EditorGUILayout.LabelField("自动：" + auto(), EditorStyles.wordWrappedMiniLabel);
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
            WindowSection.SubHeading($"轨道 · {TrackTitle(track)}");

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
            DrawTrackSettings(track);
            EditorGUILayout.LabelField("片段数", track.clips.Count.ToString());

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("在播放头处加片段")) { AddClipAt(m_SelTrack, LocalTime); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("上移", GUILayout.Width(44))) { MoveTrack(m_SelTrack, -1); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("下移", GUILayout.Width(44))) { MoveTrack(m_SelTrack, 1); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("删除轨道", GUILayout.Width(64))) { DeleteTrack(m_SelTrack); GUIUtility.ExitGUI(); }
            }
        }

        /// <summary>轨道子类带 [TrackSetting] 的字段，例如透明度作用在哪个组件上。改了要重新记录预览的初始值。</summary>
        void DrawTrackSettings(MotionTrack track)
        {
            foreach (var field in MotionTrackSettings.Fields(track.GetType()))
            {
                var attr = MotionTrackSettings.Attribute(field);
                var label = new GUIContent(attr.Label, attr.Tooltip);
                object value = field.GetValue(track);
                EditorGUI.BeginChangeCheck();
                object changed;
                if (field.FieldType.IsEnum) changed = EditorGUILayout.EnumPopup(label, (Enum)value);
                else if (field.FieldType == typeof(bool)) changed = EditorGUILayout.Toggle(label, (bool)value);
                else if (field.FieldType == typeof(int)) changed = EditorGUILayout.IntField(label, (int)value);
                else if (field.FieldType == typeof(float)) changed = EditorGUILayout.FloatField(label, (float)value);
                else changed = EditorGUILayout.TextField(label, (string)value ?? "");
                if (!EditorGUI.EndChangeCheck()) continue;
                RecordUndo("Edit Motion Track");
                field.SetValue(track, changed);
                TracksChanged();
                MarkChanged();
            }
        }

        void DrawClipInspector(MotionData data)
        {
            var eval = Evaluator;
            var track = data.tracks[m_SelTrack];
            var clip = track.clips[m_SelClip];
            if (clip.ease == null) clip.ease = new EaseSpec();
            eval.TryGetClipRange(m_SelTrack, m_SelClip, out float start, out float end);

            WindowSection.SubHeading($"片段 · {TrackTitle(track)}");

            EditorGUI.BeginChangeCheck();
            string anchor = AnchorPopup(clip.anchor, data, eval);
            float offset = EditorGUILayout.FloatField(new GUIContent("偏移（秒）", "开始时间 = 锚点到达时间 + 偏移。改段时长时片段跟着锚点走。"), clip.offset);
            float duration = Mathf.Max(0f, EditorGUILayout.FloatField("时长（秒）", clip.duration));
            var easeType = (EaseType)EditorGUILayout.EnumPopup("缓动", clip.ease.type);
            AnimationCurve curve = clip.ease.curve;
            if (easeType == EaseType.Custom)
                curve = EditorGUILayout.CurveField("自定义曲线", curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var value = ClipValueField(track, clip.value);
            string desc = EditorGUILayout.TextField(new GUIContent("描述", "写给 AI 看的一句话，例如“落地压扁”。留空时用下面自动生成的描述。"), clip.desc ?? "");
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

            AutoDescHint(clip.desc, () => MotionDescriber.Clip(data, eval, m_SelTrack, m_SelClip));
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

            WindowSection.SubHeading($"事件（{time:0.###}s）");
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

        // ---------------------------------------------------------------- 全局编辑

        static readonly GUIContent[] s_LoopScopeLabels =
        {
            new GUIContent("整段循环", "整个动效从头循环"),
            new GUIContent("尾段自循环", "前面只播一次，之后只循环最后一段路径（从最后一段开始到结尾），例如飞到位后原地浮动"),
        };

        /// <summary>全局编辑：作用于整个动效的设置（名称、描述、循环、终点绑定）。</summary>
        void DrawGlobalEdit()
        {
            var data = m_Asset.data;
            var eval = Evaluator;
            WindowSection.Begin("全局编辑 · 整个动效", "影响整段动效", WindowSection.GlobalAccent);

            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("名称", data.name ?? "");
            EditorGUILayout.LabelField(new GUIContent("描述", "整段动效写给 AI 看的说明"));
            string description = EditorGUILayout.TextArea(data.description ?? "", GUILayout.MinHeight(36));
            if (EditorGUI.EndChangeCheck())
            {
                RecordUndo("Edit Motion Settings");
                data.name = name;
                data.description = description;
                MarkChanged();
            }

            WindowSection.SubHeading("循环");
            EditorGUI.BeginChangeCheck();
            int scope;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("循环范围", "整段循环：整个动效从头循环。尾段自循环：前面只播一次，之后只循环最后一段路径。"));
                scope = GUILayout.Toolbar((int)data.loop.scope, s_LoopScopeLabels, EditorStyles.miniButton);
            }
            bool tail = scope == (int)MotionLoopScope.LastSegment;
            bool infinite = data.loop.count < 0;
            int count = Mathf.Max(1, data.loop.count);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(infinite))
                    count = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent(tail ? "尾段播放次数" : "播放次数", "1 = 不循环"), count));
                infinite = EditorGUILayout.ToggleLeft("无限", infinite, GUILayout.Width(52));
            }
            MotionLoopType loopType;
            using (new EditorGUI.DisabledScope(!infinite && count == 1))
                loopType = (MotionLoopType)EditorGUILayout.EnumPopup(new GUIContent("循环方式", "Restart 每次从头播 / Yoyo 来回播"), data.loop.type);
            if (EditorGUI.EndChangeCheck())
            {
                RecordUndo("Edit Motion Loop");
                data.loop.scope = (MotionLoopScope)scope;
                data.loop.count = infinite ? -1 : count;
                data.loop.type = loopType;
                MarkChanged();
            }
            EditorGUILayout.LabelField(LoopSummary(data, eval), EditorStyles.wordWrappedMiniLabel);

            WindowSection.SubHeading("终点绑定");
            DrawEndBinding();
            WindowSection.End();
        }

        static string LoopSummary(MotionData data, MotionEvaluator eval)
        {
            string single = $"单次 {eval.Duration:0.###}s（路径 {eval.PathDuration:0.###}s）";
            var loop = data.loop;
            if (loop.count == 1) return single + "，不循环。";
            string total = loop.count < 0 ? "无限循环" : $"总计 {eval.TotalDuration:0.###}s";
            if (loop.scope == MotionLoopScope.LastSegment && eval.SegmentCount > 0)
                return $"{single}。前 {eval.LoopStart:0.###}s 只播一次，之后尾段 {eval.LoopStart:0.###}s → {eval.Duration:0.###}s {LoopCountText(loop.count)}，{total}。";
            return $"{single}，整段{LoopCountText(loop.count)}，{total}。";
        }

        void DrawEndBinding()
        {
            var binding = EndBinding;
            EditorGUI.BeginChangeCheck();
            bool bound = EditorGUILayout.Toggle(new GUIContent("终点绑定",
                "开启后终点在运行时对准一个对象（由代码传入），当前终点只是设计参考：起点不动，整条路径按进度修正过去。"), binding.bound);
            string hint = binding.hint ?? "";
            var kind = binding.kind;
            // 按改之前的值决定显示哪些行，保持本次事件的控件与 Layout 一致。
            if (binding.bound)
            {
                hint = EditorGUILayout.TextField(new GUIContent("落点说明", "写给人和 AI 看，例如“顶部金币栏图标”"), hint);
                using (new EditorGUI.DisabledScope(m_EndTarget != null))
                    kind = (TargetKind)EditorGUILayout.EnumPopup(new GUIContent("落点类型", "RectTransform = UI，Transform = 3D。指定了落点对象时自动判断。"), kind);
            }
            if (EditorGUI.EndChangeCheck())
            {
                RecordUndo("Edit End Binding");
                bool toggled = bound != binding.bound;
                binding.bound = bound;
                binding.hint = hint;
                binding.kind = kind;
                MarkChanged();
                if (toggled) GUIUtility.ExitGUI();
            }
        }

        // ---------------------------------------------------------------- 提示与警告

        struct Problem
        {
            public MessageType type;
            public string text;
            public Problem(MessageType type, string text) { this.type = type; this.text = text; }
        }

        static GUIStyle s_ProblemStyle;
        static GUIStyle ProblemStyle => s_ProblemStyle ?? (s_ProblemStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
        {
            imagePosition = ImagePosition.ImageLeft,
            richText = false,
        });

        List<Problem> CollectProblems()
        {
            var list = new List<Problem>();
            var data = m_Asset.data;
            for (int i = 0; i < data.tracks.Count; i++)
                if (data.tracks[i] == null)
                    list.Add(new Problem(MessageType.Error, $"第 {i + 1} 条轨道的类型找不到（脚本被删或改名），可以在时间轴上右键删除它。"));
            foreach (var warning in Evaluator.Warnings)
                list.Add(new Problem(MessageType.Warning, warning));
            if (m_Preview)
                foreach (var name in m_PreviewState.UnsupportedTracks)
                    list.Add(new Problem(MessageType.Warning, $"预览目标上没有轨道 {name} 需要的组件，该轨道不预览。"));
            if (IsEndLocked && m_EndSyncFailed)
                list.Add(new Problem(MessageType.Warning, IsCrossSpace
                    ? "落点对象换算不到屏幕位置：场景里没有可用的 3D 相机，或落点在相机背后。终点暂时停在上次的位置。"
                    : "落点对象的位置换算失败（UI 预览目标的父节点不是 RectTransform？）。终点暂时停在上次的位置。"));
            if (m_EndTarget != null && m_Target != null && m_EndTarget.IsChildOf(m_Target))
                list.Add(new Problem(MessageType.Warning, "落点对象是预览目标本身或它的子节点，终点不跟随。"));
            if (EndBinding.bound && m_EndTarget == null)
                list.Add(new Problem(MessageType.Info, "终点绑定了落点对象（" + (string.IsNullOrEmpty(EndBinding.hint) ? "未写说明" : EndBinding.hint) +
                    "），但这里没指定。预览按设计时的终点播放；在“落点对象”里指定后，终点会自动对准它。"));
            AddContextProblems(list);
            if (data.loop.scope == MotionLoopScope.LastSegment && data.loop.count != 1 && data.points.Count < 2)
                list.Add(new Problem(MessageType.Info, "设成了尾段自循环，但还没有路径段，暂按整段循环。"));
            var exportProblems = new List<(MessageType type, string text)>();
            DotweenExportActions.CollectProblems(data, exportProblems);
            foreach (var p in exportProblems) list.Add(new Problem(p.type, p.text));
            return list;
        }

        void AddContextProblems(List<Problem> list)
        {
            var ctx = EditContext;
            if (ctx == null) return;
            var main = ctx.Main;
            string path = EditContextStore.ContainerPath(main);
            if (string.IsNullOrEmpty(path))
            {
                if (m_Target == null) list.Add(new Problem(MessageType.Info, "上次编辑的场景/预制体已不存在，请重新指定预览目标。"));
                return;
            }
            string where = (main.prefab ? "预制体 " : "场景 ") + Path.GetFileNameWithoutExtension(path);
            if (!EditContextStore.IsOpen(main))
            {
                if (m_Target == null) list.Add(new Problem(MessageType.Info, $"上次在{where}里编辑。点“上次编辑”一行的“打开”回到那里。"));
                return;
            }
            if (!ctx.mover.IsEmpty && m_Target == null)
                list.Add(new Problem(MessageType.Warning, $"上次的预览目标“{RefName(ctx.mover, main)}”在{where}里没找到（被删除或改名了？），请重新指定。"));
            if (!ctx.endTarget.IsEmpty && m_EndTarget == null && EditContextStore.IsOpen(ctx.endTarget))
                list.Add(new Problem(MessageType.Warning, $"上次的落点对象“{RefName(ctx.endTarget, ctx.endTarget)}”没找到（被删除或改名了？），请重新指定。"));
        }

        static string RefName(MotionObjectRef r, MotionObjectRef container)
        {
            string name = r.Name;
            return string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(EditContextStore.ContainerPath(container)) : name;
        }

        /// <summary>提示与警告单独一块，固定在窗口下方，不跟着上面的内容滚走。</summary>
        void DrawProblems()
        {
            var problems = CollectProblems();
            int errors = 0, warnings = 0;
            foreach (var p in problems)
            {
                if (p.type == MessageType.Error) errors++;
                else if (p.type == MessageType.Warning) warnings++;
            }
            int infos = problems.Count - errors - warnings;
            var accent = errors > 0 ? WindowSection.ErrorAccent : warnings > 0 ? WindowSection.WarningAccent : WindowSection.OkAccent;
            string subtitle = problems.Count == 0 ? "一切正常" : $"错误 {errors} · 警告 {warnings} · 提示 {infos}";

            WindowSection.Begin("提示与警告", subtitle, accent);
            if (problems.Count == 0)
            {
                EditorGUILayout.LabelField("没有错误或警告。", EditorStyles.miniLabel);
            }
            else
            {
                bool scroll = problems.Count > 3;
                if (scroll) m_ProblemScroll = EditorGUILayout.BeginScrollView(m_ProblemScroll, GUILayout.Height(76f));
                foreach (var p in problems)
                    GUILayout.Label(new GUIContent(" " + p.text, ProblemIcon(p.type)), ProblemStyle);
                if (scroll) EditorGUILayout.EndScrollView();
            }
            WindowSection.End();
        }

        static Texture ProblemIcon(MessageType type)
        {
            string name = type == MessageType.Error ? "console.erroricon.sml" : type == MessageType.Warning ? "console.warnicon.sml" : "console.infoicon.sml";
            return EditorGUIUtility.IconContent(name).image;
        }

        void DrawFooter()
        {
            WindowSection.Begin("导出", "JSON 给 AI 读；DOTween 代码直接放进工程", WindowSection.ExportAccent);
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
            using (new EditorGUILayout.HorizontalScope())
            {
                var copyTip = new GUIContent("复制 DOTween 代码", $"生成静态类 {DotweenExportActions.ClassName(m_Asset.data)}，调用 Play(target) 播放，结果与预览一致");
                if (GUILayout.Button(copyTip)) DotweenExportActions.Copy(this, m_Asset.data);
                if (GUILayout.Button(new GUIContent("导出 C#…", "保存成 .cs 文件，类名跟文件名一致；存到 Assets 下会自动导入"))) DotweenExportActions.Export(this, m_Asset.data);
            }
            EditorGUI.BeginChangeCheck();
            string ns = EditorGUILayout.DelayedTextField(new GUIContent("代码命名空间", "导出代码放进这个命名空间，留空为全局。整个工程共用。"), DotweenExportActions.Namespace);
            if (EditorGUI.EndChangeCheck()) DotweenExportActions.Namespace = ns.Trim();
            if (!DotweenExportActions.HasDotween)
            {
                EditorGUILayout.LabelField("本工程没装 DOTween：代码可以复制或导出到工程外，放进 Assets 会编译报错。", EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || m_Target == null))
                {
                    var runTip = new GUIContent("Play 模式试播导出代码", EditorApplication.isPlaying
                        ? "在预览目标上用导出到 Assets 的类真正跑一遍 DOTween，和预览对比；再点一次从头重播"
                        : "进入 Play 模式后可用：先“导出 C#…”到 Assets 下");
                    if (GUILayout.Button(runTip))
                    {
                        StopPreview();
                        DotweenExportActions.RunInPlayMode(this, m_Asset.data, m_Target, m_EndTarget, m_WorldCamera);
                    }
                }
            }
            WindowSection.End();
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
            SaveMover();
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
