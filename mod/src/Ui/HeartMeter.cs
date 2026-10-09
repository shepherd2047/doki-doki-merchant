using Godot;

namespace DokiDokiMerchant;

/// <summary>
/// The heart meter above the Merchant (style.css #affection): a row of hearts, a pink bar and "Lv 2 · Customer";
/// plus the glow on the Merchant himself (pink blush at Favourite and above, blue while he speaks).
/// </summary>
internal sealed class HeartMeter
{
    private const float BlockW = 290;
    private const float HeartSize = 40, HeartSpacing = 4, HeartRowH = 40;
    private const float BarX = 20, BarY = HeartRowH + 6, BarW = BlockW - 40, BarH = 16; // 12px + 2px border
    private const float LevelY = BarY + BarH + 6;

    // web #merchant: left 1204, top 470, 263x331. The aura is a soft ellipse a bit bigger than that rect.
    private static readonly Vector2 MerchantCentre = new(1204 + 263 / 2f, 470 + 331 / 2f);
    private static readonly Vector2 GlowSize = new(470, 540);

    private static readonly Color SpeakColor = new(140 / 255f, 200 / 255f, 1f);
    private static readonly Color BlushColor = new(1f, 110 / 255f, 170 / 255f);
    private static readonly Color BothColor = new(1f, 150 / 255f, 200 / 255f);

    private readonly Control _parent;
    private readonly Control _block;
    private readonly Control _heartRow;
    private readonly List<Label> _hearts = new();
    private readonly TextureRect _fill;
    private readonly Label _level;

    private readonly CanvasItem? _merchant;
    private readonly Color _merchantOrigSelfModulate = Colors.White;
    private readonly TextureRect? _glow;
    private readonly bool _glowInRoom; // sibling behind the merchant (true) or additive overlay in the Hud (false)
    private Tween? _glowTween, _tintTween, _fillTween, _levelTween;
    private SceneTree? _tree;
    private bool _cleaned;

    private string _heartsText = "";
    private int _levelNo = -1;
    private float _progress = -1;
    private bool _blush, _speaking;

    /// <param name="parent">ShopStage.Hud.</param>
    /// <param name="merchantVisual">The room's Merchant character node (CanvasItem) to tint, or null.</param>
    public HeartMeter(Control parent, CanvasItem? merchantVisual)
    {
        _parent = parent;
        _block = new Control
        {
            Name = "HeartMeter",
            Position = new Vector2(1200, 372),
            Size = new Vector2(BlockW, LevelY + 32),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        _heartRow = new Control { Name = "Hearts", Size = new Vector2(BlockW, HeartRowH), MouseFilter = Control.MouseFilterEnum.Ignore };
        _block.AddChild(_heartRow);

        // Bar: dark rounded track with a light border; the fill is clipped to the inner rounded rect.
        var track = new Panel { Name = "Bar", Position = new Vector2(BarX, BarY), Size = new Vector2(BarW, BarH), MouseFilter = Control.MouseFilterEnum.Ignore };
        track.AddThemeStyleboxOverride("panel", Theme.Box(new Color(0, 0, 0, 0.55f), new Color(1, 1, 1, 0.4f), 2, 8));
        _block.AddChild(track);
        var mask = new Panel
        {
            Name = "Mask",
            Position = new Vector2(2, 2),
            Size = new Vector2(BarW - 4, BarH - 4),
            ClipChildren = CanvasItem.ClipChildrenMode.Only,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        mask.AddThemeStyleboxOverride("panel", Theme.Box(Colors.White, null, 0, 6));
        track.AddChild(mask);
        _fill = new TextureRect
        {
            Name = "Fill",
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = new[] { Theme.Pink, Theme.PinkDeep }, Offsets = new[] { 0f, 1f } },
                FillFrom = new Vector2(0, 0),
                FillTo = new Vector2(1, 0),
                Width = 64,
                Height = 8,
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size = new Vector2(0, BarH - 4),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        mask.AddChild(_fill);

        _level = Theme.MakeLabel("", 24, new Color("ffd6e8"), bold: true, outline: 3);
        AddShadow(_level);
        _level.HorizontalAlignment = HorizontalAlignment.Center;
        _level.Position = new Vector2(0, LevelY);
        _level.Size = new Vector2(BlockW, 30);
        _level.PivotOffset = new Vector2(BlockW / 2, 15);
        _block.AddChild(_level);

        parent.AddChild(_block);

        // Merchant glow and tint.
        if (merchantVisual != null && GodotObject.IsInstanceValid(merchantVisual))
        {
            _merchant = merchantVisual;
            _merchantOrigSelfModulate = merchantVisual.SelfModulate;
            _glow = MakeGlow();
            var mParent = merchantVisual.GetParent();
            if (mParent is CanvasItem ci && mParent is not Container && merchantVisual.IsInsideTree())
            {
                _glowInRoom = true;
                _glow.ZIndex = merchantVisual.ZIndex;
                _glow.ZAsRelative = merchantVisual.ZAsRelative;
                ci.AddChild(_glow);
                ci.MoveChild(_glow, merchantVisual.GetIndex()); // just behind the merchant
            }
        }
        else
        {
            _glow = MakeGlow();
        }
        if (!_glowInRoom && _glow != null)
        {
            // Our Hud is above the room, so an additive low-alpha aura that brightens rather than covers.
            _glow.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            parent.AddChild(_glow);
            parent.MoveChild(_glow, 0);
        }
        PlaceGlow();

        _tree = parent.GetTree();
        if (_tree != null) _tree.ProcessFrame += OnFrame;
        parent.TreeExiting += Cleanup;
        _block.TreeExiting += Cleanup;
    }

    /// <param name="hearts">e.g. "♥♥♡♡♡" (Affection.Hearts).</param>
    /// <param name="progress">0..1 bar fill (Affection.Value / 100).</param>
    /// <param name="levelNo">1..5.</param>
    /// <param name="levelName">Stranger, Customer, Regular, Favourite, Darling.</param>
    /// <param name="blush">Favourite or above.</param>
    public void Set(string hearts, float progress, int levelNo, string levelName, bool blush)
    {
        if (!GodotObject.IsInstanceValid(_block)) return;
        SetHearts(hearts ?? "");

        progress = Mathf.Clamp(progress, 0, 1);
        if (!Mathf.IsEqualApprox(progress, _progress))
        {
            var first = _progress < 0;
            _progress = progress;
            var w = (BarW - 4) * progress;
            _fillTween?.Kill();
            if (first) _fill.Size = new Vector2(w, _fill.Size.Y);
            else
            {
                _fillTween = _block.CreateTween();
                _fillTween.TweenProperty(_fill, "size:x", w, 0.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            }
        }

        _level.Text = $"Lv {levelNo} · {levelName}";
        if (_levelNo != levelNo)
        {
            var first = _levelNo < 0;
            _levelNo = levelNo;
            if (!first)
            {
                _levelTween?.Kill();
                _level.Scale = new Vector2(1.3f, 1.3f);
                _levelTween = _block.CreateTween();
                _levelTween.TweenProperty(_level, "scale", Vector2.One, 0.45).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            }
        }

        if (blush != _blush)
        {
            _blush = blush;
            UpdateGlow();
        }
    }

    public void SetSpeaking(bool speaking)
    {
        if (speaking == _speaking) return;
        _speaking = speaking;
        UpdateGlow();
    }

    /// <summary>Where "+N ♥" pops should rise from, in stage coordinates.</summary>
    public Vector2 PopAnchor => new(1345, 440);

    // ---------------------------------------------------------------- hearts

    private void SetHearts(string text)
    {
        if (text == _heartsText) return;
        var old = _heartsText;
        var firstSet = _hearts.Count == 0 && old.Length == 0;
        _heartsText = text;

        if (_hearts.Count != text.Length)
        {
            foreach (var l in _hearts) l.QueueFree();
            _hearts.Clear();
            foreach (var ch in text)
            {
                var l = Theme.MakeLabel(ch.ToString(), (int)HeartSize, Theme.Pink, outline: 4);
                AddShadow(l);
                l.HorizontalAlignment = HorizontalAlignment.Center;
                l.VerticalAlignment = VerticalAlignment.Center;
                _heartRow.AddChild(l);
                _hearts.Add(l);
            }
        }
        for (var i = 0; i < text.Length; i++) _hearts[i].Text = text[i].ToString();
        LayoutHearts();

        if (firstSet) return;
        for (var i = 0; i < text.Length; i++)
        {
            var nowFull = text[i] == '♥';
            var wasFull = i < old.Length && old[i] == '♥';
            if (nowFull && !wasFull) Pop(_hearts[i]);
        }
    }

    private void LayoutHearts()
    {
        var font = Theme.Font;
        var widths = new float[_hearts.Count];
        float total = 0;
        for (var i = 0; i < _hearts.Count; i++)
        {
            var w = font != null ? font.GetStringSize(_hearts[i].Text, HorizontalAlignment.Left, -1, (int)HeartSize).X : HeartSize * 0.9f;
            widths[i] = Mathf.Max(w, 8);
            total += widths[i];
        }
        total += HeartSpacing * Mathf.Max(0, _hearts.Count - 1);
        var x = (BlockW - total) / 2;
        const float h = HeartRowH + 12; // glyph line box is taller than the CSS line-height 1 row
        for (var i = 0; i < _hearts.Count; i++)
        {
            var l = _hearts[i];
            l.Position = new Vector2(x, (HeartRowH - h) / 2);
            l.Size = new Vector2(widths[i], h);
            l.PivotOffset = new Vector2(widths[i] / 2, h / 2);
            x += widths[i] + HeartSpacing;
        }
    }

    private void Pop(Label l)
    {
        l.Scale = new Vector2(1.4f, 1.4f);
        var t = l.CreateTween();
        t.TweenProperty(l, "scale", Vector2.One, 0.3).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    private static void AddShadow(Label l)
    {
        // CSS text-shadow: 0 2px 3px #000
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        l.AddThemeConstantOverride("shadow_offset_x", 0);
        l.AddThemeConstantOverride("shadow_offset_y", 2);
        l.AddThemeConstantOverride("shadow_outline_size", 3);
    }

    // ---------------------------------------------------------------- merchant glow

    private static TextureRect MakeGlow() => new()
    {
        Name = "DokiMerchantGlow",
        Texture = new GradientTexture2D
        {
            Gradient = new Gradient
            {
                Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.6f), new Color(1, 1, 1, 0) },
                Offsets = new[] { 0f, 0.45f, 1f },
            },
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 128,
            Height = 128,
        },
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        Size = GlowSize,
        PivotOffset = GlowSize / 2,
        Modulate = new Color(1, 1, 1, 0),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>Keeps the glow on the merchant's on-screen rect (stage coords mapped into the glow's parent).</summary>
    private void PlaceGlow()
    {
        if (_glow == null || !GodotObject.IsInstanceValid(_glow) || !GodotObject.IsInstanceValid(_parent) || !_parent.IsInsideTree()) return;
        var topLeft = MerchantCentre - GlowSize / 2;
        if (!_glowInRoom)
        {
            _glow.Position = topLeft;
            return;
        }
        if (_glow.GetParent() is not CanvasItem gp || !gp.IsInsideTree()) return;
        var t = gp.GetGlobalTransformWithCanvas().AffineInverse() * _parent.GetGlobalTransformWithCanvas();
        _glow.Position = t * topLeft;
        _glow.PivotOffset = Vector2.Zero;
        _glow.Scale = t.Scale;
        _glow.Rotation = t.Rotation;
    }

    private void OnFrame()
    {
        if (_cleaned) return;
        if (_glow != null && GodotObject.IsInstanceValid(_glow) && _glow.Modulate.A > 0.001f) PlaceGlow();
    }

    private void UpdateGlow()
    {
        if (_cleaned || !GodotObject.IsInstanceValid(_block)) return;
        Color glow;
        Color tint;
        if (_blush && _speaking)
        {
            glow = BothColor with { A = 0.9f };
            tint = new Color(1f, 0.9f, 0.95f);
        }
        else if (_blush)
        {
            glow = BlushColor with { A = 0.7f };
            tint = new Color(1f, 0.93f, 0.96f);
        }
        else if (_speaking)
        {
            glow = SpeakColor with { A = 0.65f };
            tint = new Color(0.96f, 0.99f, 1f);
        }
        else
        {
            glow = new Color(1, 1, 1, 0);
            tint = Colors.White;
        }
        if (!_glowInRoom) glow.A *= 0.4f; // additive over the sprite: keep it light so it doesn't wash him out

        if (_glow != null && GodotObject.IsInstanceValid(_glow))
        {
            PlaceGlow();
            _glowTween?.Kill();
            _glowTween = _block.CreateTween();
            _glowTween.TweenProperty(_glow, "modulate", glow, 0.2);
        }
        if (_merchant != null && GodotObject.IsInstanceValid(_merchant))
        {
            _tintTween?.Kill();
            _tintTween = _block.CreateTween();
            _tintTween.TweenProperty(_merchant, "self_modulate", _merchantOrigSelfModulate * tint, 0.2);
        }
    }

    private void Cleanup()
    {
        if (_cleaned) return;
        _cleaned = true;
        if (_tree != null && GodotObject.IsInstanceValid(_tree)) _tree.ProcessFrame -= OnFrame;
        _glowTween?.Kill();
        _tintTween?.Kill();
        if (_merchant != null && GodotObject.IsInstanceValid(_merchant)) _merchant.SelfModulate = _merchantOrigSelfModulate;
        if (_glowInRoom && _glow != null && GodotObject.IsInstanceValid(_glow)) _glow.QueueFree();
    }
}
