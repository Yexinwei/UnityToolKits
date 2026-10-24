using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Limbo.CurveMotion
{
    /// <summary>
    /// 标记轨道子类里的一个设置字段（例如透明度作用在哪个组件上）。窗口选中轨道时显示，JSON 写在轨道的 settings 里。
    /// 字段要能被 Unity 序列化（public 或 [SerializeField]），类型限枚举、bool、int、float、string；字段名写进 JSON，发布后不要改。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class TrackSettingAttribute : Attribute
    {
        public string Label { get; }
        public string Tooltip { get; }

        public TrackSettingAttribute(string label, string tooltip = null)
        {
            Label = label;
            Tooltip = tooltip;
        }
    }

    /// <summary>找轨道类型上带 <see cref="TrackSettingAttribute"/> 的字段。</summary>
    public static class MotionTrackSettings
    {
        static readonly Dictionary<Type, FieldInfo[]> s_Cache = new Dictionary<Type, FieldInfo[]>();

        public static IReadOnlyList<FieldInfo> Fields(Type trackType)
        {
            if (s_Cache.TryGetValue(trackType, out var cached)) return cached;
            var list = new List<FieldInfo>();
            // 从基类往下，字段按声明顺序。
            var chain = new List<Type>();
            for (var t = trackType; t != null && t != typeof(MotionTrack) && t != typeof(object); t = t.BaseType) chain.Insert(0, t);
            foreach (var t in chain)
            {
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                foreach (var f in fields)
                    if (f.IsDefined(typeof(TrackSettingAttribute), true) && IsSupported(f.FieldType)
                        && (f.IsPublic || f.IsDefined(typeof(SerializeField), true)) && !f.IsNotSerialized)
                        list.Add(f);
            }
            var result = list.ToArray();
            s_Cache[trackType] = result;
            return result;
        }

        public static TrackSettingAttribute Attribute(FieldInfo field) =>
            (TrackSettingAttribute)System.Attribute.GetCustomAttribute(field, typeof(TrackSettingAttribute), true);

        static bool IsSupported(Type type) =>
            type.IsEnum || type == typeof(bool) || type == typeof(int) || type == typeof(float) || type == typeof(string);
    }
}
