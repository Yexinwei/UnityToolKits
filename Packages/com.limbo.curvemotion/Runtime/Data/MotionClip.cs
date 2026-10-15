using System;
using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>
    /// 属性轨道上的一个变化片段。开始时间 = 锚点到达时间 + offset，
    /// 所以修改路径段时长时，片段会跟着锚点移动。
    /// </summary>
    [Serializable]
    public class MotionClip
    {
        /// <summary>锚定的路径点 id。</summary>
        public string anchor;
        public float offset;
        [Min(0f)] public float duration = 0.3f;
        public EaseSpec ease = new EaseSpec();

        /// <summary>目标值，含义由所属轨道决定（倍率、增量或绝对值）。未使用的分量忽略。</summary>
        public Vector4 value;

        public string desc;
    }
}
