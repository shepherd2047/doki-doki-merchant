using Godot;

namespace DokiDokiMerchant;

/// <summary>
/// Bottom-left: the "…or type to the Merchant" bar with its pink "Say it ♥" button (style.css #type-bar), and the
/// retractable "▲ Chat log N" tab above it (style.css #chatlog).
/// </summary>
/// <remarks>
/// Keyboard: a focused LineEdit consumes printable keys in the GUI pass, before the game's
/// NInputManager._UnhandledKeyInput (which turns raw keys into MegaInput actions) and NHotkeyManager._UnhandledInput
/// see them. Keys it doesn't consume (Escape, Tab, F-keys, the release after Enter) are swallowed here, and
/// ChatPanelPatches blocks both managers for key events while <see cref="Typing"/> is true as a backstop.
/// </remarks>
internal sealed class ChatPanel
{
    private static readonly Color PanelBg97 = new(14 / 255f, 10 / 255f, 18 / 255f, 0.97f);
    private static readonly Color FieldBg = new(14 / 255f, 10 / 255f, 18 / 255f, 0.88f);
    private static readonly Color TabText = new("f3e3c3");
    private static readonly Color Grey = new("999988");
    private const float BarLeft = 60, BarTop = 990, BarW = 1080, BarH = 66;
    private const float LogBottom = 982, BodyH = 280, AnimTime = 0.3f;

    /// <summary>The panel whose field currently exists (for ChatPanelPatches); null when no shop is open.</summary>
    internal static ChatPanel? Active;

    /// <summary>True while a shop's chat field has keyboard focus: game hotkeys must not fire.</summary>
    internal static bool Typing => Active is { } p && p.HasFocus;

    /// <summary>The player sent a typed line (already trimmed, never empty).</summary>
    public event Action<string>? Submitted;

    private readonly LineEdit _field;
    private readonly Label _placeholder;
    private readonly Button _button;
    private readonly Control _buttonWrap;
    private readonly StyleBoxFlat _fieldNormal, _fieldFocused;

    private readonly Control _tab;
    private readonly Label _arrow, _count;
    private readonly Panel _body;
    private readonly ScrollContainer _scroll;
    private readonly VBoxContainer _list;
    private readonly Label _empty;
    private int _rows;
    private bool _open;
    private float _openness; // 0 closed .. 1 open
    private Tween? _tween;

    private readonly SceneTree? _tree;
    private bool _mouseWasDown;
    private bool _releaseOnKeyUp;

    /// <param name="parent">ShopStage.Hud.</param>
    public ChatPanel(Control parent)
    {
        // ---------- type bar ----------
        var bar = new HBoxContainer
        {
            Name = "TypeBar",
            Position = new Vector2(BarLeft, BarTop),
            Size = new Vector2(BarW, BarH),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bar.AddThemeConstantOverride("separation", 12);
        parent.AddChild(bar);

        _fieldNormal = Theme.Box(FieldBg, Theme.Brass, 3, 33);
        _fieldNormal.ContentMarginLeft = _fieldNormal.ContentMarginRight = 3 + 24;
        _fieldNormal.ContentMarginTop = _fieldNormal.ContentMarginBottom = 3;
        _fieldFocused = (StyleBoxFlat)_fieldNormal.Duplicate();
        _fieldFocused.BorderColor = Theme.Pink;
        _fieldFocused.ShadowColor = new Color(1f, 126 / 255f, 182 / 255f, 0.35f);
        _fieldFocused.ShadowSize = 8;

        _field = new LineEdit
        {
            Name = "TypeInput",
            MaxLength = 300,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.Fill,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.Click,
            ContextMenuEnabled = false,
            CaretBlink = true,
        };
        _field.AddThemeStyleboxOverride("normal", _fieldNormal);
        _field.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var readOnly = (StyleBoxFlat)_fieldNormal.Duplicate();
        _field.AddThemeStyleboxOverride("read_only", readOnly);
        if ((Theme.Bold ?? Theme.Font) is { } ff) _field.AddThemeFontOverride("font", ff);
        _field.AddThemeFontSizeOverride("font_size", 28);
        _field.AddThemeColorOverride("font_color", Colors.White);
        _field.AddThemeColorOverride("font_uneditable_color", new Color(1, 1, 1, 0.5f));
        _field.AddThemeColorOverride("caret_color", Theme.Pink);
        _field.AddThemeColorOverride("selection_color", new Color(1f, 126 / 255f, 182 / 255f, 0.4f));
        bar.AddChild(_field);

        // Placeholder as an overlay label: LineEdit can't render its placeholder in a different (italic) font.
        _placeholder = Theme.MakeLabel("…or type to the Merchant", 28, new Color("b9a9c0"), outline: 0);
        if (Italic(Theme.Bold ?? Theme.Font) is { } pf) _placeholder.AddThemeFontOverride("font", pf);
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _placeholder.OffsetLeft = 27;
        _placeholder.OffsetRight = -27;
        _placeholder.ClipText = true;
        _field.AddChild(_placeholder);

        _field.TextChanged += _ => UpdatePlaceholder();
        _field.TextSubmitted += _ => { Submit(); _releaseOnKeyUp = true; };
        _field.FocusEntered += () => _field.AddThemeStyleboxOverride("normal", _fieldFocused);
        _field.FocusExited += () => { _field.AddThemeStyleboxOverride("normal", _fieldNormal); _releaseOnKeyUp = false; };
        _field.GuiInput += OnFieldKey;

        // "Say it ♥": pink→pinkDeep gradient behind a transparent button with a white border.
        _buttonWrap = new MarginContainer { Name = "SayIt", MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.Fill };
        var grad = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        grad.AddThemeStyleboxOverride("panel", Theme.Box(Colors.White, null, 0, 33));
        var gradMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type canvas_item;
                    uniform vec4 top_color : source_color;
                    uniform vec4 bottom_color : source_color;
                    uniform float height = 66.0;
                    varying float vy;
                    void vertex() { vy = VERTEX.y; }
                    void fragment() {
                        vec4 c = mix(top_color, bottom_color, clamp(vy / height, 0.0, 1.0));
                        COLOR = vec4(c.rgb, c.a * COLOR.a);
                    }
                    """,
            },
        };
        gradMat.SetShaderParameter("top_color", Theme.Pink);
        gradMat.SetShaderParameter("bottom_color", Theme.PinkDeep);
        gradMat.SetShaderParameter("height", BarH);
        grad.Material = gradMat;
        grad.Resized += () => gradMat.SetShaderParameter("height", Mathf.Max(1f, grad.Size.Y));
        _buttonWrap.AddChild(grad);

        _button = new Button
        {
            Text = "Say it ♥",
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        StyleSayIt(_button);
        _button.Pressed += () => { Submit(); _field.ReleaseFocus(); };
        _buttonWrap.AddChild(_button);
        bar.AddChild(_buttonWrap);

        // ---------- chat log ----------
        var log = new Control
        {
            Name = "ChatLog",
            Position = new Vector2(BarLeft, 0),
            Size = new Vector2(BarW, LogBottom),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(log);

        _body = new Panel
        {
            Name = "ChatLogBody",
            Size = new Vector2(BarW, 0),
            Position = new Vector2(0, LogBottom),
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _body.AddThemeStyleboxOverride("panel", Theme.Box(PanelBg97, Theme.Brass, 3, 18));
        log.AddChild(_body);

        // The content keeps its full open size and is clipped by the body while it animates.
        _scroll = new ScrollContainer
        {
            Position = new Vector2(3, 3),
            Size = new Vector2(BarW - 6, BodyH - 6),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FollowFocus = false,
        };
        StyleScrollBar(_scroll.GetVScrollBar());
        _scroll.GetVScrollBar().Changed += ScrollToEnd;
        _body.AddChild(_scroll);

        var pad = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pad.AddThemeConstantOverride("margin_left", 22);
        pad.AddThemeConstantOverride("margin_right", 22);
        pad.AddThemeConstantOverride("margin_top", 14);
        pad.AddThemeConstantOverride("margin_bottom", 14);
        _scroll.AddChild(pad);

        _list = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 8);
        pad.AddChild(_list);

        _empty = Theme.MakeLabel("Nothing said yet. Tap the mic or type below ♥", 24, Grey, outline: 0);
        if (Italic(Theme.Font) is { } ef) _empty.AddThemeFontOverride("font", ef);
        _empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _list.AddChild(_empty);

        // Tab: "▲ Chat log [N]"
        var tabStyle = Theme.Box(Theme.PanelBg, Theme.Brass, 3, 0);
        tabStyle.BorderWidthBottom = 0;
        tabStyle.CornerRadiusTopLeft = tabStyle.CornerRadiusTopRight = 16;
        tabStyle.ContentMarginLeft = tabStyle.ContentMarginRight = 3 + 20;
        tabStyle.ContentMarginTop = 3 + 6;
        tabStyle.ContentMarginBottom = 6;
        var tab = new PanelContainer
        {
            Name = "ChatLogTab",
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        tab.AddThemeStyleboxOverride("panel", tabStyle);
        _tab = tab;
        log.AddChild(tab);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        tab.AddChild(row);

        _arrow = Theme.MakeLabel("▲", 24, TabText, bold: true, outline: 0);
        _arrow.VerticalAlignment = VerticalAlignment.Center;
        _arrow.Resized += () => _arrow.PivotOffset = _arrow.Size / 2;
        row.AddChild(_arrow);
        var title = Theme.MakeLabel("Chat log", 24, TabText, bold: true, outline: 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(title);

        var badgeStyle = Theme.Box(Theme.PinkDeep, null, 0, 12);
        badgeStyle.ContentMarginLeft = badgeStyle.ContentMarginRight = 8;
        var badge = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(30, 0),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        badge.AddThemeStyleboxOverride("panel", badgeStyle);
        _count = Theme.MakeLabel("0", 20, Colors.White, bold: true, outline: 0);
        _count.HorizontalAlignment = HorizontalAlignment.Center;
        _count.VerticalAlignment = VerticalAlignment.Center;
        badge.AddChild(_count);
        row.AddChild(badge);

        tab.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                tab.AcceptEvent();
                SetOpen(!_open);
            }
        };
        tab.MinimumSizeChanged += Layout;
        tab.Resized += Layout;

        Layout();
        Layout(); // (min size may only settle once the tab is in the tree)
        Callable.From(Layout).CallDeferred();

        // ---------- lifecycle ----------
        Active = this;
        _tree = Engine.GetMainLoop() as SceneTree;
        if (_tree != null) _tree.ProcessFrame += OnFrame;
        _field.TreeExiting += () =>
        {
            if (_tree != null) _tree.ProcessFrame -= OnFrame;
            if (Active == this) Active = null;
        };
    }

    /// <summary>True while the text field has keyboard focus (push-to-talk must ignore V then).</summary>
    public bool HasFocus => GodotObject.IsInstanceValid(_field) && _field.IsInsideTree() && _field.HasFocus();

    public void SetEnabled(bool enabled)
    {
        if (!GodotObject.IsInstanceValid(_field)) return;
        _field.Editable = enabled;
        _field.FocusMode = enabled ? Control.FocusModeEnum.Click : Control.FocusModeEnum.None;
        _field.MouseDefaultCursorShape = enabled ? Control.CursorShape.Ibeam : Control.CursorShape.Arrow;
        if (!enabled && _field.HasFocus()) _field.ReleaseFocus();
        _button.Disabled = !enabled;
        _button.MouseDefaultCursorShape = enabled ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;
        var grey = enabled ? Colors.White : new Color(0.6f, 0.6f, 0.6f, 0.6f);
        _field.Modulate = grey;
        _buttonWrap.Modulate = grey;
    }

    /// <summary>Append to the log. who: "you", "merchant", "bestie" or "system".</summary>
    public void Add(string who, string text)
    {
        if (!GodotObject.IsInstanceValid(_list)) return;
        var body = Escape(text ?? "");
        string bb = (who ?? "").ToLowerInvariant() switch
        {
            "you" => $"[b][color=#ff8a7a]You[/color][/b]  [color=#ffffff]{body}[/color]",
            "merchant" => $"[b][color=#9fd3ff]Merchant[/color][/b]  [color=#dfe8f0]{body}[/color]",
            "bestie" => $"[b][color=#{Theme.Pink.ToHtml(false)}]Bestie[/color][/b]  [i][color=#ffd6ea]{body}[/color][/i]",
            _ => $"[i][color=#999988]{body}[/color][/i]",
        };
        var rtl = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelectionEnabled = false,
        };
        var reg = Theme.Font;
        var bold = Theme.Bold ?? Theme.Font;
        if (reg != null) rtl.AddThemeFontOverride("normal_font", reg);
        if (bold != null) rtl.AddThemeFontOverride("bold_font", bold);
        if (Italic(reg) is { } it) rtl.AddThemeFontOverride("italics_font", it);
        if (Italic(bold) is { } bit) rtl.AddThemeFontOverride("bold_italics_font", bit);
        foreach (var k in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size" })
            rtl.AddThemeFontSizeOverride(k, 24);
        rtl.AddThemeConstantOverride("line_separation", 7); // line-height 1.3 at 24px
        rtl.AddThemeColorOverride("default_color", new Color("eeeeee"));
        rtl.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        rtl.Text = bb;
        _list.AddChild(rtl);

        _rows++;
        _empty.Visible = false;
        _count.Text = _rows.ToString();
        Callable.From(ScrollToEnd).CallDeferred();
    }

    // ---------------------------------------------------------------- behaviour

    private void Submit()
    {
        if (!GodotObject.IsInstanceValid(_field) || !_field.Editable) return;
        var text = _field.Text.Trim();
        if (text.Length == 0) return;
        _field.Text = "";
        UpdatePlaceholder();
        Submitted?.Invoke(text);
    }

    private void UpdatePlaceholder() => _placeholder.Visible = _field.Text.Length == 0;

    /// <summary>Keys the LineEdit itself would let through to the game while it has focus.</summary>
    private void OnFieldKey(InputEvent ev)
    {
        if (ev is not InputEventKey k) return;
        switch (k.Keycode)
        {
            case Key.Escape:
                _field.AcceptEvent();
                // Let go on key-up, so the release doesn't reach the game as a "back" press.
                if (!k.Pressed) _field.ReleaseFocus();
                break;
            case Key.Tab:
                _field.AcceptEvent(); // no focus hopping onto the game's own controls
                break;
            case Key.Enter or Key.KpEnter when !k.Pressed:
                _field.AcceptEvent();
                if (_releaseOnKeyUp) _field.ReleaseFocus();
                break;
        }
    }

    /// <summary>Clicking anywhere outside the bar releases the field's focus.</summary>
    private void OnFrame()
    {
        if (!GodotObject.IsInstanceValid(_field) || !_field.IsInsideTree()) return;
        // The stage is hidden under full-screen game screens (map, deck, card removal): never keep focus there.
        if (_field.HasFocus() && !_field.IsVisibleInTree()) _field.ReleaseFocus();
        var down = Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsMouseButtonPressed(MouseButton.Right);
        if (down && !_mouseWasDown && _field.HasFocus())
        {
            var m = _field.GetGlobalMousePosition();
            if (!_field.GetGlobalRect().HasPoint(m) && !_buttonWrap.GetGlobalRect().HasPoint(m))
                _field.ReleaseFocus();
        }
        _mouseWasDown = down;
    }

    private void SetOpen(bool open)
    {
        _open = open;
        _tween?.Kill();
        if (open)
        {
            _body.Visible = true;
            _scroll.MouseFilter = Control.MouseFilterEnum.Stop;
            ScrollToEnd();
        }
        else
        {
            _scroll.MouseFilter = Control.MouseFilterEnum.Ignore;
        }
        _tween = _tab.CreateTween();
        _tween.TweenMethod(Callable.From<float>(v => { _openness = v; Layout(); }), _openness, open ? 1f : 0f, AnimTime)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        if (!open) _tween.TweenCallback(Callable.From(() => { if (!_open) _body.Visible = false; }));
    }

    private void Layout()
    {
        if (!GodotObject.IsInstanceValid(_tab)) return;
        var h = BodyH * _openness;
        _body.Size = new Vector2(BarW, h);
        _body.Position = new Vector2(0, LogBottom - h);
        _body.Modulate = new Color(1, 1, 1, _openness);
        _arrow.Rotation = Mathf.Pi * _openness;
        var min = _tab.GetCombinedMinimumSize();
        if (_tab.Size != min) _tab.Size = min;
        _tab.Position = new Vector2(24, LogBottom - h - min.Y);
    }

    private void ScrollToEnd()
    {
        if (!GodotObject.IsInstanceValid(_scroll)) return;
        var sb = _scroll.GetVScrollBar();
        _scroll.ScrollVertical = (int)Math.Ceiling(sb.MaxValue);
    }

    // ---------------------------------------------------------------- styling helpers

    private static void StyleSayIt(Button b)
    {
        StyleBoxFlat Make(Color bg)
        {
            var s = Theme.Box(bg, Colors.White, 3, 33);
            s.ContentMarginLeft = s.ContentMarginRight = 3 + 28;
            s.ContentMarginTop = s.ContentMarginBottom = 3;
            return s;
        }
        b.AddThemeStyleboxOverride("normal", Make(Colors.Transparent));
        b.AddThemeStyleboxOverride("hover", Make(new Color(1, 1, 1, 0.14f)));
        b.AddThemeStyleboxOverride("pressed", Make(new Color(0, 0, 0, 0.14f)));
        b.AddThemeStyleboxOverride("disabled", Make(Colors.Transparent));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        if ((Theme.Bold ?? Theme.Font) is { } f) b.AddThemeFontOverride("font", f);
        b.AddThemeFontSizeOverride("font_size", 28);
        foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color", "font_disabled_color" })
            b.AddThemeColorOverride(k, Colors.White);
        b.AddThemeConstantOverride("outline_size", 4);
        b.AddThemeColorOverride("font_outline_color", Theme.Plum with { A = 0.6f });
    }

    private static void StyleScrollBar(ScrollBar sb)
    {
        var track = Theme.Box(new Color(1, 1, 1, 0.05f), null, 0, 4);
        track.ContentMarginLeft = track.ContentMarginRight = 4;
        var grab = Theme.Box(Theme.Brass with { A = 0.7f }, null, 0, 4);
        grab.ContentMarginLeft = grab.ContentMarginRight = 4;
        var grabHi = Theme.Box(Theme.Brass, null, 0, 4);
        grabHi.ContentMarginLeft = grabHi.ContentMarginRight = 4;
        sb.AddThemeStyleboxOverride("scroll", track);
        sb.AddThemeStyleboxOverride("scroll_focus", track);
        sb.AddThemeStyleboxOverride("grabber", grab);
        sb.AddThemeStyleboxOverride("grabber_highlight", grabHi);
        sb.AddThemeStyleboxOverride("grabber_pressed", grabHi);
    }

    private static readonly Dictionary<Font, FontVariation> ItalicCache = new();

    /// <summary>Kreon has no italic face: slant the outlines (FontVariation.VariationTransform, per Godot docs).</summary>
    private static Font? Italic(Font? baseFont)
    {
        if (baseFont == null) return null;
        if (ItalicCache.TryGetValue(baseFont, out var v) && GodotObject.IsInstanceValid(v)) return v;
        v = new FontVariation { BaseFont = baseFont, VariationTransform = new Transform2D(1f, 0.2f, 0f, 1f, 0f, 0f) };
        ItalicCache[baseFont] = v;
        return v;
    }

    private static string Escape(string s) => s.Replace("[", "[lb]");
}
