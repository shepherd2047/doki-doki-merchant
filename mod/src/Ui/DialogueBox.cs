using Godot;

namespace DokiDokiMerchant;

/// <summary>
/// The visual-novel text box at the bottom right (style.css #bubble) with its "The Merchant ♥" nameplate, a
/// typewriter and the bouncing ▼ caret; plus the white "You" bubble above it (style.css #you).
/// </summary>
internal sealed class DialogueBox
{
    // style.css #bubble (border-box: 3px border + 34/30/46 padding inside 696x228)
    private static readonly Vector2 BoxPos = new(1200, 822);
    private static readonly Vector2 BoxSize = new(696, 228);
    private const float TextLeft = 33, TextTop = 37, TextW = 630, TextH = 150;
    private const int FontMax = 29, FontMin = 22;

    // style.css #you
    private const float YouLeft = 1200, YouBottom = 1080 - 272, YouMaxW = 560;
    private const int YouFont = 26;

    private const string RoundedShader = @"shader_type canvas_item;
uniform vec2 size = vec2(100.0, 100.0);
uniform vec4 top_color : source_color = vec4(1.0);
uniform vec4 bottom_color : source_color = vec4(1.0);
uniform vec4 radii = vec4(12.0); // top-left, top-right, bottom-right, bottom-left
uniform float inset_width = 0.0;
uniform vec4 inset_color : source_color = vec4(1.0, 1.0, 1.0, 0.0);
float sd_round_box(vec2 p, vec2 b, vec4 r) {
    float rr = p.x > 0.0 ? (p.y > 0.0 ? r.z : r.y) : (p.y > 0.0 ? r.w : r.x);
    vec2 q = abs(p) - b + rr;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - rr;
}
void fragment() {
    vec2 px = UV * size;
    float d = sd_round_box(px - size * 0.5, size * 0.5, radii);
    vec4 c = mix(top_color, bottom_color, UV.y);
    if (inset_width > 0.0) {
        float line = 1.0 - smoothstep(inset_width - 0.75, inset_width + 0.75, -d);
        c.rgb = mix(c.rgb, inset_color.rgb, line * inset_color.a);
        c.a = max(c.a, line * inset_color.a);
    }
    c.a *= clamp(0.5 - d, 0.0, 1.0);
    COLOR = c;
}";

    private readonly Control _box;
    private readonly Control _plate;
    private readonly ShaderMaterial _plateMat;
    private readonly Label _plateLabel;
    private readonly Control _clip;
    private readonly RichTextLabel _text;

    private readonly Control _youRoot;
    private readonly PanelContainer _youPanel;
    private readonly RichTextLabel _youText;
    private readonly Control _youTag;
    private readonly FontVariation? _italic;
    private Tween? _youTween;

    private Tween? _shakeTween;

    // typewriter state
    private string _who = "";
    private string _full = "";
    private int _shown;
    private double _shownF;
    private double _rate = 40;
    private int _fontSize = FontMax;
    private bool _done = true;
    private int _renderedShown = -1;
    private readonly Action _onFrame;
    private SceneTree? _tree;

    /// <param name="parent">ShopStage.Hud.</param>
    public DialogueBox(Control parent)
    {
        // ---------------------------------------------------------------- the box
        _box = new Control { Name = "DialogueBox", Position = BoxPos, Size = BoxSize, MouseFilter = Control.MouseFilterEnum.Ignore };

        // Pink border and the soft shadow below (box-shadow 0 10px 30px rgba(0,0,0,.6)).
        var frame = new Panel { Size = BoxSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        var frameStyle = new StyleBoxFlat
        {
            DrawCenter = false,
            BorderColor = Theme.Pink,
            ShadowColor = new Color(0, 0, 0, 0.6f),
            ShadowSize = 30,
            ShadowOffset = new Vector2(0, 10),
            AntiAliasing = true,
        };
        frameStyle.SetBorderWidthAll(3);
        frameStyle.SetCornerRadiusAll(18);
        frame.AddThemeStyleboxOverride("panel", frameStyle);
        _box.AddChild(frame);

        // Gradient fill inside the border, with the faint inset white line.
        var fill = new ColorRect { Position = new Vector2(3, 3), Size = BoxSize - new Vector2(6, 6), Color = Colors.White, MouseFilter = Control.MouseFilterEnum.Ignore };
        fill.Material = Rounded(fill.Size, new Color(40 / 255f, 18 / 255f, 40 / 255f, 0.9f), new Color(22 / 255f, 10 / 255f, 26 / 255f, 0.94f),
            new Vector4(15, 15, 15, 15), 3, new Color(1, 1, 1, 0.15f));
        _box.AddChild(fill);

        // Text area: clipped, 150px tall; the label inside grows with its content and scrolls up when it overflows.
        _clip = new Control { Position = new Vector2(TextLeft, TextTop), Size = new Vector2(TextW, TextH), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Position = Vector2.Zero,
            Size = new Vector2(TextW, TextH),
            CustomMinimumSize = new Vector2(TextW, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelectionEnabled = false,
        };
        _text.AddThemeColorOverride("default_color", Theme.Ink);
        if (Theme.Font != null) _text.AddThemeFontOverride("normal_font", Theme.Font);
        if (Theme.Bold != null) _text.AddThemeFontOverride("bold_font", Theme.Bold);
        ApplyTextSize(FontMax);
        _clip.AddChild(_text);
        _box.AddChild(_clip);

        // ---------------------------------------------------------------- nameplate
        _plate = new PanelContainer { Position = new Vector2(24, -26), MouseFilter = Control.MouseFilterEnum.Ignore };
        var plateMargin = new StyleBoxEmpty();
        plateMargin.ContentMarginLeft = plateMargin.ContentMarginRight = 22;
        plateMargin.ContentMarginTop = plateMargin.ContentMarginBottom = 4;
        _plate.AddThemeStyleboxOverride("panel", plateMargin);
        var plateBg = new ColorRect { Color = Colors.White, MouseFilter = Control.MouseFilterEnum.Ignore };
        _plateMat = Rounded(Vector2.One, Theme.Pink, Theme.PinkDeep, new Vector4(12, 12, 12, 0), 0, new Color(1, 1, 1, 0));
        plateBg.Material = _plateMat;
        // The bg must cover the padding too, so it lives outside the container's content rect.
        var plateHolder = new Control { Position = new Vector2(24, -26), MouseFilter = Control.MouseFilterEnum.Ignore };
        plateHolder.AddChild(plateBg);
        _plateLabel = Theme.MakeLabel("", 28, Colors.White, bold: true, outline: 0);
        _plateLabel.AddThemeColorOverride("font_shadow_color", new Color("6a1438"));
        _plateLabel.AddThemeConstantOverride("shadow_offset_x", 0);
        _plateLabel.AddThemeConstantOverride("shadow_offset_y", 2);
        _plateLabel.AddThemeConstantOverride("shadow_outline_size", 2);
        _plate.AddChild(_plateLabel);
        _plate.Resized += () =>
        {
            plateBg.Size = _plate.Size;
            _plateMat.SetShaderParameter("size", _plate.Size);
        };
        _box.AddChild(plateHolder);
        _box.AddChild(_plate);

        // ---------------------------------------------------------------- the You bubble
        _youRoot = new Control { Name = "YouBubble", Position = new Vector2(YouLeft, YouBottom), Size = Vector2.Zero, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _youPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, GrowVertical = Control.GrowDirection.Begin };
        var youStyle = new StyleBoxFlat
        {
            BgColor = new Color(1, 1, 1, 0.95f),
            BorderColor = Theme.Brass,
            ShadowColor = new Color(0, 0, 0, 0.45f),
            ShadowSize = 18,
            ShadowOffset = new Vector2(0, 6),
            AntiAliasing = true,
            CornerRadiusTopLeft = 22,
            CornerRadiusTopRight = 22,
            CornerRadiusBottomRight = 6,
            CornerRadiusBottomLeft = 22,
            ContentMarginLeft = 20 + 3,
            ContentMarginRight = 22 + 3,
            ContentMarginTop = 14 + 3,
            ContentMarginBottom = 14 + 3,
        };
        youStyle.SetBorderWidthAll(3);
        _youPanel.AddThemeStyleboxOverride("panel", youStyle);
        _youText = new RichTextLabel
        {
            BbcodeEnabled = false,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelectionEnabled = false,
        };
        if (Theme.Font != null) _youText.AddThemeFontOverride("normal_font", Theme.Font);
        _youText.AddThemeFontSizeOverride("normal_font_size", YouFont);
        _youText.AddThemeColorOverride("default_color", new Color("3a1630"));
        _youText.AddThemeConstantOverride("line_separation", 1);
        _youPanel.AddChild(_youText);

        // The "You" tag, drawn over a transparent inline spacer at the start of the text.
        _youTag = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var tagStyle = Theme.Box(new Color("8b2b2b"), radius: 10);
        tagStyle.ContentMarginLeft = tagStyle.ContentMarginRight = 12;
        tagStyle.ContentMarginTop = tagStyle.ContentMarginBottom = 2;
        _youTag.AddThemeStyleboxOverride("panel", tagStyle);
        var tagLabel = Theme.MakeLabel("You", 20, Colors.White, bold: true, outline: 0);
        _youTag.AddChild(tagLabel);
        _youText.AddChild(_youTag);

        _youRoot.AddChild(_youPanel);
        if (Theme.Font != null)
            _italic = new FontVariation { BaseFont = Theme.Font, VariationTransform = new Transform2D(1f, 0.2f, 0f, 1f, 0f, 0f) };

        parent.AddChild(_youRoot);
        parent.AddChild(_box);

        // ---------------------------------------------------------------- per-frame typewriter
        _onFrame = OnFrame;
        _tree = parent.GetTree();
        if (_tree != null) _tree.ProcessFrame += _onFrame;
        _box.TreeExiting += () =>
        {
            if (_tree != null) _tree.ProcessFrame -= _onFrame;
            _tree = null;
        };

        SetSpeaker("merchant");
        Render();
    }

    /// <summary>Type out a line. who: "merchant" (nameplate "The Merchant ♥"), "bestie" ("Your Bestie ♥",
    /// lilac), or "system" (setup problems, no voice; nameplate "Doki Doki Merchant"). seconds: how long the
    /// voice clip lasts, so typing finishes with the voice (0 = default speed).</summary>
    public void Say(string who, string text, double seconds)
    {
        if (!GodotObject.IsInstanceValid(_box)) return;
        text = (text ?? "").Trim();
        who = string.IsNullOrEmpty(who) ? "merchant" : who.ToLowerInvariant();
        // A streamed continuation of the same speaker's line keeps typing from where it was (like the web say()).
        var cont = who == _who && _shown > 0 && text.StartsWith(_full[.._shown], StringComparison.Ordinal);
        SetSpeaker(who);
        _full = text;
        if (!cont) { _shown = 0; _shownF = 0; }
        var remaining = Math.Max(1, _full.Length - _shown);
        _rate = seconds > 0 ? Math.Clamp(remaining / seconds, 18, 60) : 40;
        _done = _shown >= _full.Length;
        FitFont();
        _renderedShown = -1;
        Render();
    }

    /// <summary>True while a line is still being typed out.</summary>
    public bool IsTyping => !_done;

    /// <summary>Shake the box (an insult or a lowball).</summary>
    public void Shake()
    {
        if (!GodotObject.IsInstanceValid(_box)) return;
        _shakeTween?.Kill();
        _box.Position = BoxPos;
        var t = _box.CreateTween();
        const double step = 0.08; // 0.4s, keyframes at 20/40/60/80%
        t.TweenProperty(_box, "position:x", BoxPos.X - 6, step).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(_box, "position:x", BoxPos.X + 6, step).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(_box, "position:x", BoxPos.X - 6, step).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(_box, "position:x", BoxPos.X + 6, step).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(_box, "position:x", BoxPos.X, step).SetTrans(Tween.TransitionType.Sine);
        _shakeTween = t;
    }

    /// <summary>Show the player's words above the box. live = still listening/transcribing (grey italic with "…";
    /// an empty live text shows just "…" while the mic records). An empty final text hides the bubble.</summary>
    public void ShowYou(string text, bool live)
    {
        if (!GodotObject.IsInstanceValid(_youRoot)) return;
        text = (text ?? "").Trim();
        if (text.Length == 0 && !live)
        {
            HideYou();
            return;
        }

        // Inline layout: [spacer the size of the tag] text (…)
        var tagSize = _youTag.GetCombinedMinimumSize();
        var shown = live ? (text.Length == 0 ? "…" : text + " …") : text;
        var font = live ? (Font?)_italic ?? Theme.Font : Theme.Font;
        var lineH = font != null ? font.GetHeight(YouFont) : YouFont * 1.25f;
        var spacerW = tagSize.X + 10;
        var spacerH = Mathf.Max(1, Mathf.Min(tagSize.Y, lineH));

        _youText.Clear();
        _youText.AddImage(Spacer(), (int)spacerW, (int)spacerH);
        if (font != null) _youText.PushFont(font, YouFont);
        _youText.PushColor(live ? new Color("8a6a80") : new Color("3a1630"));
        _youText.AddText(shown);
        _youText.Pop();
        if (font != null) _youText.Pop();

        // Shrink-wrap up to max-width 560 (border-box: minus 3px borders and 20/22 padding).
        var innerMax = YouMaxW - 6 - 42;
        var textW = font != null ? font.GetStringSize(shown, HorizontalAlignment.Left, -1, YouFont).X : shown.Length * YouFont * 0.5f;
        var w = Mathf.Min(innerMax, spacerW + textW + 4);
        _youText.CustomMinimumSize = new Vector2(w, 0);
        _youTag.Position = new Vector2(0, Mathf.Max(0, (lineH - tagSize.Y) / 2) - 1);
        _youTag.Size = tagSize;
        ResetYouSize();
        Callable.From(ResetYouSize).CallDeferred();

        var wasHidden = !_youRoot.Visible || _youRoot.Modulate.A < 0.99f;
        _youTween?.Kill();
        _youRoot.Visible = true;
        var t = _youRoot.CreateTween();
        if (wasHidden)
        {
            _youRoot.Position = new Vector2(YouLeft, YouBottom + 12);
            _youRoot.Modulate = new Color(1, 1, 1, 0);
            t.SetParallel();
            t.TweenProperty(_youRoot, "position:y", YouBottom, 0.25).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
            t.TweenProperty(_youRoot, "modulate:a", 1.0, 0.25).SetEase(Tween.EaseType.Out);
            t.SetParallel(false);
        }
        else
        {
            _youRoot.Position = new Vector2(YouLeft, YouBottom);
            _youRoot.Modulate = Colors.White;
            t.TweenInterval(0.01);
        }
        if (!live)
        {
            t.TweenInterval(6.0);
            AppendFade(t);
        }
        _youTween = t;
    }

    /// <summary>Fade the You bubble out.</summary>
    public void HideYou()
    {
        if (!GodotObject.IsInstanceValid(_youRoot) || !_youRoot.Visible) return;
        _youTween?.Kill();
        var t = _youRoot.CreateTween();
        AppendFade(t);
        _youTween = t;
    }

    // ---------------------------------------------------------------- internals

    private void AppendFade(Tween t)
    {
        t.TweenProperty(_youRoot, "modulate:a", 0.0, 0.8);
        t.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(_youRoot)) _youRoot.Visible = false; }));
    }

    private void ResetYouSize()
    {
        if (!GodotObject.IsInstanceValid(_youPanel)) return;
        // Zero-height rect anchored at the bubble's bottom edge; GrowVertical=Begin makes it grow upward.
        _youPanel.OffsetLeft = 0;
        _youPanel.OffsetRight = 0;
        _youPanel.OffsetTop = 0;
        _youPanel.OffsetBottom = 0;
    }

    private static ImageTexture? _spacer;
    private static ImageTexture Spacer()
    {
        if (_spacer != null && GodotObject.IsInstanceValid(_spacer)) return _spacer;
        var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        _spacer = ImageTexture.CreateFromImage(img);
        return _spacer;
    }

    private void SetSpeaker(string who)
    {
        _who = who;
        Color top, bottom, shadow;
        string name;
        switch (who)
        {
            case "bestie":
                name = "Your Bestie ♥"; top = Theme.Lilac; bottom = new Color("8a4fd8"); shadow = new Color("3a1468");
                break;
            case "system":
                name = "Doki Doki Merchant"; top = Theme.Brass; bottom = new Color("6e6a64"); shadow = new Color("3a2c14");
                break;
            default:
                name = "The Merchant ♥"; top = Theme.Pink; bottom = Theme.PinkDeep; shadow = new Color("6a1438");
                break;
        }
        _plateLabel.Text = name;
        _plateLabel.AddThemeColorOverride("font_shadow_color", shadow);
        _plateMat.SetShaderParameter("top_color", top);
        _plateMat.SetShaderParameter("bottom_color", bottom);
        _plate.Size = Vector2.Zero; // shrink-wrap to the new name
    }

    private void ApplyTextSize(int size)
    {
        _fontSize = size;
        _text.AddThemeFontSizeOverride("normal_font_size", size);
        _text.AddThemeFontSizeOverride("bold_font_size", size);
        // line-height 1.28: Kreon's own line height is about 1.2em, add the rest as separation
        _text.AddThemeConstantOverride("line_separation", Mathf.RoundToInt(size * 0.08f));
    }

    /// <summary>Pick the largest font (29 down to 22) at which the whole line fits the 150px area.</summary>
    private void FitFont()
    {
        var full = Escape(_full) + " " + Caret();
        for (var size = FontMax; size >= FontMin; size--)
        {
            ApplyTextSize(size);
            _text.Text = full;
            if (_text.GetContentHeight() <= TextH) return;
        }
        // Still too long at the minimum: keep 22px and scroll as it types.
    }

    private void OnFrame()
    {
        if (!GodotObject.IsInstanceValid(_box)) return;
        if (_done) return;
        var dt = _tree != null ? _box.GetProcessDeltaTime() : 0.016;
        _shownF += _rate * dt;
        var add = (int)_shownF;
        if (add <= 0) return;
        _shownF -= add;
        _shown = Math.Min(_full.Length, _shown + add);
        if (_shown >= _full.Length) _done = true;
        Render();
    }

    private void Render()
    {
        if (_renderedShown == _shown && !_done) return;
        _renderedShown = _shown;
        var bb = Escape(_full[.._Clamp(_shown)]);
        if (_done && _full.Length > 0) bb += " " + Caret();
        _text.Text = bb;
        // Keep the latest lines visible when the text overflows the 150px area.
        var h = _text.GetContentHeight();
        _text.Size = new Vector2(TextW, Mathf.Max(h, 1));
        _text.Position = new Vector2(0, Mathf.Min(0, TextH - h));
    }

    private int _Clamp(int n) => Math.Clamp(n, 0, _full.Length);

    // ▼ in pink, bobbing about 5px every 0.8s (built-in wave effect: amp/10 px, freq in rad/s).
    private static string Caret() => $"[color=#{Theme.Pink.ToHtml(false)}][wave amp=25.0 freq=7.85 connected=0]▼[/wave][/color]";

    private static string Escape(string s) => s.Replace("[", "[lb]");

    private static ShaderMaterial Rounded(Vector2 size, Color top, Color bottom, Vector4 radii, float insetWidth, Color insetColor)
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = RoundedShader } };
        m.SetShaderParameter("size", size);
        m.SetShaderParameter("top_color", top);
        m.SetShaderParameter("bottom_color", bottom);
        m.SetShaderParameter("radii", radii);
        m.SetShaderParameter("inset_width", insetWidth);
        m.SetShaderParameter("inset_color", insetColor);
        return m;
    }
}
