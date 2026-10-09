using Godot;

namespace DokiDokiMerchant;

internal enum StampKind { Love, Bad, Deal, Info }

/// <summary>
/// The juice (style.css .stamp/.heart-pop/.vignette/#stage.jolt): big rotated judgement stamps with a sub-pill,
/// ray burst and flying hearts; floating "+8 ♥" pops; level-up/down screen vignettes; the screen jolt.
/// Everything is built from stock nodes, animated with tweens, and ignores the mouse.
/// </summary>
internal sealed class Fx
{
    private const Control.MouseFilterEnum Ignore = Control.MouseFilterEnum.Ignore;
    private static readonly Vector2 StampAt = new(1290, 330);

    private readonly Control _parent;
    private readonly Control _stageRoot;
    private readonly RandomNumberGenerator _rng = new();

    private Control? _stamp;
    private Tween? _stampTween;
    private bool _jolting;

    private static Shader? _burstShader, _vignetteShader;

    /// <param name="parent">ShopStage.Fx.</param>
    /// <param name="stageRoot">ShopStage.Root, shaken by Jolt.</param>
    public Fx(Control parent, Control stageRoot)
    {
        _parent = parent;
        _stageRoot = stageRoot;
        _rng.Randomize();
    }

    private bool Alive => GodotObject.IsInstanceValid(_parent) && _parent.IsInsideTree();

    // ------------------------------------------------------------------------------------------------ stamp

    private readonly record struct StampStyle(Color Text, Color Stroke, Color Rays, Color Sub, int Size);

    private static StampStyle StyleFor(StampKind kind) => kind switch
    {
        StampKind.Love => new(new Color("ffd6ea"), new Color("c2185b"), new Color(1f, 110 / 255f, 175 / 255f, 0.85f), Theme.Plum, 108),
        StampKind.Bad => new(new Color("ffe0d6"), new Color("8b0000"), new Color(1f, 60 / 255f, 60 / 255f, 0.85f), new Color("b00020"), 108),
        StampKind.Deal => new(new Color("fff3a8"), new Color("7a4a00"), new Color(1f, 215 / 255f, 90 / 255f, 0.9f), new Color("8a5a00"), 108),
        _ => new(Colors.White, new Color("4a2a5a"), new Color(1f, 1f, 1f, 0.5f), new Color("4a2a5a"), 84),
    };

    /// <summary>e.g. Stamp("CRITICAL FLIRT!", StampKind.Love, "+8 ♥"). Centred at (1290, 330) like the web.
    /// A Bad stamp also jolts the stage (as the web does).</summary>
    public void Stamp(string text, StampKind kind, string? sub = null)
    {
        if (!Alive) return;
        DismissStamp();

        var st = StyleFor(kind);
        var root = new Control { Name = "Stamp", Position = StampAt, MouseFilter = Ignore };
        _parent.AddChild(root);
        _stamp = root;

        // Ray burst behind everything, centred on the whole stamp.
        var burst = new ColorRect
        {
            MouseFilter = Ignore,
            Color = Colors.White,
            Size = new Vector2(900, 900),
            Position = new Vector2(-450, -450),
            PivotOffset = new Vector2(450, 450),
            Material = new ShaderMaterial { Shader = BurstShader() },
        };
        ((ShaderMaterial)burst.Material).SetShaderParameter("ray_color", st.Rays);
        root.AddChild(burst);

        // Text and sub pill, stacked and centred on the stamp origin (CSS translate(-50%, -50%)).
        // Labels are measured only once they are in the tree: out of it, their theme overrides (font, size) are
        // not applied yet and GetMinimumSize reports the default 16px font.
        var label = Theme.MakeLabel(text, st.Size, st.Text, bold: true, outline: 12, outlineColor: st.Stroke);
        label.AddThemeColorOverride("font_shadow_color", new Color("3a0c2a"));
        label.AddThemeConstantOverride("shadow_offset_x", 0);
        label.AddThemeConstantOverride("shadow_offset_y", 10);
        label.AddThemeConstantOverride("shadow_outline_size", 12);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(label);
        var textSize = Measure(label);
        label.Size = textSize;

        Control? pill = null;
        var pillSize = Vector2.Zero;
        const float gap = 18;
        if (!string.IsNullOrEmpty(sub))
        {
            var panel = new Panel { MouseFilter = Ignore };
            panel.AddThemeStyleboxOverride("panel", Theme.Box(Colors.White, st.Sub, 4, 30));
            root.AddChild(panel);
            var subLabel = Theme.MakeLabel(sub, 38, st.Sub, bold: true, outline: 0);
            panel.AddChild(subLabel);
            var ls = Measure(subLabel);
            subLabel.Size = ls;
            subLabel.Position = new Vector2(30, 12);   // 26/8 padding + 4px border
            pillSize = ls + new Vector2(60, 24);
            panel.Size = pillSize;
            pill = panel;
        }

        var total = textSize.Y + (pill != null ? gap + pillSize.Y : 0);
        var top = -total / 2;

        label.Position = new Vector2(-textSize.X / 2, top);
        label.PivotOffset = textSize / 2;
        if (pill != null)
        {
            pill.Position = new Vector2(-pillSize.X / 2, top + textSize.Y + gap);
            pill.PivotOffset = pillSize / 2;
        }

        // Burst: scale 0.3 -> 1.1, rotate 0 -> 25deg, fade out, 0.9s ease-out.
        burst.Scale = Vector2.One * 0.3f;
        var bt = burst.CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        bt.TweenProperty(burst, "scale", Vector2.One * 1.1f, 0.9);
        bt.TweenProperty(burst, "rotation", Mathf.DegToRad(25), 0.9);
        bt.TweenProperty(burst, "modulate:a", 0f, 0.9);

        Slam(label, -8, 0);
        if (pill != null) Slam(pill, -4, 0.12);

        // Flying hearts (love/deal): 14 at even angles, 220..380px out, rotated along their direction.
        if (kind is StampKind.Love or StampKind.Deal)
        {
            var heartColor = kind == StampKind.Deal ? new Color("ffd75a") : new Color("ff4f9a");
            for (var i = 0; i < 14; i++)
            {
                var a = Mathf.Tau * i / 14f;
                var r = 220 + _rng.Randf() * 160;
                var h = Theme.MakeLabel("♥", 54, heartColor, bold: true, outline: 8, outlineColor: new Color(1, 1, 1, 0.7f));
                root.AddChild(h);
                var hs = Measure(h);
                h.Size = hs;
                h.PivotOffset = hs / 2;
                h.Rotation = a;
                h.Scale = Vector2.One * 0.4f;
                h.Position = -hs / 2;
                var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
                var ht = h.CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
                ht.TweenProperty(h, "position", -hs / 2 + dir * r, 1.1);
                ht.TweenProperty(h, "scale", Vector2.One * 1.1f, 1.1);
                ht.TweenProperty(h, "modulate:a", 0f, 1.1);
            }
        }

        // Whole stamp: hold 80% of 1.9s, then fade out drifting up 10% of its height.
        var t = root.CreateTween();
        _stampTween = t;
        t.TweenInterval(1.52);
        t.SetParallel();
        t.TweenProperty(root, "modulate:a", 0f, 0.38);
        t.TweenProperty(root, "position:y", StampAt.Y - total * 0.1f, 0.38);
        t.SetParallel(false);
        t.TweenCallback(Callable.From(() =>
        {
            if (_stamp == root) { _stamp = null; _stampTween = null; }
            root.QueueFree();
        }));

        if (kind == StampKind.Bad) Jolt();
    }

    /// <summary>A label's real minimum size. Call only once it is inside the tree (theme overrides applied).</summary>
    private static Vector2 Measure(Label l)
    {
        if (!l.IsInsideTree()) l.Notification((int)Control.NotificationThemeChanged);
        return l.GetMinimumSize();
    }

    /// <summary>Quickly fades out the stamp on screen, if any.</summary>
    private void DismissStamp()
    {
        var old = _stamp;
        _stamp = null;
        if (_stampTween != null && GodotObject.IsInstanceValid(_stampTween)) _stampTween.Kill();
        _stampTween = null;
        if (old == null || !GodotObject.IsInstanceValid(old)) return;
        var t = old.CreateTween();
        t.TweenProperty(old, "modulate:a", 0f, 0.15);
        t.TweenCallback(Callable.From(old.QueueFree));
    }

    /// <summary>CSS slam: from scale 3, rotate -20deg, transparent to the resting rotation, 0.45s with overshoot.</summary>
    private static void Slam(Control c, float restDeg, double delay)
    {
        void Apply(float u)
        {
            var y = CubicBezier(0.2f, 1.6f, 0.4f, 1f, u);
            c.Scale = Vector2.One * Mathf.Lerp(3f, 1f, y);
            c.RotationDegrees = Mathf.Lerp(-20f, restDeg, y);
            c.Modulate = c.Modulate with { A = Mathf.Clamp(y, 0f, 1f) };
        }
        Apply(0);
        var t = c.CreateTween();
        if (delay > 0) t.TweenInterval(delay);
        t.TweenMethod(Callable.From<float>(Apply), 0f, 1f, 0.45);
    }

    /// <summary>CSS cubic-bezier(x1, y1, x2, y2) evaluated at progress <paramref name="x"/>.</summary>
    private static float CubicBezier(float x1, float y1, float x2, float y2, float x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        static float B(float p1, float p2, float s) => 3 * (1 - s) * (1 - s) * s * p1 + 3 * (1 - s) * s * s * p2 + s * s * s;
        float lo = 0, hi = 1, s = x;
        for (var i = 0; i < 24; i++)
        {
            s = (lo + hi) / 2;
            if (B(x1, x2, s) < x) lo = s; else hi = s;
        }
        return B(y1, y2, s);
    }

    private static Shader BurstShader() => _burstShader ??= new Shader
    {
        // repeating-conic-gradient(ray 0 5deg, transparent 5deg 14deg) masked by
        // radial-gradient(circle, transparent 18%, #000 30%, transparent 68%); "circle" is farthest-corner sized
        // (450 * sqrt 2 = 636px for the 900px box).
        Code = @"
shader_type canvas_item;
uniform vec4 ray_color : source_color = vec4(1.0);
void fragment() {
    vec2 p = (UV - 0.5) * 900.0;
    float r = length(p);
    float ang = degrees(atan(p.x, -p.y));
    if (ang < 0.0) ang += 360.0;
    float m = mod(ang, 14.0);
    float aa = max(fwidth(ang), 0.001);
    float wedge = smoothstep(0.0, aa, m) * (1.0 - smoothstep(5.0 - aa, 5.0, m));
    float R = 636.4;
    float mask = clamp((r - 0.18 * R) / (0.12 * R), 0.0, 1.0) * clamp((0.68 * R - r) / (0.38 * R), 0.0, 1.0);
    mask *= 1.0 - smoothstep(449.0, 450.0, r);
    COLOR = vec4(ray_color.rgb, ray_color.a * wedge * mask) * COLOR;
}",
    };

    // ------------------------------------------------------------------------------------------------ pops

    /// <summary>A "+N ♥" (pink, rising) or "−N ♥" (grey) pop at a stage position.</summary>
    public void HeartPop(int delta, Vector2 at)
    {
        if (delta == 0) return;
        var text = delta > 0 ? $"+{delta} ♥" : $"−{-delta} ♥";
        Rise(text, 44, delta > 0 ? Theme.Pink : new Color("99aaaa"), at);
    }

    /// <summary>A small pink note rising from the heart meter, e.g. "he won't warm up more this visit".</summary>
    public void Note(string text, Vector2 at) => Rise(text, 30, new Color("ffd6e8"), at);

    /// <summary>style.css heartpop: 20px low, scale 0.6, clear -> (20%) scale 1.15, opaque -> 120px up, clear; 1.6s.</summary>
    private void Rise(string text, int size, Color color, Vector2 at)
    {
        if (!Alive) return;
        var l = Theme.MakeLabel(text, size, color, bold: true, outline: 8, outlineColor: new Color(0, 0, 0, 0.75f));
        l.AutowrapMode = TextServer.AutowrapMode.Off;
        l.AddThemeColorOverride("font_shadow_color", Colors.Black);
        l.AddThemeConstantOverride("shadow_offset_x", 0);
        l.AddThemeConstantOverride("shadow_offset_y", 3);
        _parent.AddChild(l);
        var s = Measure(l);
        l.Size = s;
        l.PivotOffset = s / 2;
        var basePos = new Vector2(at.X - s.X / 2, at.Y - s.Y / 2);
        l.Position = basePos + new Vector2(0, 20);
        l.Scale = Vector2.One * 0.6f;
        l.Modulate = l.Modulate with { A = 0 };

        var t = l.CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        t.SetParallel();
        t.TweenProperty(l, "position", basePos, 0.32);
        t.TweenProperty(l, "scale", Vector2.One * 1.15f, 0.32);
        t.TweenProperty(l, "modulate:a", 1f, 0.32);
        t.Chain().TweenProperty(l, "position", basePos + new Vector2(0, -120), 1.28);
        t.TweenProperty(l, "scale", Vector2.One, 1.28);
        t.TweenProperty(l, "modulate:a", 0f, 1.28);
        t.Chain().TweenCallback(Callable.From(l.QueueFree));
    }

    // ------------------------------------------------------------------------------------------------ vignette

    /// <summary>Full-screen pink (up) or blue-grey (down) inner glow with a big title and smaller sub line.</summary>
    public void Vignette(bool up, string title, string? sub = null)
    {
        if (!Alive) return;
        var root = new Control { Name = "Vignette", MouseFilter = Ignore, Size = new Vector2(ShopStage.W, ShopStage.H) };
        root.Modulate = root.Modulate with { A = 0 };

        var glow = new ColorRect
        {
            MouseFilter = Ignore,
            Color = Colors.White,
            Size = new Vector2(ShopStage.W, ShopStage.H),
            Material = new ShaderMaterial { Shader = VignetteShader() },
        };
        var mat = (ShaderMaterial)glow.Material;
        mat.SetShaderParameter("glow_color", up ? new Color(1f, 90 / 255f, 160 / 255f, 0.75f) : new Color(40 / 255f, 40 / 255f, 80 / 255f, 0.85f));
        mat.SetShaderParameter("rect_size", new Vector2(ShopStage.W, ShopStage.H));
        root.AddChild(glow);

        var box = new VBoxContainer { MouseFilter = Ignore, Alignment = BoxContainer.AlignmentMode.Center, Size = new Vector2(ShopStage.W, ShopStage.H) };
        box.AddThemeConstantOverride("separation", 0);
        var glowColor = up ? Theme.PinkDeep with { A = 0.6f } : new Color("333344") with { A = 0.8f };
        var shadowColor = up ? new Color("6a1438") : Colors.Black;
        box.AddChild(VignetteLabel(title, 72, glowColor, shadowColor));
        if (!string.IsNullOrEmpty(sub)) box.AddChild(VignetteLabel(sub, 40, glowColor, shadowColor));
        root.AddChild(box);
        _parent.AddChild(root);

        // 0 -> 15% fade in, hold to 70%, fade out by 100% of 1.9s.
        var t = root.CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        t.TweenProperty(root, "modulate:a", 1f, 0.285);
        t.TweenInterval(1.045);
        t.TweenProperty(root, "modulate:a", 0f, 0.57);
        t.TweenCallback(Callable.From(root.QueueFree));
    }

    private static Label VignetteLabel(string text, int size, Color glow, Color shadow)
    {
        var l = Theme.MakeLabel(text, size, Colors.White, bold: true, outline: size >= 60 ? 16 : 10, outlineColor: glow);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.AutowrapMode = TextServer.AutowrapMode.Off;
        l.AddThemeColorOverride("font_shadow_color", shadow);
        l.AddThemeConstantOverride("shadow_offset_x", 0);
        l.AddThemeConstantOverride("shadow_offset_y", 4);
        l.AddThemeConstantOverride("shadow_outline_size", 0);
        return l;
    }

    private static Shader VignetteShader() => _vignetteShader ??= new Shader
    {
        // box-shadow: inset 0 0 220px 60px: the inner rect (inset by the 60px spread) blurred with sigma = 110,
        // and the shadow is everything that blurred rect doesn't cover.
        Code = @"
shader_type canvas_item;
uniform vec4 glow_color : source_color = vec4(1.0, 0.35, 0.63, 0.75);
uniform vec2 rect_size = vec2(1920.0, 1080.0);
float erf_approx(float x) {
    float a = 0.147;
    float x2 = x * x;
    float v = sqrt(1.0 - exp(-x2 * (1.27324 + a * x2) / (1.0 + a * x2)));
    return x < 0.0 ? -v : v;
}
float phi(float z) { return 0.5 * (1.0 + erf_approx(z * 0.70710678)); }
void fragment() {
    vec2 p = UV * rect_size;
    float spread = 60.0;
    float sigma = 110.0;
    float cx = phi((p.x - spread) / sigma) - phi((p.x - (rect_size.x - spread)) / sigma);
    float cy = phi((p.y - spread) / sigma) - phi((p.y - (rect_size.y - spread)) / sigma);
    float a = clamp(1.0 - cx * cy, 0.0, 1.0);
    COLOR = vec4(glow_color.rgb, glow_color.a * a) * COLOR;
}",
    };

    // ------------------------------------------------------------------------------------------------ jolt

    private static readonly Vector2[] JoltKeys = { Vector2.Zero, new(-14, 6), new(12, -8), new(-8, 4), new(6, -2), Vector2.Zero };

    /// <summary>Shake the whole stage briefly (0.4s). Ignored while a jolt is already running.</summary>
    public void Jolt()
    {
        if (_jolting || !GodotObject.IsInstanceValid(_stageRoot) || !_stageRoot.IsInsideTree()) return;
        _jolting = true;
        var basePos = _stageRoot.Position;
        var lastSet = basePos;

        void Apply(float u)
        {
            if (!GodotObject.IsInstanceValid(_stageRoot)) return;
            // If the layout re-fitted the stage mid-shake, follow its new resting position.
            if (_stageRoot.Position != lastSet) basePos += _stageRoot.Position - lastSet;
            var seg = Mathf.Min((int)(u * 5), 4);
            var k = u * 5 - seg;
            k = k * k * (3 - 2 * k);   // CSS "ease" per keyframe, roughly
            lastSet = basePos + JoltKeys[seg].Lerp(JoltKeys[seg + 1], k);
            _stageRoot.Position = lastSet;
        }

        var t = _stageRoot.CreateTween();
        t.TweenMethod(Callable.From<float>(Apply), 0f, 1f, 0.4);
        t.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(_stageRoot))
            {
                if (_stageRoot.Position != lastSet) basePos += _stageRoot.Position - lastSet;
                _stageRoot.Position = basePos;
            }
            _jolting = false;
        }));
    }
}
