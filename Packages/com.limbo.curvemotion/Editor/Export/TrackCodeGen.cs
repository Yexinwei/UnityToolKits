using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Limbo.CurveMotion.Editor.Export
{
    /// <summary>
    /// 一种属性轨道的 DOTween 代码生成器。新增轨道类型时写一个子类并加上 <see cref="TrackCodeGenAttribute"/>，导出器会自动发现。
    /// 生成器只负责“怎么读、怎么写、怎么补间这个属性”；片段的时间、截断、循环由导出器统一处理。
    /// </summary>
    public abstract class TrackCodeGen
    {
        /// <summary>为一条轨道做准备（取组件、声明变量、登记辅助方法），返回读写这个属性的方式。</summary>
        public abstract TrackBinding Bind(DotweenCodeContext ctx, MotionTrack track);
    }

    /// <summary>标记 <see cref="TrackCodeGen"/> 负责的轨道类型。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TrackCodeGenAttribute : Attribute
    {
        public Type TrackType { get; }
        public TrackCodeGenAttribute(Type trackType) { TrackType = trackType; }
    }

    /// <summary>
    /// 生成代码里读写一个属性的方式。值的类型由轨道维度决定（float、Vector2~Vector4）。
    /// </summary>
    public sealed class TrackBinding
    {
        /// <summary>当前值的表达式（DOTween 在片段开始时读它作为起始值）。</summary>
        public string Read;

        /// <summary>把值写回对象的语句（不带分号），参数是值表达式。</summary>
        public Func<string, string> Write;

        /// <summary>播放开始时的初始值表达式；第一次用到时才声明变量（片段目标值按它算倍率、增量）。</summary>
        public Func<string> Initial;

        /// <summary>从当前值补间到 to 的 DOTween 表达式，参数为 (to, duration)。为空时用 DOTween.To(() =&gt; Read, v =&gt; Write(v), to, duration)。</summary>
        public Func<string, string, string> Tween;
    }

    /// <summary>生成 Play() 方法时的共享状态：变量、using、辅助方法，按名字去重。</summary>
    public sealed class DotweenCodeContext
    {
        readonly List<string> m_Declarations = new List<string>();
        readonly HashSet<string> m_Names = new HashSet<string>();
        readonly SortedSet<string> m_Usings = new SortedSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> m_Helpers = new Dictionary<string, string>();
        readonly List<string> m_HelperOrder = new List<string>();

        public DotweenCodeContext(TargetKind kind) { Kind = kind; }

        /// <summary>动效对象的类型：RectTransform（UI）或 Transform。</summary>
        public TargetKind Kind { get; }

        public bool IsUi => Kind == TargetKind.RectTransform;

        /// <summary>Play() 里动效对象参数的变量名。</summary>
        public string Target => "target";

        public IEnumerable<string> Usings => m_Usings;
        public IReadOnlyList<string> Declarations => m_Declarations;

        public IEnumerable<string> Helpers
        {
            get { foreach (var key in m_HelperOrder) yield return m_Helpers[key]; }
        }

        public void Using(string ns) => m_Usings.Add(ns);

        /// <summary>声明 Play() 开头的局部变量 var name = expression;。同名的只声明一次，返回变量名。</summary>
        public string Declare(string name, string expression)
        {
            if (m_Names.Add(name)) m_Declarations.Add("var " + name + " = " + expression + ";");
            return name;
        }

        /// <summary>取一个没用过的变量名：rotation、rotation2、rotation3……</summary>
        public string UniqueName(string baseName)
        {
            if (!m_Names.Contains(baseName)) return baseName;
            for (int i = 2; ; i++)
                if (!m_Names.Contains(baseName + i)) return baseName + i;
        }

        /// <summary>登记一个类成员（辅助方法、静态字段），同 key 只写一次。</summary>
        public void Helper(string key, string code)
        {
            if (m_Helpers.ContainsKey(key)) return;
            m_Helpers.Add(key, code);
            m_HelperOrder.Add(key);
        }
    }

    /// <summary>按轨道类型找代码生成器：扫描所有带 <see cref="TrackCodeGenAttribute"/> 的 <see cref="TrackCodeGen"/> 子类。</summary>
    public static class TrackCodeGenRegistry
    {
        static Dictionary<Type, TrackCodeGen> s_ByTrack;

        public static bool TryGet(Type trackType, out TrackCodeGen gen)
        {
            EnsureLoaded();
            for (var t = trackType; t != null && t != typeof(MotionTrack); t = t.BaseType)
                if (s_ByTrack.TryGetValue(t, out gen)) return true;
            gen = null;
            return false;
        }

        static void EnsureLoaded()
        {
            if (s_ByTrack != null) return;
            var map = new Dictionary<Type, TrackCodeGen>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || !typeof(TrackCodeGen).IsAssignableFrom(type)) continue;
                    var attr = type.GetCustomAttribute<TrackCodeGenAttribute>();
                    if (attr == null || attr.TrackType == null || map.ContainsKey(attr.TrackType)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                    map.Add(attr.TrackType, (TrackCodeGen)Activator.CreateInstance(type));
                }
            }
            s_ByTrack = map;
        }
    }
}
