using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// 窗口里的时间轴（单次播放的时间范围）：
    /// 标尺（点击/拖动定位播放头）、路径段条（拖分界线改段时长）、事件行、每条属性轨道一行（拖动、拉伸片段）。
    /// 双击空白处添加，右键弹出菜单。只管绘制与鼠标交互，数据修改走 <see cref="MotionEditing"/>，Undo 与刷新走窗口。
    /// </summary>
    internal sealed class TimelineView
    {
        const float HeaderWidth = 118f;
        const float RulerHeight = 20f;
        const float RowHeight = 22f;
        const float Padding = 10f;
        const float EdgeGrab = 5f;
        const float MarkerGrab = 6f;
        const float SnapPixels = 8f;
        const float MinDuration = 0.01f;

        const int PathRow = 0;
        const int EventRow = 1;
        const int FirstTrackRow = 2;

        static readonly Color BackgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
        static readonly Color HeaderColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        static readonly Color RulerColor = new Color(0.13f, 0.13f, 0.13f, 1f);
        static readonly Color RowColorA = new Color(1f, 1f, 1f, 0.025f);
        static readonly Color RowColorB = new Color(1f, 1f, 1f, 0.05f);
        static readonly Color TickColor = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color GuideColor = new Color(1f, 1f, 1f, 0.07f);
        static readonly Color LoopFillColor = new Color(0.42f, 0.6f, 1f, 0.1f);
        static readonly Color LoopLineColor = new Color(0.42f, 0.6f, 1f, 0.85f);
        static readonly Color SegmentColorA = new Color(0.25f, 0.6f, 0.75f, 1f);
        static readonly Color SegmentColorB = new Color(0.2f, 0.5f, 0.65f, 1f);
        static readonly Color PointColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        static readonly Color StartColor = new Color(0.4f, 1f, 0.4f, 1f);
        static readonly Color SelectedColor = new Color(1f, 0.85f, 0.2f, 1f);
        static readonly Color EventColor = new Color(1f, 0.75f, 0.3f, 1f);
        static readonly Color PlayheadColor = new Color(1f, 0.3f, 0.3f, 1f);
        static readonly Color SnapColor = new Color(0.4f, 0.9f, 1f, 0.9f);
        static readonly Color SelectedHeaderColor = new Color(0.24f, 0.37f, 0.59f, 0.6f);

        static readonly Color[] TrackPalette =
        {
            new Color(0.93f, 0.55f, 0.25f, 1f),
            new Color(0.62f, 0.45f, 0.9f, 1f),
            new Color(0.45f, 0.75f, 0.45f, 1f),
            new Color(0.9f, 0.45f, 0.6f, 1f),
            new Color(0.5f, 0.7f, 0.9f, 1f),
            new Color(0.85f, 0.8f, 0.4f, 1f),
        };

        enum DragKind { None, Scrub, Boundary, ClipMove, ClipStart, ClipEnd, Event }

        DragKind m_Drag;
        int m_DragTrack;
        int m_DragIndex;
        float m_DragMouseTime;
        float m_DragOrigStart;
        float m_DragOrigEnd;
        int m_UndoGroup;
        float m_FrozenRange;
        float m_SnapLine = -1f;

        Rect m_Content;
        float m_Range = 1f;

        public static float GetHeight(MotionData data) => RulerHeight + RowHeight * (FirstTrackRow + data.tracks.Count + 1);

        public static Color GetTrackColor(int trackIndex) => TrackPalette[trackIndex % TrackPalette.Length];

        public void Draw(CurveMotionWindow window)
        {
            var data = window.Asset.data;
            var eval = window.Evaluator;
            var rect = GUILayoutUtility.GetRect(10f, GetHeight(data), GUILayout.ExpandWidth(true));
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;

            // 拖动中途丢了鼠标捕获（例如按了 Esc），结束拖动状态。
            if (m_Drag != DragKind.None && GUIUtility.hotControl != id) m_Drag = DragKind.None;

            m_Content = new Rect(rect.x + HeaderWidth, rect.y, Mathf.Max(40f, rect.width - HeaderWidth), rect.height);
            m_Range = m_Drag != DragKind.None ? m_FrozenRange : ComputeRange(data, eval);

            DrawHeaders(window, data, eval, new Rect(rect.x, rect.y, HeaderWidth, rect.height));
            if (e.type == EventType.Repaint) DrawContent(window, data, eval);
            HandleMouse(window, data, eval, id, e);
        }

        // ---------------------------------------------------------------- 坐标换算

        float Width => Mathf.Max(1f, m_Content.width - 2f * Padding);
        float TimeToX(float t) => m_Content.x + Padding + t / m_Range * Width;
        float XToTime(float x) => (x - m_Content.x - Padding) / Width * m_Range;
        float PixelsToTime(float px) => px / Width * m_Range;

        static Rect RowRect(Rect area, int row) => new Rect(area.x, area.y + RulerHeight + row * RowHeight, area.width, RowHeight);

        static float ComputeRange(MotionData data, MotionEvaluator eval)
        {
            float end = eval.Duration;
            for (int t = 0; t < data.tracks.Count; t++)
            {
                var clips = data.tracks[t]?.clips;
                if (clips == null) continue;
                for (int c = 0; c < clips.Count; c++)
                    if (eval.TryGetClipRange(t, c, out float start, out _))
                        end = Mathf.Max(end, start + clips[c].duration);
            }
            return Mathf.Max(0.5f, end * 1.15f);
        }

        /// <summary>编辑产生的时间统一保留到毫秒，JSON 里不会出现 0.35000002 这类数。</summary>
        static float Round(float t) => Mathf.Round(t * 1000f) / 1000f;

        // ---------------------------------------------------------------- 表头

        void DrawHeaders(CurveMotionWindow window, MotionData data, MotionEvaluator eval, Rect area)
        {
            var e = Event.current;
            if (e.type == EventType.Repaint) EditorGUI.DrawRect(area, HeaderColor);

            GUI.Label(new Rect(area.x + 4f, area.y + 2f, area.width - 8f, RulerHeight - 4f),
                $"{window.LocalTime:0.00} / {eval.Duration:0.00}s", EditorStyles.miniLabel);

            var path = RowRect(area, PathRow);
            GUI.Label(Inset(path), "路径", EditorStyles.miniBoldLabel);
            if (GUI.Button(PlusRect(path), new GUIContent("+", "末尾加一个路径点"), EditorStyles.miniButton))
            {
                window.AppendPoint();
                GUIUtility.ExitGUI();
            }

            var events = RowRect(area, EventRow);
            GUI.Label(Inset(events), $"事件（{data.events.Count}）", EditorStyles.miniBoldLabel);
            if (GUI.Button(PlusRect(events), new GUIContent("+", "在播放头处加事件"), EditorStyles.miniButton))
            {
                window.AddEventAt(window.LocalTime);
                GUIUtility.ExitGUI();
            }

            for (int t = 0; t < data.tracks.Count; t++)
            {
                var row = RowRect(area, FirstTrackRow + t);
                var track = data.tracks[t];
                bool selected = window.SelectedTrack == t;
                if (e.type == EventType.Repaint)
                {
                    if (selected) EditorGUI.DrawRect(row, SelectedHeaderColor);
                    EditorGUI.DrawRect(new Rect(row.x, row.y + 3f, 3f, row.height - 6f), GetTrackColor(t));
                }
                if (track == null)
                {
                    GUI.Label(Inset(row), "（轨道类型缺失）", EditorStyles.miniLabel);
                    continue;
                }

                bool enabled = GUI.Toggle(new Rect(row.x + 6f, row.y + 3f, 16f, 16f), track.enabled, new GUIContent("", "启用/停用这条轨道"));
                if (enabled != track.enabled)
                {
                    window.RecordUndo("Toggle Motion Track");
                    track.enabled = enabled;
                    window.MarkChanged();
                }
                var label = new Rect(row.x + 24f, row.y, row.width - 26f, row.height);
                if (GUI.Button(label, string.IsNullOrEmpty(track.name) ? track.DisplayName : track.name,
                        selected ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel))
                {
                    if (e.button == 1) ShowTrackMenu(window, t);
                    else window.SelectTrack(t);
                    GUIUtility.ExitGUI();
                }
            }

            var add = RowRect(area, FirstTrackRow + data.tracks.Count);
            if (GUI.Button(new Rect(add.x + 4f, add.y + 2f, add.width - 8f, add.height - 4f), "+ 添加属性轨道", EditorStyles.miniButton))
                ShowAddTrackMenu(window);
        }

        static Rect Inset(Rect row) => new Rect(row.x + 6f, row.y, row.width - 30f, row.height);
        static Rect PlusRect(Rect row) => new Rect(row.xMax - 24f, row.y + 3f, 20f, row.height - 6f);

        static void ShowAddTrackMenu(CurveMotionWindow window)
        {
            var menu = new GenericMenu();
            foreach (var type in MotionTrackRegistry.TrackTypes)
            {
                string typeId = MotionTrackRegistry.GetTypeId(type);
                menu.AddItem(new GUIContent($"{MotionTrackRegistry.GetDisplayName(type)}（{typeId}）"), false, () => window.AddTrack(typeId));
            }
            menu.ShowAsContext();
        }

        static void ShowTrackMenu(CurveMotionWindow window, int track)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("在播放头处加片段"), false, () => window.AddClipAt(track, window.LocalTime));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("上移"), false, () => window.MoveTrack(track, -1));
            menu.AddItem(new GUIContent("下移"), false, () => window.MoveTrack(track, 1));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除轨道"), false, () => window.DeleteTrack(track));
            menu.ShowAsContext();
        }

        // ---------------------------------------------------------------- 绘制

        void DrawContent(CurveMotionWindow window, MotionData data, MotionEvaluator eval)
        {
            EditorGUI.DrawRect(m_Content, BackgroundColor);
            int rows = FirstTrackRow + data.tracks.Count;
            for (int r = 0; r < rows; r++)
                EditorGUI.DrawRect(RowRect(m_Content, r), (r & 1) == 0 ? RowColorA : RowColorB);

            DrawRuler();

            // 路径点时间的竖向参考线，方便对齐片段与事件。
            float rowsTop = m_Content.y + RulerHeight;
            float rowsHeight = rows * RowHeight;
            for (int i = 0; i < data.points.Count; i++)
                EditorGUI.DrawRect(new Rect(TimeToX(eval.GetPointTime(i)), rowsTop, 1f, rowsHeight), GuideColor);
            DrawLoopRange(data, eval, rowsTop, rowsHeight);

            DrawPathRow(window, data, eval);
            DrawEventRow(window, data, eval);
            for (int t = 0; t < data.tracks.Count; t++) DrawTrackRow(window, data, eval, t);

            if (m_Drag != DragKind.None && m_SnapLine >= 0f)
                EditorGUI.DrawRect(new Rect(TimeToX(m_SnapLine) - 0.5f, rowsTop, 1f, rowsHeight), SnapColor);

            float x = TimeToX(window.LocalTime);
            EditorGUI.DrawRect(new Rect(x - 0.5f, m_Content.y, 1.5f, RulerHeight + rowsHeight), PlayheadColor);
            EditorGUI.DrawRect(new Rect(x - 4f, m_Content.y + RulerHeight - 6f, 9f, 6f), PlayheadColor);
        }

        void DrawRuler()
        {
            var ruler = new Rect(m_Content.x, m_Content.y, m_Content.width, RulerHeight);
            EditorGUI.DrawRect(ruler, RulerColor);
            float step = PickStep(Width / m_Range);
            int count = Mathf.FloorToInt(m_Range / step + 1e-4f);
            for (int i = 0; i <= count; i++)
            {
                float t = i * step;
                float x = TimeToX(t);
                EditorGUI.DrawRect(new Rect(x, ruler.yMax - 6f, 1f, 6f), TickColor);
                GUI.Label(new Rect(x + 2f, ruler.y + 1f, 48f, RulerHeight - 4f), t.ToString("0.##") + "s", EditorStyles.miniLabel);

                float minor = TimeToX(t + step * 0.5f);
                if (minor < ruler.xMax) EditorGUI.DrawRect(new Rect(minor, ruler.yMax - 3f, 1f, 3f), TickColor);
            }
        }

        /// <summary>循环的那一截（整段或尾段）加一层底色，并在后面标出循环范围和次数。</summary>
        void DrawLoopRange(MotionData data, MotionEvaluator eval, float rowsTop, float rowsHeight)
        {
            var loop = data.loop;
            if (loop.count == 1 || eval.LoopLength <= 0f) return;
            float x0 = TimeToX(eval.LoopStart);
            float x1 = TimeToX(eval.Duration);
            EditorGUI.DrawRect(new Rect(x0, rowsTop, x1 - x0, rowsHeight), LoopFillColor);
            EditorGUI.DrawRect(new Rect(x0, rowsTop, x1 - x0, 2f), LoopLineColor);
            EditorGUI.DrawRect(new Rect(x0, rowsTop, 1f, rowsHeight), LoopLineColor);
            EditorGUI.DrawRect(new Rect(x1 - 1f, rowsTop, 1f, rowsHeight), LoopLineColor);
            string label = (eval.LoopStart > 0f ? "尾段循环 " : "整段循环 ") + (loop.count < 0 ? "无限" : "×" + loop.count) +
                           (loop.type == MotionLoopType.Yoyo ? " Yoyo" : "");
            GUI.Label(new Rect(x1 + 3f, rowsTop + 2f, 110f, RowHeight - 4f), label, EditorStyles.miniLabel);
        }

        static float PickStep(float pixelsPerSecond)
        {
            float[] steps = { 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 30f, 60f };
            foreach (var s in steps)
                if (s * pixelsPerSecond >= 56f) return s;
            return 60f;
        }

        void DrawPathRow(CurveMotionWindow window, MotionData data, MotionEvaluator eval)
        {
            var row = RowRect(m_Content, PathRow);
            for (int s = 0; s < eval.SegmentCount; s++)
            {
                float x0 = TimeToX(eval.GetPointTime(s));
                float x1 = TimeToX(eval.GetPointTime(s + 1));
                var r = new Rect(x0, row.y + 3f, Mathf.Max(1f, x1 - x0), row.height - 6f);
                EditorGUI.DrawRect(r, (s & 1) == 0 ? SegmentColorA : SegmentColorB);
                if (window.Selected == s) DrawOutline(r, SelectedColor);

                var seg = data.points[s].segment;
                string label = $"{s}→{s + 1}  {eval.GetSegmentDuration(s):0.###}s" +
                               (seg != null && seg.timing == TimingMode.Speed ? "（速度）" : "");
                if (r.width > 36f) GUI.Label(r, label, CenteredMiniLabel);
            }

            for (int i = 0; i < data.points.Count; i++)
            {
                float x = TimeToX(eval.GetPointTime(i));
                var marker = new Rect(x - 1.5f, row.y + 1f, 3f, row.height - 2f);
                EditorGUI.DrawRect(marker, i == window.Selected ? SelectedColor : i == 0 ? StartColor : PointColor);
                if (i > 0) EditorGUIUtility.AddCursorRect(new Rect(x - EdgeGrab, row.y, EdgeGrab * 2f, row.height), MouseCursor.ResizeHorizontal);
            }
        }

        void DrawEventRow(CurveMotionWindow window, MotionData data, MotionEvaluator eval)
        {
            var row = RowRect(m_Content, EventRow);
            for (int i = 0; i < data.events.Count; i++)
            {
                float x = TimeToX(eval.GetEventTime(i));
                bool selected = window.SelectedEvent == i;
                DrawDiamond(new Vector2(x, row.center.y), 5.5f, selected ? SelectedColor : EventColor);
                GUI.Label(new Rect(x + 7f, row.y, 90f, row.height), data.events[i].name, EditorStyles.miniLabel);
            }
        }

        void DrawTrackRow(CurveMotionWindow window, MotionData data, MotionEvaluator eval, int t)
        {
            var track = data.tracks[t];
            if (track?.clips == null) return;
            var row = RowRect(m_Content, FirstTrackRow + t);
            var baseColor = GetTrackColor(t);
            if (!track.enabled) baseColor.a = 0.35f;

            for (int c = 0; c < track.clips.Count; c++)
            {
                if (!eval.TryGetClipRange(t, c, out float start, out float end)) continue;
                var clip = track.clips[c];
                float fullEnd = start + clip.duration;
                var full = ClipRect(row, start, fullEnd);
                var live = ClipRect(row, start, end);
                var color = track is ColorTrack ? new Color(clip.value.x, clip.value.y, clip.value.z, baseColor.a) : baseColor;

                // 被后一个片段覆盖掉的部分画成半透明。
                if (fullEnd > end + 1e-4f) EditorGUI.DrawRect(full, new Color(color.r, color.g, color.b, color.a * 0.3f));
                EditorGUI.DrawRect(live, color);
                if (window.SelectedTrack == t && window.SelectedClip == c) DrawOutline(full, SelectedColor);
                if (live.width > 24f) GUI.Label(new Rect(live.x + 3f, live.y, live.width - 6f, live.height), ClipLabel(track, clip), ClipLabelStyle);

                EditorGUIUtility.AddCursorRect(new Rect(full.x - EdgeGrab, full.y, EdgeGrab * 2f, full.height), MouseCursor.ResizeHorizontal);
                EditorGUIUtility.AddCursorRect(new Rect(full.xMax - EdgeGrab, full.y, EdgeGrab * 2f, full.height), MouseCursor.ResizeHorizontal);
            }
        }

        Rect ClipRect(Rect row, float start, float end)
        {
            float x0 = TimeToX(start);
            float x1 = Mathf.Max(x0 + 3f, TimeToX(end));
            return new Rect(x0, row.y + 3f, x1 - x0, row.height - 6f);
        }

        internal static string ClipLabel(MotionTrack track, MotionClip clip)
        {
            var v = clip.value;
            if (track is ColorTrack) return "颜色";
            if (track is AlphaTrack) return "α " + Format(v.x);
            string prefix = track.Mode == ValueMode.Multiply ? "×" : track.Mode == ValueMode.Add ? "+" : "";
            int dim = Mathf.Clamp(track.Dimension, 1, 4);
            bool uniform = true;
            for (int i = 1; i < dim; i++)
                if (!Mathf.Approximately(v[i], v[0])) uniform = false;
            if (uniform) return prefix + Format(v.x);
            var parts = new string[dim];
            for (int i = 0; i < dim; i++) parts[i] = Format(v[i]);
            return prefix + "(" + string.Join(", ", parts) + ")";
        }

        static string Format(float f) => f.ToString("0.##");

        static void DrawOutline(Rect r, Color color)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1.5f), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1.5f, r.width, 1.5f), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1.5f, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - 1.5f, r.y, 1.5f, r.height), color);
        }

        static void DrawDiamond(Vector2 center, float size, Color color)
        {
            var previous = Handles.color;
            Handles.color = color;
            Handles.DrawAAConvexPolygon(
                new Vector3(center.x, center.y - size),
                new Vector3(center.x + size, center.y),
                new Vector3(center.x, center.y + size),
                new Vector3(center.x - size, center.y));
            Handles.color = previous;
        }

        static GUIStyle s_Centered;
        static GUIStyle CenteredMiniLabel => s_Centered ?? (s_Centered = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.white },
        });

        static GUIStyle s_ClipLabel;
        static GUIStyle ClipLabelStyle => s_ClipLabel ?? (s_ClipLabel = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.white },
        });

        // ---------------------------------------------------------------- 交互

        void HandleMouse(CurveMotionWindow window, MotionData data, MotionEvaluator eval, int id, Event e)
        {
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !m_Content.Contains(e.mousePosition)) return;
                    GUIUtility.keyboardControl = 0;
                    if (e.clickCount == 2 && DoubleClick(window, data, eval, e.mousePosition))
                    {
                        e.Use();
                        ExitAfterChange();
                    }
                    BeginDrag(window, data, eval, e.mousePosition);
                    if (m_Drag != DragKind.None)
                    {
                        GUIUtility.hotControl = id;
                        m_FrozenRange = m_Range;
                        Undo.IncrementCurrentGroup();
                        m_UndoGroup = Undo.GetCurrentGroup();
                    }
                    e.Use();
                    ExitAfterChange();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) return;
                    UpdateDrag(window, data, eval, e.mousePosition);
                    e.Use();
                    ExitAfterChange();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) return;
                    GUIUtility.hotControl = 0;
                    if (m_Drag != DragKind.Scrub) Undo.CollapseUndoOperations(m_UndoGroup);
                    m_Drag = DragKind.None;
                    m_SnapLine = -1f;
                    window.Repaint();
                    e.Use();
                    break;

                case EventType.ContextClick:
                    if (!m_Content.Contains(e.mousePosition)) return;
                    ShowContextMenu(window, data, eval, e.mousePosition);
                    e.Use();
                    break;
            }
        }

        /// <summary>
        /// 时间轴在检查器之前绘制。鼠标事件里改了选中项或数据后，下面的控件数会和本帧 Layout 时不一致，
        /// 直接结束本次 OnGUI，下一帧重新布局。
        /// </summary>
        static void ExitAfterChange() => GUIUtility.ExitGUI();

        int RowAt(Vector2 mouse)
        {
            float y = mouse.y - m_Content.y - RulerHeight;
            return y < 0f ? -1 : Mathf.FloorToInt(y / RowHeight);
        }

        int HitPointBoundary(MotionEvaluator eval, float x)
        {
            int best = -1;
            float bestDistance = EdgeGrab;
            for (int i = 1; i < eval.SegmentCount + 1; i++)
            {
                float d = Mathf.Abs(TimeToX(eval.GetPointTime(i)) - x);
                if (d > bestDistance) continue;
                bestDistance = d;
                best = i;
            }
            return best;
        }

        int HitSegment(MotionEvaluator eval, float time)
        {
            for (int s = 0; s < eval.SegmentCount; s++)
                if (time >= eval.GetPointTime(s) && time <= eval.GetPointTime(s + 1)) return s;
            return -1;
        }

        int HitEvent(MotionData data, MotionEvaluator eval, float x)
        {
            int best = -1;
            float bestDistance = MarkerGrab;
            for (int i = 0; i < data.events.Count; i++)
            {
                float d = Mathf.Abs(TimeToX(eval.GetEventTime(i)) - x);
                if (d > bestDistance) continue;
                bestDistance = d;
                best = i;
            }
            return best;
        }

        /// <summary>命中片段：先看两端（拉伸），再看本体（拖动）；后画的片段在上面，所以倒序找。</summary>
        int HitClip(MotionData data, MotionEvaluator eval, int t, float x, out DragKind kind)
        {
            kind = DragKind.None;
            var clips = data.tracks[t]?.clips;
            if (clips == null) return -1;
            for (int c = clips.Count - 1; c >= 0; c--)
            {
                if (!eval.TryGetClipRange(t, c, out float start, out _)) continue;
                float x0 = TimeToX(start);
                float x1 = Mathf.Max(x0 + 3f, TimeToX(start + clips[c].duration));
                if (Mathf.Abs(x - x1) <= EdgeGrab) { kind = DragKind.ClipEnd; return c; }
                if (Mathf.Abs(x - x0) <= EdgeGrab && x1 - x0 > EdgeGrab * 2f) { kind = DragKind.ClipStart; return c; }
                if (x > x0 && x < x1) { kind = DragKind.ClipMove; return c; }
            }
            return -1;
        }

        void BeginDrag(CurveMotionWindow window, MotionData data, MotionEvaluator eval, Vector2 mouse)
        {
            m_Drag = DragKind.None;
            m_SnapLine = -1f;
            float time = XToTime(mouse.x);
            m_DragMouseTime = time;
            int row = RowAt(mouse);

            if (row < 0)
            {
                m_Drag = DragKind.Scrub;
                window.Scrub(Mathf.Max(0f, time));
                return;
            }

            if (row == PathRow)
            {
                int boundary = HitPointBoundary(eval, mouse.x);
                if (boundary > 0)
                {
                    m_Drag = DragKind.Boundary;
                    m_DragIndex = boundary;
                    m_DragOrigEnd = eval.GetPointTime(boundary);
                    window.Select(boundary - 1);
                    return;
                }
                int segment = HitSegment(eval, time);
                if (segment >= 0) window.Select(segment);
                else window.ClearSelection();
                return;
            }

            if (row == EventRow)
            {
                int hit = HitEvent(data, eval, mouse.x);
                if (hit < 0)
                {
                    window.ClearSelection();
                    return;
                }
                window.SelectEvent(hit);
                m_Drag = DragKind.Event;
                m_DragIndex = hit;
                m_DragOrigStart = eval.GetEventTime(hit);
                return;
            }

            int t = row - FirstTrackRow;
            if (t < 0 || t >= data.tracks.Count) return;
            int c = HitClip(data, eval, t, mouse.x, out var kind);
            if (c < 0)
            {
                window.SelectTrack(t);
                return;
            }
            window.SelectClip(t, c);
            eval.TryGetClipRange(t, c, out float clipStart, out _);
            m_Drag = kind;
            m_DragTrack = t;
            m_DragIndex = c;
            m_DragOrigStart = clipStart;
            m_DragOrigEnd = clipStart + data.tracks[t].clips[c].duration;
        }

        void UpdateDrag(CurveMotionWindow window, MotionData data, MotionEvaluator eval, Vector2 mouse)
        {
            float mouseTime = XToTime(mouse.x);
            float delta = mouseTime - m_DragMouseTime;
            bool snap = window.SnapTimeline && !EditorGUI.actionKey;
            float threshold = PixelsToTime(SnapPixels);
            m_SnapLine = -1f;

            switch (m_Drag)
            {
                case DragKind.Scrub:
                    window.Scrub(Mathf.Max(0f, mouseTime));
                    return;

                case DragKind.Boundary:
                {
                    int segment = m_DragIndex - 1;
                    if (segment < 0 || segment >= eval.SegmentCount) return;
                    float segStart = eval.GetPointTime(segment);
                    float time = Mathf.Max(segStart + MinDuration, m_DragOrigEnd + delta);
                    if (snap) time = SnapWithLine(time, Candidates(window, data, eval, movingFromPoint: m_DragIndex), threshold);
                    window.RecordUndo("Change Segment Duration");
                    MotionEditing.SetSegmentDuration(data, eval, segment, Round(Mathf.Max(MinDuration, time - segStart)));
                    window.MarkChanged();
                    return;
                }

                case DragKind.Event:
                {
                    if (m_DragIndex >= data.events.Count) return;
                    var motionEvent = data.events[m_DragIndex];
                    float time = Mathf.Max(0f, m_DragOrigStart + delta);
                    if (snap) time = SnapWithLine(time, Candidates(window, data, eval, excludeEvent: motionEvent), threshold);
                    window.RecordUndo("Move Motion Event");
                    MotionEditing.SetEventTime(data, eval, motionEvent, Round(time));
                    window.MarkChanged();
                    return;
                }
            }

            var clips = m_DragTrack < data.tracks.Count ? data.tracks[m_DragTrack]?.clips : null;
            if (clips == null || m_DragIndex >= clips.Count) return;
            var clip = clips[m_DragIndex];
            var candidates = snap ? Candidates(window, data, eval, excludeClip: clip) : null;

            switch (m_Drag)
            {
                case DragKind.ClipMove:
                {
                    float length = m_DragOrigEnd - m_DragOrigStart;
                    float start = Mathf.Max(0f, m_DragOrigStart + delta);
                    if (snap)
                    {
                        start = TimeSnapping.SnapRange(start, length, candidates, threshold, out bool snapped, out float at);
                        if (snapped) m_SnapLine = at;
                        start = Mathf.Max(0f, start);
                    }
                    window.RecordUndo("Move Motion Clip");
                    MotionEditing.SetClipStart(data, eval, clip, Round(start));
                    window.MarkChanged();
                    break;
                }
                case DragKind.ClipStart:
                {
                    float end = m_DragOrigEnd;
                    float start = Mathf.Clamp(m_DragOrigStart + delta, 0f, end - MinDuration);
                    if (snap) start = Mathf.Min(SnapWithLine(start, candidates, threshold), end - MinDuration);
                    start = Round(start);
                    window.RecordUndo("Resize Motion Clip");
                    clip.duration = Round(end - start);
                    MotionEditing.SetClipStart(data, eval, clip, start);
                    window.MarkChanged();
                    break;
                }
                case DragKind.ClipEnd:
                {
                    float start = m_DragOrigStart;
                    float end = Mathf.Max(start + MinDuration, m_DragOrigEnd + delta);
                    if (snap) end = Mathf.Max(start + MinDuration, SnapWithLine(end, candidates, threshold));
                    window.RecordUndo("Resize Motion Clip");
                    clip.duration = Round(end - start);
                    window.MarkChanged();
                    break;
                }
            }
        }

        float SnapWithLine(float time, List<float> candidates, float threshold)
        {
            float snapped = TimeSnapping.Snap(time, candidates, threshold, out bool hit);
            if (hit) m_SnapLine = snapped;
            return snapped;
        }

        static List<float> Candidates(CurveMotionWindow window, MotionData data, MotionEvaluator eval,
            MotionClip excludeClip = null, MotionEvent excludeEvent = null, int movingFromPoint = int.MaxValue)
        {
            var list = TimeSnapping.CollectTimes(data, eval, excludeClip, excludeEvent, movingFromPoint);
            list.Add(window.LocalTime);
            return list;
        }

        bool DoubleClick(CurveMotionWindow window, MotionData data, MotionEvaluator eval, Vector2 mouse)
        {
            int row = RowAt(mouse);
            float time = Round(Mathf.Max(0f, XToTime(mouse.x)));
            if (row == EventRow && HitEvent(data, eval, mouse.x) < 0)
            {
                window.AddEventAt(time);
                return true;
            }
            int t = row - FirstTrackRow;
            if (t >= 0 && t < data.tracks.Count && HitClip(data, eval, t, mouse.x, out _) < 0)
            {
                window.AddClipAt(t, time);
                return true;
            }
            return false;
        }

        void ShowContextMenu(CurveMotionWindow window, MotionData data, MotionEvaluator eval, Vector2 mouse)
        {
            int row = RowAt(mouse);
            float time = Round(Mathf.Max(0f, XToTime(mouse.x)));
            var menu = new GenericMenu();

            if (row == PathRow)
            {
                int boundary = HitPointBoundary(eval, mouse.x);
                if (boundary > 0)
                    menu.AddItem(new GUIContent($"删除路径点 {boundary}"), false, () => window.DeletePoint(boundary));
                else
                    menu.AddItem(new GUIContent("在此处插入路径点"), false, () => window.InsertPointAtTime(time));
            }
            else if (row == EventRow)
            {
                int hit = HitEvent(data, eval, mouse.x);
                if (hit >= 0)
                {
                    menu.AddItem(new GUIContent("复制一份"), false, () => window.DuplicateEvent(hit));
                    menu.AddItem(new GUIContent("删除事件"), false, () => window.DeleteEvent(hit));
                }
                else menu.AddItem(new GUIContent("在此处加事件"), false, () => window.AddEventAt(time));
            }
            else
            {
                int t = row - FirstTrackRow;
                if (t < 0 || t >= data.tracks.Count) return;
                int c = HitClip(data, eval, t, mouse.x, out _);
                if (c >= 0)
                {
                    menu.AddItem(new GUIContent("复制一份"), false, () => window.DuplicateClip(t, c));
                    menu.AddItem(new GUIContent("删除片段"), false, () => window.DeleteClip(t, c));
                }
                else
                {
                    menu.AddItem(new GUIContent("在此处加片段"), false, () => window.AddClipAt(t, time));
                    if (window.HasClipboard) menu.AddItem(new GUIContent("粘贴到此处"), false, () => window.PasteAt(t, time));
                    else menu.AddDisabledItem(new GUIContent("粘贴到此处"));
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("删除轨道"), false, () => window.DeleteTrack(t));
            }
            menu.ShowAsContext();
        }
    }
}
