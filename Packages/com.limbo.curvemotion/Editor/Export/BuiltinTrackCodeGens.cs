namespace Limbo.CurveMotion.Editor.Export
{
    /// <summary>缩放：target.DOScale，起始值由 DOTween 在片段开始时读取。</summary>
    [TrackCodeGen(typeof(ScaleTrack))]
    public sealed class ScaleCodeGen : TrackCodeGen
    {
        public override TrackBinding Bind(DotweenCodeContext ctx, MotionTrack track)
        {
            string t = ctx.Target;
            string initial = ctx.UniqueName("scale0");
            return new TrackBinding
            {
                Read = t + ".localScale",
                Write = v => t + ".localScale = " + v,
                Initial = () => ctx.Declare(initial, t + ".localScale"),
                Tween = (to, duration) => t + ".DOScale(" + to + ", " + duration + ")",
            };
        }
    }

    /// <summary>
    /// 旋转：欧拉角增量。用一个变量记住当前欧拉角再补间，不读 localEulerAngles（它会被规范到 0~360），
    /// 这样转 360° 之后再转回来也和预览一致。
    /// </summary>
    [TrackCodeGen(typeof(RotationTrack))]
    public sealed class RotationCodeGen : TrackCodeGen
    {
        public override TrackBinding Bind(DotweenCodeContext ctx, MotionTrack track)
        {
            string t = ctx.Target;
            string initial = ctx.Declare(ctx.UniqueName("rotation0"), t + ".localEulerAngles");
            string current = ctx.Declare(ctx.UniqueName("rotation"), initial);
            return new TrackBinding
            {
                Read = current,
                Write = v => t + ".localEulerAngles = " + current + " = " + v,
                Initial = () => initial,
            };
        }
    }

    /// <summary>透明度：UI 先找 CanvasGroup 再找 Graphic（含 TextMeshPro），场景物体用 SpriteRenderer，与预览一致。</summary>
    [TrackCodeGen(typeof(AlphaTrack))]
    public sealed class AlphaCodeGen : TrackCodeGen
    {
        public override TrackBinding Bind(DotweenCodeContext ctx, MotionTrack track)
        {
            string t = ctx.Target;
            string args;
            if (ctx.IsUi)
            {
                ctx.Using("UnityEngine.UI");
                args = ctx.Declare("canvasGroup", t + ".GetComponent<CanvasGroup>()") + ", " + ctx.Declare("graphic", t + ".GetComponent<Graphic>()");
                ctx.Helper("alpha-ui",
@"    static float GetAlpha(CanvasGroup group, Graphic graphic) => group != null ? group.alpha : graphic != null ? graphic.color.a : 1f;

    static void SetAlpha(CanvasGroup group, Graphic graphic, float alpha)
    {
        if (group != null) { group.alpha = alpha; return; }
        if (graphic == null) return;
        var color = graphic.color;
        color.a = alpha;
        graphic.color = color;
    }");
            }
            else
            {
                args = ctx.Declare("sprite", t + ".GetComponent<SpriteRenderer>()");
                ctx.Helper("alpha-sprite",
@"    static float GetAlpha(SpriteRenderer sprite) => sprite != null ? sprite.color.a : 1f;

    static void SetAlpha(SpriteRenderer sprite, float alpha)
    {
        if (sprite == null) return;
        var color = sprite.color;
        color.a = alpha;
        sprite.color = color;
    }");
            }
            string initial = ctx.UniqueName("alpha0");
            return new TrackBinding
            {
                Read = "GetAlpha(" + args + ")",
                Write = v => "SetAlpha(" + args + ", " + v + ")",
                Initial = () => ctx.Declare(initial, "GetAlpha(" + args + ")"),
            };
        }
    }

    /// <summary>颜色 RGB（不动透明度）：UI 用 Graphic，场景物体用 SpriteRenderer。</summary>
    [TrackCodeGen(typeof(ColorTrack))]
    public sealed class ColorCodeGen : TrackCodeGen
    {
        public override TrackBinding Bind(DotweenCodeContext ctx, MotionTrack track)
        {
            string t = ctx.Target;
            string arg;
            if (ctx.IsUi)
            {
                ctx.Using("UnityEngine.UI");
                arg = ctx.Declare("graphic", t + ".GetComponent<Graphic>()");
                ctx.Helper("rgb-ui",
@"    static Vector3 GetRgb(Graphic graphic) => graphic != null ? new Vector3(graphic.color.r, graphic.color.g, graphic.color.b) : Vector3.one;

    static void SetRgb(Graphic graphic, Vector3 rgb)
    {
        if (graphic != null) graphic.color = new Color(rgb.x, rgb.y, rgb.z, graphic.color.a);
    }");
            }
            else
            {
                arg = ctx.Declare("sprite", t + ".GetComponent<SpriteRenderer>()");
                ctx.Helper("rgb-sprite",
@"    static Vector3 GetRgb(SpriteRenderer sprite) => sprite != null ? new Vector3(sprite.color.r, sprite.color.g, sprite.color.b) : Vector3.one;

    static void SetRgb(SpriteRenderer sprite, Vector3 rgb)
    {
        if (sprite != null) sprite.color = new Color(rgb.x, rgb.y, rgb.z, sprite.color.a);
    }");
            }
            string initial = ctx.UniqueName("color0");
            return new TrackBinding
            {
                Read = "GetRgb(" + arg + ")",
                Write = v => "SetRgb(" + arg + ", " + v + ")",
                Initial = () => ctx.Declare(initial, "GetRgb(" + arg + ")"),
            };
        }
    }
}
