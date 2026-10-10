using Godot;

namespace DokiDokiMerchant;

/// <summary>
/// The date scene: a dimmed stage with a visual-novel card in the middle, a pink "♥ Date" title, the situation
/// text and up to three answer buttons (or a single Continue button for his reply and the ending).
/// </summary>
internal sealed class DatePanel
{
    private const float Width = 1040;

    private readonly Control _root;
    private readonly Control _card;
    private readonly Label _title;
    private readonly Label _step;
    private readonly RichTextLabel _text;
    private readonly VBoxContainer _buttons;

    /// <summary>An answer (0..2) or the Continue button (-1).</summary>
    public event Action<int>? Picked;

    public bool IsOpen => GodotObject.IsInstanceValid(_root) && _root.Visible;

    /// <param name="parent">ShopStage.Popups.</param>
    public DatePanel(Control parent)
    {
        _root = new Control { Name = "Date", Size = new Vector2(ShopStage.W, ShopStage.H), Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        var dim = new ColorRect { Color = new Color(0.08f, 0.02f, 0.06f, 0.72f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(Width, 0) };
        var box = Theme.Box(Theme.PanelBg, Theme.Pink, 4, 22, 36);
        box.ShadowColor = new Color(1f, 0.3f, 0.6f, 0.35f);
        box.ShadowSize = 28;
        panel.AddThemeStyleboxOverride("panel", box);
        _card = panel;
        _root.AddChild(panel);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 18);
        panel.AddChild(col);

        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddChild(head);
        _title = Theme.MakeLabel(Loc.T("♥ A Date with the Merchant ♥", "♥ 与商人的约会 ♥"), 44, Theme.Pink, bold: true, outline: 8, outlineColor: Theme.Plum);
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(_title);
        _step = Theme.MakeLabel("", 26, Theme.Lilac, bold: true, outline: 0);
        _step.VerticalAlignment = VerticalAlignment.Center;
        head.AddChild(_step);

        _text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        if (Theme.Font != null) _text.AddThemeFontOverride("normal_font", Theme.Font);
        if (Theme.Bold != null) _text.AddThemeFontOverride("bold_font", Theme.Bold);
        _text.AddThemeFontSizeOverride("normal_font_size", 32);
        _text.AddThemeFontSizeOverride("bold_font_size", 32);
        _text.AddThemeFontSizeOverride("italics_font_size", 32);
        _text.AddThemeColorOverride("default_color", Theme.Ink);
        _text.AddThemeConstantOverride("line_separation", 8);
        col.AddChild(_text);

        _buttons = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _buttons.AddThemeConstantOverride("separation", 12);
        col.AddChild(_buttons);

        panel.Resized += () => panel.Position = new Vector2((ShopStage.W - panel.Size.X) / 2, Mathf.Max(110, (ShopStage.H - panel.Size.Y) / 2 - 60));
        parent.AddChild(_root);
    }

    /// <summary>One question: the situation and its answers.</summary>
    public void Ask(int round, int rounds, string situation, IReadOnlyList<string> answers)
    {
        Fill($"{round}/{rounds}", $"[i]{Escape(situation)}[/i]");
        for (var i = 0; i < answers.Count; i++)
        {
            var index = i;
            _buttons.AddChild(MakeButton($"{(char)('A' + i)}.  {answers[i]}", Theme.Pink.Darkened(0.35f), () => Picked?.Invoke(index)));
        }
        Open();
    }

    /// <summary>His reply, or the ending, with one button to go on.</summary>
    public void Say(string step, string bbcode, string button)
    {
        Fill(step, bbcode);
        _buttons.AddChild(MakeButton(button, Theme.PinkDeep, () => Picked?.Invoke(-1)));
        Open();
    }

    public void Hide()
    {
        if (!GodotObject.IsInstanceValid(_root) || !_root.Visible) return;
        var t = _root.CreateTween();
        t.TweenProperty(_root, "modulate:a", 0f, 0.25);
        t.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(_root)) _root.Visible = false; }));
    }

    public static string Escape(string s) => s.Replace("[", "[lb]");

    private void Fill(string step, string bbcode)
    {
        _step.Text = step;
        _text.Text = bbcode;
        foreach (var c in _buttons.GetChildren()) c.QueueFree();
    }

    private void Open()
    {
        if (_root.Visible) return;
        _root.Visible = true;
        _root.Modulate = new Color(1, 1, 1, 0);
        _card.PivotOffset = _card.Size / 2;
        _card.Scale = new Vector2(0.9f, 0.9f);
        var t = _root.CreateTween().SetParallel();
        t.TweenProperty(_root, "modulate:a", 1f, 0.3);
        t.TweenProperty(_card, "scale", Vector2.One, 0.35).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private static Button MakeButton(string text, Color bg, Action onPress)
    {
        var b = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        var box = Theme.Box(bg, Theme.Pink.Lightened(0.3f), 3, 16);
        box.ContentMarginLeft = box.ContentMarginRight = 24;
        box.ContentMarginTop = box.ContentMarginBottom = 12;
        Theme.StyleButton(b, box, 28, Theme.Ink);
        var pressed = false;
        b.Pressed += () =>
        {
            if (pressed) return; // one click per button, even if the panel is slow to refill
            pressed = true;
            onPress();
        };
        return b;
    }
}
