// DOTween 的最小模拟，只实现导出代码用到的 API，用来在没有 Unity 的环境里逐帧回放导出的 Sequence，
// 和编辑器预览（MotionEvaluator）对照数值。不是 DOTween 的替代品，也不要放进包里。
//
// 模拟的行为（按 DOTween 1.2 的实现）：
// - 补间在第一次被更新到时才读取起始值（嵌套在 Sequence 里的，在 Sequence 第一次走到它时读）；
// - Sequence 按开始时间排序子项；前进时依次更新时间范围与 (from, to] 相交的子项，后退时倒序更新；
// - Restart 循环在每轮开始前把所有子项倒回起点，Yoyo 的奇数轮倒着播；
// - DOVirtual.Float 的回调在每次更新（包括倒回）后调用；
// - 每帧按创建顺序更新根补间，第一帧就前进 deltaTime。
using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening.Core;
using Limbo.CurveMotion;
using UnityEngine;

namespace DG.Tweening.Core
{
    public delegate T DOGetter<out T>();
    public delegate void DOSetter<in T>(T pNewValue);
}

namespace DG.Tweening
{
    public enum Ease
    {
        Unset, Linear,
        InSine, OutSine, InOutSine,
        InQuad, OutQuad, InOutQuad,
        InCubic, OutCubic, InOutCubic,
        InQuart, OutQuart, InOutQuart,
        InQuint, OutQuint, InOutQuint,
        InExpo, OutExpo, InOutExpo,
        InCirc, OutCirc, InOutCirc,
        InElastic, OutElastic, InOutElastic,
        InBack, OutBack, InOutBack,
        InBounce, OutBounce, InOutBounce,
    }

    public enum LoopType { Restart, Yoyo, Incremental }

    public delegate void TweenCallback();
    public delegate void TweenCallback<in T>(T value);
    public delegate float EaseFunction(float time, float duration, float overshootOrAmplitude, float period);

    public abstract class Tween
    {
        internal float duration;
        internal int loops = 1;
        internal LoopType loopType;
        internal Ease ease = Ease.OutQuad;
        internal EaseFunction customEase;
        internal bool isPlaying = true;
        internal bool startupDone;
        internal bool isSequenced;
        internal bool isComplete;
        internal bool autoKill = true;
        internal bool active = true;
        internal float fullPosition;
        internal object target;
        internal TweenCallback onComplete, onKill, onUpdate;

        protected Tween() { DOTween.Register(this); }

        internal float FullDuration => loops < 0 ? float.PositiveInfinity : duration * loops;

        internal virtual void Startup() { startupDone = true; }

        /// <summary>单轮内的位置 position（0~duration），backwards 表示倒着走，isRewind 表示 Restart 循环开头的倒回。</summary>
        internal abstract void Apply(float fromPosition, float toPosition, bool isRewind);

        internal float Evaluate(float position)
        {
            if (customEase != null) return customEase(position, duration, 1.70158f, 0f);
            if (duration <= 0f) return 1f;
            return Easing.Evaluate(ToEaseType(ease), position / duration);
        }

        static EaseType ToEaseType(Ease ease)
        {
            if (ease == Ease.Unset) throw new InvalidOperationException("Ease.Unset");
            return (EaseType)Enum.Parse(typeof(EaseType), ease.ToString());
        }

        /// <summary>跳到总位置 to（含循环）。</summary>
        internal void Goto(float to, bool isRewind = false)
        {
            if (!startupDone) Startup();
            float full = FullDuration;
            to = Math.Max(0f, Math.Min(to, full));
            float from = fullPosition;
            fullPosition = to;
            if (duration <= 0f)
            {
                Apply(0f, 0f, isRewind);
                Fire(onUpdate);
                return;
            }

            int fromLoop = LoopIndex(from), toLoop = LoopIndex(to);
            if (toLoop == fromLoop)
            {
                Apply(Local(from, fromLoop), Local(to, toLoop), isRewind);
            }
            else if (toLoop > fromLoop)
            {
                for (int loop = fromLoop; loop < toLoop; loop++)
                {
                    float start = loop == fromLoop ? Local(from, fromLoop) : Edge(loop, true);
                    Apply(start, Edge(loop, false), isRewind);
                    if (loopType == LoopType.Restart) Apply(duration, 0f, true);
                }
                Apply(Edge(toLoop, true), Local(to, toLoop), isRewind);
            }
            else
            {
                // 后退跨轮（只在倒回嵌套循环时出现）：直接回到起点。
                Apply(Local(from, fromLoop), 0f, true);
            }
            Fire(onUpdate);
        }

        int LoopIndex(float position)
        {
            if (position >= FullDuration) return Math.Max(0, (loops < 0 ? int.MaxValue : loops) - 1);
            return (int)Math.Floor(position / duration);
        }

        float Local(float position, int loop)
        {
            float local = Math.Min(duration, position - loop * duration);
            return loopType == LoopType.Yoyo && (loop & 1) == 1 ? duration - local : local;
        }

        /// <summary>第 loop 轮的开始（start = true）或结束位置。</summary>
        float Edge(int loop, bool start)
        {
            bool reversed = loopType == LoopType.Yoyo && (loop & 1) == 1;
            return start != reversed ? 0f : duration;
        }

        internal static void Fire(TweenCallback callback) => callback?.Invoke();

        internal void Kill()
        {
            if (!active) return;
            active = false;
            isPlaying = false;
            Fire(onKill);
        }
    }

    public class Tweener : Tween
    {
        readonly Func<object> m_Get;
        readonly Action<object, object, float> m_Set;
        object m_Start;

        internal Tweener(Func<object> get, Action<object, object, float> set, float duration)
        {
            if (duration <= 0f) throw new InvalidOperationException("导出代码不应产生 0 时长的补间（DOTween 倒放、倒回时不会还原它）");
            m_Get = get;
            m_Set = set;
            this.duration = Math.Max(0f, duration);
        }

        internal override void Startup()
        {
            base.Startup();
            m_Start = m_Get();
        }

        internal override void Apply(float fromPosition, float toPosition, bool isRewind) => m_Set(m_Start, null, Evaluate(toPosition));

        internal static Tweener Create<T>(DOGetter<T> getter, DOSetter<T> setter, T end, float duration, Func<T, T, float, T> lerp)
        {
            return new Tweener(() => getter(), (start, _, k) => setter(lerp((T)start, end, k)), duration);
        }
    }

    public sealed class Sequence : Tween
    {
        sealed class Child
        {
            public Tween tween;
            public TweenCallback callback;
            public float start, end;
        }

        readonly List<Child> m_Children = new List<Child>();

        internal Sequence() { ease = Ease.Linear; }

        internal void Add(float at, Tween tween)
        {
            if (startupDone) throw new InvalidOperationException("Sequence 已开始，不能再插入");
            tween.isSequenced = true;
            if (tween.loops < 0) tween.loops = 1;
            m_Children.Add(new Child { tween = tween, start = at, end = at + tween.FullDuration });
            duration = Math.Max(duration, at + tween.FullDuration);
        }

        internal void AddCallback(float at, TweenCallback callback)
        {
            if (startupDone) throw new InvalidOperationException("Sequence 已开始，不能再插入");
            m_Children.Add(new Child { callback = callback, start = at, end = at });
            duration = Math.Max(duration, at);
        }

        internal void AddInterval(float interval) => duration += interval;

        internal override void Startup()
        {
            base.Startup();
            if (ease != Ease.Linear || customEase != null) throw new InvalidOperationException("导出的 Sequence 应该是 Linear");
            // 稳定排序：同时开始的保持插入顺序。
            var ordered = new List<Child>(m_Children);
            var index = new Dictionary<Child, int>();
            for (int i = 0; i < ordered.Count; i++) index[ordered[i]] = i;
            ordered.Sort((a, b) => a.start != b.start ? a.start.CompareTo(b.start) : index[a].CompareTo(index[b]));
            m_Children.Clear();
            m_Children.AddRange(ordered);
        }

        internal override void Apply(float fromPosition, float toPosition, bool isRewind)
        {
            if (toPosition < fromPosition || isRewind && toPosition <= fromPosition)
            {
                for (int i = m_Children.Count - 1; i >= 0; i--)
                {
                    var c = m_Children[i];
                    if (c.end < toPosition || c.start > fromPosition) continue;
                    if (c.callback != null) continue;
                    if (!c.tween.startupDone) continue;
                    c.tween.Goto(Math.Max(0f, toPosition - c.start), isRewind);
                }
                return;
            }

            foreach (var c in m_Children)
            {
                if (c.start > toPosition || c.start > 0f && c.end <= fromPosition || c.start <= 0f && c.end < fromPosition) continue;
                if (c.callback != null)
                {
                    // 前进经过（或刚好到达）回调时间时触发；同一位置不重复触发。
                    if (c.start > fromPosition || c.start == 0f && fromPosition == 0f) Fire(c.callback);
                    continue;
                }
                float local = Math.Max(0f, toPosition - c.start);
                if (toPosition >= c.end) local = c.tween.FullDuration;
                c.tween.Goto(local, isRewind);
            }
        }
    }

    public static class DOTween
    {
        static readonly List<Tween> s_Tweens = new List<Tween>();

        internal static void Register(Tween tween) => s_Tweens.Add(tween);

        /// <summary>清空所有补间（每个测试开始前调用）。</summary>
        public static void Reset() => s_Tweens.Clear();

        /// <summary>模拟一帧：按创建顺序更新所有在播放的根补间。</summary>
        public static void Step(float deltaTime)
        {
            var snapshot = new List<Tween>(s_Tweens);
            foreach (var t in snapshot)
            {
                if (!t.active || t.isSequenced || !t.isPlaying || t.isComplete) continue;
                t.Goto(t.fullPosition + deltaTime);
                if (t.fullPosition >= t.FullDuration)
                {
                    t.isComplete = true;
                    t.isPlaying = false;
                    Tween.Fire(t.onComplete);
                    if (t.autoKill) t.Kill();
                }
            }
            s_Tweens.RemoveAll(t => !t.active);
        }

        /// <summary>还在运行的根补间个数（不含嵌套在 Sequence 里的）。</summary>
        public static int ActiveCount => s_Tweens.Count(t => t.active && !t.isSequenced);

        public static Sequence Sequence() => new Sequence();

        public static Tweener To(DOGetter<float> getter, DOSetter<float> setter, float endValue, float duration) =>
            Tweener.Create(getter, setter, endValue, duration, (a, b, k) => a + (b - a) * k);

        public static Tweener To(DOGetter<Vector2> getter, DOSetter<Vector2> setter, Vector2 endValue, float duration) =>
            Tweener.Create(getter, setter, endValue, duration, (a, b, k) => a + (b - a) * k);

        public static Tweener To(DOGetter<Vector3> getter, DOSetter<Vector3> setter, Vector3 endValue, float duration) =>
            Tweener.Create(getter, setter, endValue, duration, (a, b, k) => a + (b - a) * k);

        public static Tweener To(DOGetter<Vector4> getter, DOSetter<Vector4> setter, Vector4 endValue, float duration) =>
            Tweener.Create(getter, setter, endValue, duration, (a, b, k) => a + (b - a) * k);
    }

    public static class DOVirtual
    {
        public static Tweener Float(float from, float to, float duration, TweenCallback<float> onVirtualUpdate)
        {
            float value = from;
            var t = DOTween.To(() => value, x => value = x, to, duration);
            t.onUpdate = () => onVirtualUpdate(value);
            return t;
        }

        public static float EasedValue(float from, float to, float lifetimePercentage, Ease easeType)
        {
            if (easeType == Ease.Unset) throw new InvalidOperationException("Ease.Unset");
            var type = (EaseType)Enum.Parse(typeof(EaseType), easeType.ToString());
            return from + (to - from) * Easing.Evaluate(type, lifetimePercentage);
        }
    }

    public static class ShortcutExtensions
    {
        public static Tweener DOScale(this Transform target, Vector3 endValue, float duration) =>
            DOTween.To(() => target.localScale, x => target.localScale = x, endValue, duration);
    }

    public static class TweenSettingsExtensions
    {
        public static T SetEase<T>(this T t, Ease ease) where T : Tween { t.ease = ease; t.customEase = null; return t; }
        public static T SetEase<T>(this T t, EaseFunction customEase) where T : Tween { t.customEase = customEase; return t; }
        public static T SetLoops<T>(this T t, int loops, LoopType loopType) where T : Tween { t.loops = loops; t.loopType = loopType; return t; }
        public static T SetTarget<T>(this T t, object target) where T : Tween { t.target = target; return t; }
        public static T SetLink<T>(this T t, GameObject gameObject) where T : Tween => t;
        public static T SetAutoKill<T>(this T t, bool autoKill) where T : Tween { t.autoKill = autoKill; return t; }
        public static T OnComplete<T>(this T t, TweenCallback action) where T : Tween { t.onComplete = action; return t; }
        public static T OnKill<T>(this T t, TweenCallback action) where T : Tween { t.onKill = action; return t; }

        public static Sequence Insert(this Sequence s, float atPosition, Tween t) { s.Add(atPosition, t); return s; }
        public static Sequence InsertCallback(this Sequence s, float atPosition, TweenCallback callback) { s.AddCallback(atPosition, callback); return s; }
        public static Sequence AppendInterval(this Sequence s, float interval) { s.AddInterval(interval); return s; }
    }

    public static class TweenExtensions
    {
        public static T Pause<T>(this T t) where T : Tween { t.isPlaying = false; return t; }
        public static T Play<T>(this T t) where T : Tween { if (t.active && !t.isComplete) t.isPlaying = true; return t; }
        public static bool IsPlaying(this Tween t) => t.active && t.isPlaying;
        public static void Kill(this Tween t, bool complete = false) => t.Kill();
        public static float Duration(this Tween t, bool includeLoops = true) => includeLoops ? t.FullDuration : t.duration;
    }
}
