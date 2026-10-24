using System.Collections.Generic;
using UnityEngine;

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// 在场景里的真实对象上预览动效：开始时记下对象初始状态，结束时原样还原。
    /// 路径位置写 localPosition（UI 写 anchoredPosition），属性轨道交给各轨道的 Read/WriteValue。
    /// </summary>
    internal sealed class MotionPreview
    {
        Transform m_Target;
        RectTransform m_Rect;
        Vector3 m_StartLocalPosition;
        Vector2 m_StartAnchoredPosition;
        Vector3 m_StartScale;
        Vector3 m_StartEuler;
        readonly Dictionary<MotionTrack, Vector4> m_TrackInitial = new Dictionary<MotionTrack, Vector4>();
        readonly List<string> m_Unsupported = new List<string>();
        readonly List<IState> m_Saved = new List<IState>();

        public bool IsActive => m_Target != null;
        public Transform Target => m_Target;

        /// <summary>预览开始时没有对应组件、无法预览的轨道名。</summary>
        public IReadOnlyList<string> UnsupportedTracks => m_Unsupported;

        public void Begin(Transform target, MotionData data)
        {
            if (target == m_Target) return;
            End();
            if (target == null) return;

            m_Target = target;
            m_Rect = target as RectTransform;
            m_StartLocalPosition = target.localPosition;
            if (m_Rect != null) m_StartAnchoredPosition = m_Rect.anchoredPosition;
            m_StartScale = target.localScale;
            m_StartEuler = target.localEulerAngles;
            SaveComponents(target.gameObject);
            CaptureTracks(data);
        }

        /// <summary>数据里增删了轨道后调用，补记新轨道的初始值。</summary>
        public void CaptureTracks(MotionData data)
        {
            if (m_Target == null) return;
            // 用开始预览时的状态作为初始值：先还原再读取，避免读到预览中途的值。
            RestoreState();
            m_TrackInitial.Clear();
            m_Unsupported.Clear();
            foreach (var track in data.tracks)
            {
                if (track == null) continue;
                if (track.TryReadValue(m_Target.gameObject, out var value)) m_TrackInitial[track] = value;
                else m_Unsupported.Add(track.name);
            }
        }

        public void Apply(MotionEvaluator eval, float localTime)
        {
            if (m_Target == null) return;
            var offset = eval.EvaluatePosition(localTime);
            if (m_Rect != null) m_Rect.anchoredPosition = m_StartAnchoredPosition + new Vector2(offset.x, offset.y);
            else m_Target.localPosition = m_StartLocalPosition + offset;

            var tracks = eval.Data.tracks;
            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                if (track == null || !m_TrackInitial.TryGetValue(track, out var initial)) continue;
                track.WriteValue(m_Target.gameObject, eval.EvaluateTrack(i, localTime, initial));
            }
        }

        public void End()
        {
            if (m_Target == null) return;
            RestoreState();
            m_Target = null;
            m_Rect = null;
            m_TrackInitial.Clear();
            m_Unsupported.Clear();
            m_Saved.Clear();
        }

        /// <summary>路径在场景中的坐标系：父节点空间，原点在对象的初始本地位置。</summary>
        public static Matrix4x4 GetPathMatrix(Transform target, Vector3 startLocalPosition)
        {
            var parent = target != null ? target.parent : null;
            var parentMatrix = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
            return parentMatrix * Matrix4x4.Translate(startLocalPosition);
        }

        /// <summary>预览中用记下的初始位置，否则用对象当前位置。</summary>
        public Matrix4x4 GetPathMatrix(Transform target)
        {
            if (target == null) return Matrix4x4.identity;
            return GetPathMatrix(target, GetStartLocalPosition(target));
        }

        /// <summary>对象的起始 localPosition：预览中是开始预览时记下的值，否则是当前值。</summary>
        public Vector3 GetStartLocalPosition(Transform target)
        {
            if (target == null) return Vector3.zero;
            return target == m_Target ? m_StartLocalPosition : target.localPosition;
        }

        void RestoreState()
        {
            if (m_Target == null) return;
            if (m_Rect != null) m_Rect.anchoredPosition = m_StartAnchoredPosition;
            m_Target.localPosition = m_StartLocalPosition;
            m_Target.localScale = m_StartScale;
            m_Target.localEulerAngles = m_StartEuler;
            foreach (var state in m_Saved) state.Restore();
        }

        void SaveComponents(GameObject go)
        {
            m_Saved.Clear();
            var group = go.GetComponent<CanvasGroup>();
            if (group != null) m_Saved.Add(new CanvasGroupState(group));
            var sprite = go.GetComponent<SpriteRenderer>();
            if (sprite != null) m_Saved.Add(new SpriteState(sprite));
#if CURVEMOTION_UGUI
            var graphic = go.GetComponent<UnityEngine.UI.Graphic>();
            if (graphic != null) m_Saved.Add(new GraphicState(graphic));
#endif
        }

        interface IState { void Restore(); }

        sealed class CanvasGroupState : IState
        {
            readonly CanvasGroup m_Group;
            readonly float m_Alpha;
            public CanvasGroupState(CanvasGroup g) { m_Group = g; m_Alpha = g.alpha; }
            public void Restore() { if (m_Group != null) m_Group.alpha = m_Alpha; }
        }

        sealed class SpriteState : IState
        {
            readonly SpriteRenderer m_Sprite;
            readonly Color m_Color;
            public SpriteState(SpriteRenderer s) { m_Sprite = s; m_Color = s.color; }
            public void Restore() { if (m_Sprite != null) m_Sprite.color = m_Color; }
        }

#if CURVEMOTION_UGUI
        sealed class GraphicState : IState
        {
            readonly UnityEngine.UI.Graphic m_Graphic;
            readonly Color m_Color;
            public GraphicState(UnityEngine.UI.Graphic g) { m_Graphic = g; m_Color = g.color; }
            public void Restore() { if (m_Graphic != null) m_Graphic.color = m_Color; }
        }
#endif
    }
}
