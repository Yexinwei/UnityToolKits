using UnityEditor;
using UnityEngine;

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// Scene 视图里的路径绘制与编辑：曲线、点、切线手柄、Shift+点击插入、Delete 删除、播放头，
    /// 以及事件点位置和选中片段对应的路径区间。
    /// 所有坐标都在路径空间（<see cref="CurveMotionWindow.PathMatrix"/>），由 Handles.matrix 转到世界。
    /// </summary>
    internal static class PathSceneEditor
    {
        const int DrawSamplesPerSegment = 32;
        const float InsertPickDistance = 12f;

        static readonly Color PathColor = new Color(0.25f, 0.85f, 1f, 1f);
        static readonly Color PointColor = Color.white;
        static readonly Color StartColor = new Color(0.4f, 1f, 0.4f, 1f);
        static readonly Color SelectedColor = new Color(1f, 0.85f, 0.2f, 1f);
        static readonly Color TangentColor = new Color(1f, 1f, 1f, 0.5f);
        static readonly Color InsertColor = new Color(1f, 0.5f, 0.2f, 1f);
        static readonly Color PlayheadColor = new Color(1f, 0.3f, 0.3f, 1f);
        static readonly Color EventColor = new Color(1f, 0.75f, 0.3f, 1f);
        const int SpanSamples = 32;

        public static void Draw(CurveMotionWindow window)
        {
            var data = window.Asset.data;
            var eval = window.Evaluator;
            if (data.points.Count == 0) return;

            var previousMatrix = Handles.matrix;
            var previousColor = Handles.color;
            Handles.matrix = window.PathMatrix;

            DrawPath(eval);
            DrawClipSpan(window, eval, data);
            DrawEvents(window, eval, data);
            if (window.EditPath)
            {
                HandleDelete(window);
                HandleInsert(window, eval);
                DrawTangents(window, data);
                DrawPoints(window, data, eval);
                DrawSelectedHandle(window, data);
            }
            DrawPlayhead(window, eval, data);

            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
        }

        static void DrawPath(MotionEvaluator eval)
        {
            Handles.color = PathColor;
            var samples = new Vector3[DrawSamplesPerSegment + 1];
            for (int s = 0; s < eval.SegmentCount; s++)
            {
                eval.GetSegmentControls(s, out var p0, out var p1, out var p2, out var p3);
                for (int i = 0; i <= DrawSamplesPerSegment; i++)
                    samples[i] = PathMath.Bezier(p0, p1, p2, p3, i / (float)DrawSamplesPerSegment);
                Handles.DrawAAPolyLine(4f, samples);
            }
        }

        static void DrawPoints(CurveMotionWindow window, MotionData data, MotionEvaluator eval)
        {
            for (int i = 0; i < data.points.Count; i++)
            {
                var pos = data.points[i].position;
                float size = HandleUtility.GetHandleSize(pos) * 0.07f;
                Handles.color = i == window.Selected ? SelectedColor : i == 0 ? StartColor : PointColor;
                if (Handles.Button(pos, Quaternion.identity, size, size * 1.6f, Handles.SphereHandleCap))
                    window.Select(i);
                Handles.Label(pos + Vector3.up * size * 2.5f, $"{i}  {eval.GetPointTime(i):0.##}s", EditorStyles.miniBoldLabel);
            }
        }

        static void DrawSelectedHandle(CurveMotionWindow window, MotionData data)
        {
            int index = window.Selected;
            if (index <= 0 || index >= data.points.Count) return;
            var point = data.points[index];

            EditorGUI.BeginChangeCheck();
            var pos = Handles.PositionHandle(point.position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                window.RecordUndo("Move Path Point");
                pos = window.SnapPathPosition(pos);
                if (window.IsUiTarget) pos.z = data.points[0].position.z;
                point.position = pos;
                window.MarkChanged();
            }
        }

        static void DrawTangents(CurveMotionWindow window, MotionData data)
        {
            int index = window.Selected;
            if (index < 0 || index >= data.points.Count) return;
            var point = data.points[index];
            PathMath.ResolveTangents(data.points, index, out var inT, out var outT);
            var pos = point.position;

            Handles.color = TangentColor;
            bool hasIn = index > 0;
            bool hasOut = index < data.points.Count - 1;
            if (hasIn) Handles.DrawDottedLine(pos, pos + inT, 3f);
            if (hasOut) Handles.DrawDottedLine(pos, pos + outT, 3f);

            bool editable = point.type == PointType.Smooth || point.type == PointType.Free;
            if (!editable) return;

            if (hasOut && TangentHandle(pos + outT, out var newOut))
            {
                window.RecordUndo("Edit Tangent");
                point.outTangent = Flatten(window, newOut - pos);
                if (point.type == PointType.Smooth) point.inTangent = -point.outTangent;
                window.MarkChanged();
            }
            if (hasIn && TangentHandle(pos + inT, out var newIn))
            {
                window.RecordUndo("Edit Tangent");
                var t = Flatten(window, newIn - pos);
                if (point.type == PointType.Smooth) point.outTangent = -t;
                point.inTangent = t;
                window.MarkChanged();
            }
        }

        static bool TangentHandle(Vector3 position, out Vector3 moved)
        {
            float size = HandleUtility.GetHandleSize(position) * 0.05f;
            EditorGUI.BeginChangeCheck();
#if UNITY_2022_1_OR_NEWER
            moved = Handles.FreeMoveHandle(position, size, Vector3.zero, Handles.RectangleHandleCap);
#else
            moved = Handles.FreeMoveHandle(position, Quaternion.identity, size, Vector3.zero, Handles.RectangleHandleCap);
#endif
            return EditorGUI.EndChangeCheck();
        }

        static Vector3 Flatten(CurveMotionWindow window, Vector3 v)
        {
            v = window.SnapPathVector(v);
            if (window.IsUiTarget) v.z = 0f;
            return v;
        }

        /// <summary>选中片段时，把它生效的时间段对应的那截路径加粗高亮，便于看出变化发生在哪里。</summary>
        static void DrawClipSpan(CurveMotionWindow window, MotionEvaluator eval, MotionData data)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!window.TryGetSelectedClipSpan(out float start, out float end, out var color)) return;
            start = Mathf.Min(start, eval.PathDuration);
            end = Mathf.Min(end, eval.PathDuration);
            if (end - start <= 1e-4f) return;

            var origin = data.points[0].position;
            var samples = new Vector3[SpanSamples + 1];
            for (int i = 0; i <= SpanSamples; i++)
                samples[i] = origin + eval.EvaluatePosition(Mathf.Lerp(start, end, i / (float)SpanSamples));
            Handles.color = new Color(color.r, color.g, color.b, 0.85f);
            Handles.DrawAAPolyLine(9f, samples);
        }

        /// <summary>事件在路径上的触发位置。</summary>
        static void DrawEvents(CurveMotionWindow window, MotionEvaluator eval, MotionData data)
        {
            if (Event.current.type != EventType.Repaint) return;
            var origin = data.points[0].position;
            for (int i = 0; i < data.events.Count; i++)
            {
                var pos = origin + eval.EvaluatePosition(eval.GetEventTime(i));
                float size = HandleUtility.GetHandleSize(pos) * 0.05f;
                Handles.color = i == window.SelectedEvent ? SelectedColor : EventColor;
                Handles.DotHandleCap(0, pos, Quaternion.identity, size, EventType.Repaint);
                Handles.Label(pos + Vector3.down * size * 3f, data.events[i].name, EditorStyles.miniLabel);
            }
        }

        /// <summary>按住 Shift：在离鼠标最近的路径位置显示插入点，点击插入。</summary>
        static void HandleInsert(CurveMotionWindow window, MotionEvaluator eval)
        {
            // 每个事件都申请控件 id，保证 Layout 与 MouseDown 之间 id 一致。
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            if (!e.shift || eval.SegmentCount == 0) return;

            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId);
            if (e.type == EventType.MouseMove) HandleUtility.Repaint();

            if (!FindNearestOnPath(eval, e.mousePosition, out int segment, out float fraction, out var position)) return;

            Handles.color = InsertColor;
            float size = HandleUtility.GetHandleSize(position) * 0.06f;
            Handles.SphereHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);

            if (e.type == EventType.MouseDown && e.button == 0 && HandleUtility.nearestControl == controlId)
            {
                window.RecordUndo("Insert Path Point");
                int index = MotionEditing.InsertPoint(window.Asset.data, segment, fraction, position);
                window.MarkChanged();
                window.Select(index);
                e.Use();
            }
        }

        /// <summary>在屏幕空间找路径上离鼠标最近的点。返回段索引、段内弧长比例和路径空间坐标。</summary>
        static bool FindNearestOnPath(MotionEvaluator eval, Vector2 mouse, out int bestSegment, out float bestFraction, out Vector3 bestPosition)
        {
            bestSegment = -1;
            bestFraction = 0f;
            bestPosition = Vector3.zero;
            float bestDistance = InsertPickDistance;
            const int samples = 48;
            var local = new Vector3[samples + 1];
            var lengths = new float[samples + 1];

            for (int s = 0; s < eval.SegmentCount; s++)
            {
                eval.GetSegmentControls(s, out var p0, out var p1, out var p2, out var p3);
                for (int i = 0; i <= samples; i++)
                {
                    local[i] = PathMath.Bezier(p0, p1, p2, p3, i / (float)samples);
                    lengths[i] = i == 0 ? 0f : lengths[i - 1] + Vector3.Distance(local[i - 1], local[i]);
                }
                float total = lengths[samples];

                for (int i = 0; i < samples; i++)
                {
                    var a = HandleUtility.WorldToGUIPoint(local[i]);
                    var b = HandleUtility.WorldToGUIPoint(local[i + 1]);
                    float t = ProjectOnSegment(mouse, a, b);
                    float d = Vector2.Distance(mouse, Vector2.Lerp(a, b, t));
                    if (d >= bestDistance) continue;
                    bestDistance = d;
                    bestSegment = s;
                    bestPosition = Vector3.Lerp(local[i], local[i + 1], t);
                    float along = Mathf.Lerp(lengths[i], lengths[i + 1], t);
                    bestFraction = total > 0f ? along / total : 0.5f;
                }
            }
            return bestSegment >= 0;
        }

        static float ProjectOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            return len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
        }

        /// <summary>Delete / Backspace 删除选中点，并拦截 Unity 默认的删除对象命令。</summary>
        static void HandleDelete(CurveMotionWindow window)
        {
            if (window.Selected <= 0) return;
            var e = Event.current;
            bool isDelete = e.commandName == "SoftDelete" || e.commandName == "Delete";
            if (e.type == EventType.ValidateCommand && isDelete)
            {
                e.Use();
            }
            else if (e.type == EventType.ExecuteCommand && isDelete)
            {
                window.DeleteSelected();
                e.Use();
            }
        }

        static void DrawPlayhead(CurveMotionWindow window, MotionEvaluator eval, MotionData data)
        {
            if (eval.Duration <= 0f) return;
            var pos = data.points[0].position + eval.EvaluatePosition(window.LocalTime);
            Handles.color = PlayheadColor;
            float size = HandleUtility.GetHandleSize(pos) * 0.09f;
            if (Event.current.type == EventType.Repaint)
                Handles.SphereHandleCap(0, pos, Quaternion.identity, size, EventType.Repaint);
        }
    }
}
