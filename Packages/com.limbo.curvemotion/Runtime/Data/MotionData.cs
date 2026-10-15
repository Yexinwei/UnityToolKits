using System;
using System.Collections.Generic;
using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>动效作用的对象类型，决定位置单位与导出的 DOTween API。</summary>
    public enum TargetKind
    {
        /// <summary>UI，位置为 anchoredPosition，单位像素。</summary>
        RectTransform,
        /// <summary>场景物体，位置为 localPosition，单位 Unity 单位。</summary>
        Transform,
    }

    /// <summary>路径点类型，决定该点两侧切线如何计算。</summary>
    public enum PointType
    {
        /// <summary>尖角：两侧无切线，相邻段在此点折线相接。</summary>
        Corner,
        /// <summary>平滑：只编辑出切线，入切线自动镜像。</summary>
        Smooth,
        /// <summary>自由：入、出切线分别编辑。</summary>
        Free,
        /// <summary>自动：按前后点自动计算切线（类 Catmull-Rom）。</summary>
        Auto,
    }

    /// <summary>路径段的计时方式。</summary>
    public enum TimingMode
    {
        /// <summary>直接填时长。</summary>
        Duration,
        /// <summary>填速度，时长 = 段弧长 / 速度。</summary>
        Speed,
    }

    public enum MotionLoopType
    {
        Restart,
        Yoyo,
    }

    /// <summary>
    /// 一段曲线运动的完整数据：路径 + 属性轨道 + 事件点 + 循环。
    /// 所有位置都是相对起点的偏移，同一份数据可以套用到任意对象。
    /// </summary>
    [Serializable]
    public class MotionData
    {
        public string name = "Motion";
        [TextArea] public string description;
        public TargetKind target = TargetKind.RectTransform;

        /// <summary>路径点。第一个点是起点，位置通常为零。</summary>
        public List<PathPoint> points = new List<PathPoint>();

        [SerializeReference] public List<MotionTrack> tracks = new List<MotionTrack>();

        public List<MotionEvent> events = new List<MotionEvent>();

        public LoopSettings loop = new LoopSettings();

        /// <summary>终点绑定：终点在运行时对准另一个对象，可以跨 UI 与 3D（例如 3D 物体飞到 UI 图标上）。</summary>
        public EndTarget endTarget = new EndTarget();

        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

        public int FindPointIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < points.Count; i++)
                if (points[i].id == id) return i;
            return -1;
        }

        public PathPoint AddPoint(Vector3 position, PointType type = PointType.Auto)
        {
            var point = new PathPoint { id = NewId(), position = position, type = type };
            points.Add(point);
            return point;
        }
    }

    [Serializable]
    public class PathPoint
    {
        /// <summary>稳定 id，属性片段与事件点通过它锚定，插入/删除其他点不影响。</summary>
        public string id;
        public Vector3 position;
        public PointType type = PointType.Auto;

        /// <summary>入切线（相对本点的偏移）。仅 Free 类型使用存储值。</summary>
        public Vector3 inTangent;

        /// <summary>出切线（相对本点的偏移）。Smooth、Free 类型使用存储值。</summary>
        public Vector3 outTangent;

        /// <summary>从本点到下一个点的路径段设置。最后一个点的此项不使用。</summary>
        public SegmentSettings segment = new SegmentSettings();
    }

    [Serializable]
    public class SegmentSettings
    {
        public TimingMode timing = TimingMode.Duration;
        [Min(0f)] public float duration = 0.5f;
        [Min(0f)] public float speed = 1f;
        public EaseSpec ease = new EaseSpec { type = EaseType.Linear };
        public string desc;
    }

    /// <summary>事件点。v1 导出为一条日志占位，接入正式项目后替换为实际回调。</summary>
    [Serializable]
    public class MotionEvent
    {
        public string anchor;
        public float offset;
        public string name = "Event";
        public string message;
    }

    /// <summary>
    /// 终点绑定。bound 为 true 时，最后一个路径点只是设计时的参考位置：运行时换成落点对象的实际位置，
    /// 起点不动，整条路径按走过的弧长比例逐渐修正过去（见 <see cref="MotionEvaluator.EvaluatePosition(float, Vector3)"/>）。
    /// 落点对象是场景里的东西，不存进数据，由编辑器预览或运行时代码传入。
    /// </summary>
    [Serializable]
    public class EndTarget
    {
        public bool bound;

        /// <summary>落点对象的类型。与 <see cref="MotionData.target"/> 不同时，需要经过屏幕坐标换算。</summary>
        public TargetKind kind = TargetKind.RectTransform;

        /// <summary>写给人和 AI 看的落点说明，例如“顶部金币栏图标”。</summary>
        public string hint;
    }

    [Serializable]
    public class LoopSettings
    {
        /// <summary>播放次数。1 = 不循环，-1 = 无限循环。</summary>
        public int count = 1;
        public MotionLoopType type = MotionLoopType.Restart;
    }
}
