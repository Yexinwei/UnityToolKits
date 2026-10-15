using System;
using System.Collections.Generic;
using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>片段目标值如何与对象的初始值结合。</summary>
    public enum ValueMode
    {
        /// <summary>目标 = 初始值 × 片段值（逐分量），如缩放倍率。</summary>
        Multiply,
        /// <summary>目标 = 初始值 + 片段值，如旋转增量。</summary>
        Add,
        /// <summary>目标 = 片段值，如透明度、颜色。</summary>
        Absolute,
    }

    /// <summary>
    /// 标记一个属性轨道类型。typeId 写入 JSON，发布后不要改。
    /// 新增属性只需写一个带此特性的 <see cref="MotionTrack"/> 子类，编辑器与导出器会自动发现。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class MotionTrackAttribute : Attribute
    {
        public string Id { get; }
        public string DisplayName { get; }

        public MotionTrackAttribute(string typeId, string displayName)
        {
            Id = typeId;
            DisplayName = displayName;
        }
    }

    /// <summary>
    /// 属性轨道基类。片段值统一存为 Vector4，只使用前 <see cref="Dimension"/> 个分量。
    /// </summary>
    [Serializable]
    public abstract class MotionTrack
    {
        public string name;
        public bool enabled = true;
        public List<MotionClip> clips = new List<MotionClip>();

        /// <summary>值的有效分量数（1~4）。</summary>
        public abstract int Dimension { get; }

        public abstract ValueMode Mode { get; }

        /// <summary>新建片段时的默认值。</summary>
        public abstract Vector4 DefaultValue { get; }

        /// <summary>分量名，用于 Inspector 与 JSON 描述。</summary>
        public virtual string[] ComponentNames => new[] { "x", "y", "z", "w" };

        public string TypeId => MotionTrackRegistry.GetTypeId(GetType());

        public string DisplayName => MotionTrackRegistry.GetDisplayName(GetType());

        public virtual Vector4 ResolveTarget(Vector4 initial, Vector4 value)
        {
            switch (Mode)
            {
                case ValueMode.Multiply: return Vector4.Scale(initial, value);
                case ValueMode.Add: return initial + value;
                default: return value;
            }
        }

        public MotionClip AddClip(string anchor, float offset, float duration)
        {
            var clip = new MotionClip { anchor = anchor, offset = offset, duration = duration, value = DefaultValue };
            clips.Add(clip);
            return clip;
        }
    }
}
