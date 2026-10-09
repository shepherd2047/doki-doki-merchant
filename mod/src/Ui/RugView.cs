using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace DokiDokiMerchant;

/// <summary>
/// The web game's rug on the left half of the room (style.css #rug/#goods, main.js SLOTS/renderGoods): the
/// original rug texture with the goods on it, each with a number badge, a coin price, sale tag and SOLD stamp.
/// Goods are the game's own NCard / NRelic / NPotion nodes (mouse ignored) under a transparent hit Control.
/// </summary>
internal sealed class RugView
{
    /// <summary>The player clicked an item (sold items and the hidden secret card can't be clicked).</summary>
    public event Action<Haggle.Item>? Clicked;

    // main.js: RUG offset and SLOTS (centres of each .good box, which is translate(-50%,-50%)).
    private static readonly Vector2 RugOffset = new(20, 100);
    private static readonly Dictionary<string, Vector2[]> Slots = new()
    {
        ["card"] = new Vector2[] { new(185, 230), new(448, 230), new(711, 230), new(974, 230) },
        ["relic"] = new Vector2[] { new(150, 640), new(300, 640) },
        ["potion"] = new Vector2[] { new(450, 640), new(600, 640) },
        ["service"] = new Vector2[] { new(790, 650) },
        ["secret"] = new Vector2[] { new(1010, 625) },
    };

    private const float CardScale = 194f / 300f; // web 598 * 0.325 = 194px; NCard.defaultSize is 300x422
    private const float PriceGap = 6, PriceH = 37;
    private const string ShopDir = "res://images/rooms/merchant_room/";

    private static readonly Color GoldGlow = new(1f, 230 / 255f, 120 / 255f, 0.95f);
    private static readonly Color BlueGlow = new("88ccff");
    private static readonly Color PurpleGlow = new(200 / 255f, 120 / 255f, 1f, 0.95f);
    private static readonly Color Strike = new("aaaaaa");

    private readonly Func<int> _gold;
    private readonly Control _rug;
    private readonly Control _goods;
    private readonly List<Good> _all = new();
    private readonly Dictionary<Haggle.Item, Good> _byItem = new();
    private Haggle.Item? _selected;

    /// <param name="parent">ShopStage.Rug (1920x1080 stage coordinates).</param>
    /// <param name="items">Everything on the rug in shelf order, including the hidden secret card (Hidden = true).</param>
    /// <param name="gold">The player's current gold, for red unaffordable prices.</param>
    public RugView(Control parent, IReadOnlyList<Haggle.Item> items, Func<int> gold)
    {
        _gold = gold;
        _rug = BuildRug();
        parent.AddChild(_rug);
        _goods = new Control { Name = "Goods", Size = new Vector2(ShopStage.W, ShopStage.H), MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(_goods);

        var used = new Dictionary<string, int>();
        foreach (var it in items)
        {
            var key = it.Secret ? "secret" : it.Kind;
            if (!Slots.TryGetValue(key, out var slots)) continue;
            var n = used.GetValueOrDefault(key);
            if (n >= slots.Length) continue; // more goods than the web layout has room for: skip
            used[key] = n + 1;
            try
            {
                var g = new Good(this, it, RugOffset + slots[n]);
                _all.Add(g);
                _byItem[it] = g;
            }
            catch (Exception e)
            {
                Log.Warn($"[Doki] RugView could not build shelf {it.Shelf}: {e.Message}");
            }
        }
        Refresh();
    }

    /// <summary>The rug drops in from above (0.7s quint ease-out), as when the web game opens.</summary>
    public void DropIn()
    {
        if (!GodotObject.IsInstanceValid(_rug) || !_rug.IsInsideTree()) return;
        _rug.Position = new Vector2(20, -1000);
        _goods.Position = new Vector2(0, -1080);
        var t = _rug.CreateTween().SetParallel();
        t.TweenProperty(_rug, "position", new Vector2(20, -30), 0.7).SetTrans(Tween.TransitionType.Quint).SetEase(Tween.EaseType.Out);
        t.TweenProperty(_goods, "position", Vector2.Zero, 0.7).SetTrans(Tween.TransitionType.Quint).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Re-read prices (entry.Cost; Agreed/AngerTax for strike-through), sold state, affordability.</summary>
    public void Refresh()
    {
        var gold = SafeGold();
        foreach (var g in _all)
        {
            try { g.Refresh(gold); }
            catch (Exception e) { Log.Warn($"[Doki] RugView refresh shelf {g.Item.Shelf}: {e.Message}"); }
        }
    }

    /// <summary>Highlight the item the detail popup is showing (null clears it).</summary>
    public void SetSelected(Haggle.Item? item)
    {
        _selected = item;
        foreach (var g in _all) g.SetSelected(ReferenceEquals(g.Item, item));
    }

    /// <summary>The Merchant is talking about this item: bounce it with a blue glow (web .pointed).</summary>
    public void Point(Haggle.Item item)
    {
        if (_byItem.TryGetValue(item, out var g)) g.Point();
    }

    /// <summary>The secret card was just stocked into item.Entry: rebuild its visual and rise it into view with the ♥ SECRET ♥ ribbon.</summary>
    public void RevealSecret(Haggle.Item item)
    {
        if (!_byItem.TryGetValue(item, out var g)) return;
        try { g.Reveal(SafeGold()); }
        catch (Exception e) { Log.Warn($"[Doki] RugView reveal: {e.Message}"); }
    }

    /// <summary>Centre of the item's slot in stage coordinates.</summary>
    public Vector2 SlotCenter(Haggle.Item item) => _byItem.TryGetValue(item, out var g) ? g.Center : Vector2.Zero;

    private int SafeGold()
    {
        try { return _gold(); }
        catch { return int.MaxValue; }
    }

    // ------------------------------------------------------------------------------------------- rug

    /// <summary>The web's shop_rug_full.png is the game's shop_rug.png columns 1000..1836 mirrored + as is.</summary>
    private static Control BuildRug()
    {
        var box = new Control { Name = "RugTex", Position = new Vector2(20, -30), Size = new Vector2(1160, 930), MouseFilter = Control.MouseFilterEnum.Ignore };
        // drop-shadow(0 10px 18px rgba(0,0,0,.6)) under the rug's body
        var shadowStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.35f), DrawCenter = true, ShadowColor = new Color(0, 0, 0, 0.6f), ShadowSize = 18, ShadowOffset = new Vector2(0, 10), AntiAliasing = true };
        shadowStyle.SetCornerRadiusAll(24);
        var shadow = new Panel { Position = new Vector2(30, 24), Size = new Vector2(1100, 878), MouseFilter = Control.MouseFilterEnum.Ignore };
        shadow.AddThemeStyleboxOverride("panel", shadowStyle);
        box.AddChild(shadow);

        var tex = Theme.Tex(ShopDir + "shop_rug.png");
        if (tex == null)
        {
            var p = new Panel { Size = box.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
            p.AddThemeStyleboxOverride("panel", Theme.Box(new Color("3f7464"), new Color("2b5246"), 6, 30));
            box.AddChild(p);
            return box;
        }
        var k = tex.GetWidth() / 1920f;
        var region = new Rect2(1000 * k, 0, 836 * k, tex.GetHeight());
        for (var i = 0; i < 2; i++)
        {
            var half = new TextureRect
            {
                Texture = new AtlasTexture { Atlas = tex, Region = region },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                FlipH = i == 0,
                Position = new Vector2(580 * i, 0),
                Size = new Vector2(580, 930),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            box.AddChild(half);
        }
        return box;
    }

    // ------------------------------------------------------------------------------------------- helpers

    private static void IgnoreMouse(Node n)
    {
        if (n is Control c)
        {
            c.MouseFilter = Control.MouseFilterEnum.Ignore;
            c.FocusMode = Control.FocusModeEnum.None;
        }
        foreach (var child in n.GetChildren()) IgnoreMouse(child);
    }

    /// <summary>Run <paramref name="a"/> a moment after <paramref name="n"/> is in the tree (lets game nodes lay out).</summary>
    private static void Later(Node n, double delay, Action a)
    {
        void Go()
        {
            if (!GodotObject.IsInstanceValid(n) || !n.IsInsideTree()) return;
            n.GetTree().CreateTimer(delay).Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(n)) return;
                try { a(); }
                catch (Exception e) { Log.Warn($"[Doki] RugView: {e.Message}"); }
            };
        }
        if (n.IsInsideTree()) Go();
        else n.Connect(Node.SignalName.TreeEntered, Callable.From(Go), (uint)GodotObject.ConnectFlags.OneShot);
    }

    private static void TextShadow(Label l, Color c, int dy, int blur)
    {
        l.AddThemeColorOverride("font_shadow_color", c);
        l.AddThemeConstantOverride("shadow_offset_x", 0);
        l.AddThemeConstantOverride("shadow_offset_y", dy);
        l.AddThemeConstantOverride("shadow_outline_size", blur);
    }

    private static Panel Glow(Rect2 r, Color c, int size, int radius)
    {
        var s = new StyleBoxFlat { BgColor = c with { A = 0 }, DrawCenter = false, ShadowColor = c, ShadowSize = size, AntiAliasing = true };
        s.SetCornerRadiusAll(radius);
        var p = new Panel { Position = r.Position, Size = r.Size, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        p.AddThemeStyleboxOverride("panel", s);
        return p;
    }

    private static object? CurrentModel(MerchantEntry e) => e switch
    {
        MerchantCardEntry c => c.CreationResult?.Card,
        MerchantRelicEntry r => r.Model,
        MerchantPotionEntry p => p.Model,
        MerchantCardRemovalEntry => "removal",
        _ => null,
    };

    private const string RoundedShader = @"shader_type canvas_item;
uniform vec2 size = vec2(100.0, 30.0);
uniform float radius = 6.0;
void fragment() {
    vec2 p = UV * size;
    vec2 q = abs(p - size * 0.5) - (size * 0.5 - vec2(radius));
    float d = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - radius;
    COLOR.a *= clamp(0.5 - d, 0.0, 1.0);
}";

    // ------------------------------------------------------------------------------------------- one good

    private sealed class Good
    {
        public readonly Haggle.Item Item;
        public readonly Vector2 Center;
        private readonly RugView _view;
        private readonly Vector2 _boxSize;
        private readonly Rect2 _visRect; // the item art inside the box
        private readonly Control _box, _hover, _point, _art, _hit, _tipAnchor;
        private readonly Panel _glowSel, _glowPoint, _glowSecret;
        private readonly Label _price, _strike;
        private readonly TextureRect? _sale;
        private readonly Control _stamp;
        private Control? _ribbon;
        private Control? _visual;
        private object? _model;
        private bool _built, _revealed, _hovered, _sold;
        private int _lastCost;
        private Tween? _hoverTween, _pointTween;

        public Good(RugView view, Haggle.Item item, Vector2 center)
        {
            _view = view;
            Item = item;
            Center = center;
            var kind = item.Kind;
            Vector2 vis = kind switch
            {
                "card" => new Vector2(194, 422 * CardScale),
                "relic" => new Vector2(84, 84),
                "potion" => new Vector2(64, 64),
                _ => new Vector2(171, 215),
            };
            var boxW = kind == "card" ? 194f : 140f;
            _boxSize = new Vector2(boxW, vis.Y + PriceGap + PriceH);
            _visRect = new Rect2(new Vector2((boxW - vis.X) / 2, 0), vis);

            _box = new Control
            {
                Name = $"Good{item.Shelf}",
                Position = center - _boxSize / 2,
                Size = _boxSize,
                PivotOffset = _boxSize / 2,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            };
            view._goods.AddChild(_box);
            _hover = Sub(_box, "Hover");
            _point = Sub(_hover, "Point");

            var glowRect = kind == "card" ? _visRect.Grow(-2) : _visRect.Grow(-vis.X * 0.12f);
            var radius = kind == "card" ? 14 : (int)(glowRect.Size.X / 2);
            if (kind == "service") { glowRect = _visRect.Grow(-20); radius = (int)(glowRect.Size.X / 2); }
            _glowSecret = Glow(glowRect, PurpleGlow, 18, radius);
            _glowSel = Glow(glowRect, GoldGlow, 14, radius);
            _glowPoint = Glow(glowRect, BlueGlow, 20, radius);
            _point.AddChild(_glowSecret);
            _point.AddChild(_glowSel);
            _point.AddChild(_glowPoint);
            _glowPoint.Visible = true;
            _glowPoint.Modulate = new Color(1, 1, 1, 0);

            _art = Sub(_point, "Art");

            // price row: coin, struck list price, shown price
            var row = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
                Position = new Vector2(boxW / 2 - 160, vis.Y + PriceGap),
                Size = new Vector2(320, PriceH),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            row.AddThemeConstantOverride("separation", 6);
            var coinTex = Theme.Tex("res://images/atlases/ui_atlas.sprites/top_bar/top_bar_gold.tres");
            if (coinTex != null)
            {
                row.AddChild(new TextureRect
                {
                    Texture = coinTex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = new Vector2(35, PriceH),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            }
            _strike = Theme.MakeLabel("", 24, Strike, bold: true, outline: 0);
            _strike.VerticalAlignment = VerticalAlignment.Center;
            TextShadow(_strike, Colors.Black, 2, 3);
            _strike.Draw += () =>
            {
                var s = _strike.Size;
                _strike.DrawLine(new Vector2(0, s.Y * 0.52f), new Vector2(s.X, s.Y * 0.52f), Strike, 2.5f);
            };
            row.AddChild(_strike);
            _price = Theme.MakeLabel("", 27, Theme.Ink, bold: true, outline: 0);
            _price.VerticalAlignment = VerticalAlignment.Center;
            TextShadow(_price, Colors.Black, 2, 3);
            row.AddChild(_price);
            _point.AddChild(row);

            // sale tag at the top-right
            if (item.Entry is MerchantCardEntry)
            {
                var saleTex = Theme.Tex(ShopDir + "shop_sales_tag.png");
                if (saleTex != null)
                {
                    var h = 64f * saleTex.GetHeight() / Math.Max(1, saleTex.GetWidth());
                    _sale = new TextureRect
                    {
                        Texture = saleTex,
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        Position = new Vector2(boxW + 14 - 64, -14),
                        Size = new Vector2(64, h),
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                        Visible = false,
                    };
                    _point.AddChild(_sale);
                }
            }

            // number badge at the top-left
            var badge = new Panel { Position = new Vector2(-10, -10), Size = new Vector2(34, 34), MouseFilter = Control.MouseFilterEnum.Ignore };
            badge.AddThemeStyleboxOverride("panel", Theme.Box(new Color("2a2030"), Theme.Brass, 2, 17));
            var no = Theme.MakeLabel(item.Shelf.ToString(), 20, Theme.Ink, outline: 0);
            no.HorizontalAlignment = HorizontalAlignment.Center;
            no.VerticalAlignment = VerticalAlignment.Center;
            no.Size = badge.Size;
            badge.AddChild(no);
            _point.AddChild(badge);

            // SOLD stamp
            _stamp = BuildStamp();
            _point.AddChild(_stamp);

            if (item.Secret) BuildRibbon();

            // the hit area: the whole box, plus the art where it overflows (card removal)
            var hitRect = new Rect2(Vector2.Zero, _boxSize).Merge(_visRect);
            _hit = new Control
            {
                Name = "Hit",
                Position = hitRect.Position,
                Size = hitRect.Size,
                MouseFilter = Control.MouseFilterEnum.Stop,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            };
            _point.AddChild(_hit);
            // Hover tips owner: same screen position as the hit, with Size*Scale equal to its on-screen size
            // (NHoverTipSet.SetAlignment uses the owner's local scale, which ignores the stage's fit scale).
            _tipAnchor = new Control { Name = "TipAnchor", Size = hitRect.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
            _hit.AddChild(_tipAnchor);
            _hit.MouseEntered += OnEnter;
            _hit.MouseExited += OnExit;
            _hit.GuiInput += OnInput;
            // The stage is hidden while the map / deck view / pause menu is up: drop the hover and its tips.
            _hit.VisibilityChanged += () => { if (_hovered && !_hit.IsVisibleInTree()) OnExit(); };
            _box.TreeExiting += HideTips;

            if (!item.Hidden) Build();
        }

        private bool Shown => !Item.Hidden || _revealed;
        private bool Clickable => Shown && !_sold;

        private Control Sub(Control parent, string name)
        {
            var c = new Control { Name = name, Size = _boxSize, PivotOffset = _boxSize / 2, MouseFilter = Control.MouseFilterEnum.Ignore };
            parent.AddChild(c);
            return c;
        }

        private Control BuildStamp()
        {
            var pc = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            var s = new StyleBoxFlat { DrawCenter = false, BorderColor = Theme.Red };
            s.SetBorderWidthAll(4);
            s.ContentMarginLeft = s.ContentMarginRight = 12;
            pc.AddThemeStyleboxOverride("panel", s);
            var l = Theme.MakeLabel("SOLD", 46, Theme.Red, bold: true, outline: 0);
            pc.AddChild(l);
            var size = pc.GetCombinedMinimumSize();
            if (size.X < 10) size = new Vector2(150, 60);
            pc.Size = size;
            pc.PivotOffset = size / 2;
            pc.Position = new Vector2(_boxSize.X * 0.5f, _boxSize.Y * 0.4f) - size / 2;
            pc.RotationDegrees = -14;
            return pc;
        }

        private void BuildRibbon()
        {
            const string text = "♥ SECRET ♥";
            var font = Theme.Bold ?? Theme.Font;
            var tw = font?.GetStringSize(text, HorizontalAlignment.Left, -1, 22).X ?? 130f;
            var th = font != null ? font.GetHeight(22) : 28f;
            var size = new Vector2(tw + 32, th + 4);
            var pill = new TextureRect
            {
                Name = "SecretRibbon",
                Texture = Theme.VGradient(Theme.Lilac, new Color("8a4fd8"), 8, 64),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Size = size,
                PivotOffset = size / 2,
                Position = new Vector2(_boxSize.X / 2 - size.X / 2, -30),
                RotationDegrees = -4,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            var mat = new ShaderMaterial { Shader = new Shader { Code = RoundedShader } };
            mat.SetShaderParameter("size", size);
            mat.SetShaderParameter("radius", 6f);
            pill.Material = mat;
            var l = Theme.MakeLabel(text, 22, Colors.White, bold: true, outline: 0);
            TextShadow(l, new Color("3a1468"), 2, 2);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Size = size;
            l.UseParentMaterial = false;
            pill.AddChild(l);
            _point.AddChild(pill);
            _ribbon = pill;
        }

        // ---------------------------------------------------------------------- the item art

        private void Build()
        {
            if (_visual != null && GodotObject.IsInstanceValid(_visual)) _visual.QueueFree();
            _visual = null;
            _model = CurrentModel(Item.Entry);
            _built = true;
            _box.Visible = true;
            Control? v = null;
            try { v = BuildGameNode(); }
            catch (Exception e) { Log.Warn($"[Doki] RugView: game node for shelf {Item.Shelf} failed: {e.Message}"); }
            v ??= BuildFallback();
            _visual = v;
            _art.AddChild(v);
            IgnoreMouse(v);
        }

        private Control? BuildGameNode()
        {
            var c = _visRect.GetCenter();
            switch (Item.Entry)
            {
                case MerchantCardEntry { CreationResult: { } cr }:
                {
                    var card = NCard.Create(cr.Card);
                    if (card == null) return null;
                    var holder = new Control { Name = "CardHolder", Position = c, MouseFilter = Control.MouseFilterEnum.Ignore };
                    card.Scale = new Vector2(CardScale, CardScale);
                    void Update()
                    {
                        try
                        {
                            card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                            IgnoreMouse(card);
                        }
                        catch (Exception e) { Log.Warn($"[Doki] RugView card visuals: {e.Message}"); }
                    }
                    card.Connect(Node.SignalName.Ready, Callable.From(Update), (uint)GodotObject.ConnectFlags.OneShot);
                    holder.AddChild(card);
                    Later(holder, 0.05, () => IgnoreMouse(card));
                    return holder;
                }
                case MerchantRelicEntry { Model: { } relic }:
                {
                    var node = NRelic.Create(relic, NRelic.IconSize.Small);
                    if (node == null) return null;
                    return FitHolder(node, () => node.IsNodeReady() ? node.Icon : null, 84);
                }
                case MerchantPotionEntry { Model: { } potion }:
                {
                    var node = NPotion.Create(potion);
                    if (node == null) return null;
                    return FitHolder(node, () => node.IsNodeReady() ? node.Image : null, 64);
                }
                case MerchantCardRemovalEntry:
                {
                    var tex = Theme.Tex(ShopDir + "card_removal_00.png") ?? Theme.Tex(ShopDir + "card_removal.png");
                    if (tex == null) return null;
                    return new TextureRect
                    {
                        Texture = tex,
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        Position = _visRect.Position,
                        Size = _visRect.Size,
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                    };
                }
            }
            return null;
        }

        /// <summary>Holds a game node centred on the art rect; once laid out, scales its icon to <paramref name="target"/> px.</summary>
        private Control FitHolder(Control node, Func<TextureRect?> icon, float target)
        {
            var holder = new Control { Name = "IconHolder", Position = _visRect.GetCenter(), MouseFilter = Control.MouseFilterEnum.Ignore };
            node.Modulate = new Color(1, 1, 1, 0);
            holder.AddChild(node);
            Later(holder, 0.03, () =>
            {
                if (!GodotObject.IsInstanceValid(node)) return;
                Rect2 r;
                var ic = icon();
                if (ic != null && GodotObject.IsInstanceValid(ic) && ic.Size.X > 1)
                {
                    var inv = node.GetGlobalTransform().AffineInverse();
                    var g = ic.GetGlobalTransform();
                    var p0 = inv * (g * Vector2.Zero);
                    var p1 = inv * (g * ic.Size);
                    r = new Rect2(p0, p1 - p0).Abs();
                }
                else r = new Rect2(Vector2.Zero, node.Size.X > 1 ? node.Size : new Vector2(target, target));
                var k = target / Mathf.Max(1f, Mathf.Max(r.Size.X, r.Size.Y));
                node.Scale = new Vector2(k, k);
                node.Position = -r.GetCenter() * k;
                node.Modulate = Colors.White;
                IgnoreMouse(node);
            });
            return holder;
        }

        private Control BuildFallback()
        {
            Texture2D? tex = null;
            try
            {
                tex = Item.Entry switch
                {
                    MerchantRelicEntry { Model: { } r } => r.Icon,
                    MerchantPotionEntry { Model: { } pm } => pm.Image,
                    _ => null,
                };
            }
            catch { }
            if (tex != null)
            {
                return new TextureRect
                {
                    Texture = tex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Position = _visRect.Position,
                    Size = _visRect.Size,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
            }
            var p = new Panel { Position = _visRect.Position, Size = _visRect.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
            p.AddThemeStyleboxOverride("panel", Theme.Box(Theme.PanelBg, Theme.Brass, 3, 12));
            var l = Theme.MakeLabel(ItemText.Name(Item), Item.Kind == "card" ? 20 : 14, Theme.Ink, bold: true, outline: 4);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Position = new Vector2(4, 4);
            l.Size = _visRect.Size - new Vector2(8, 8);
            p.AddChild(l);
            return p;
        }

        // ---------------------------------------------------------------------- state

        public void Refresh(int gold)
        {
            if (!Shown)
            {
                _box.Visible = false;
                return;
            }
            var model = CurrentModel(Item.Entry);
            if (!_built || (model != null && !ReferenceEquals(model, _model) && !(model is string && _model is string)))
                Build(); // first show, or The Courier restocked this slot with something new
            _box.Visible = true;

            _sold = !Item.Entry.IsStocked;
            if (!_sold)
            {
                try { _lastCost = Item.Entry.Cost; }
                catch { _lastCost = Item.ListPrice; }
            }
            var cost = _lastCost;
            var anger = Haggle.AngerTax.Contains(Item.Entry);
            var deal = Item.Agreed != null && !anger;
            var struck = !_sold && (Item.Agreed != null || anger) && Item.ListPrice != cost;
            _strike.Text = struck ? Item.ListPrice.ToString() : "";
            _strike.Visible = struck;
            _strike.QueueRedraw();
            _price.Text = cost.ToString();
            var color = Theme.Ink;
            if (!_sold && cost > gold) color = Theme.Red;
            else if (struck) color = deal ? Theme.Green : Theme.Red;
            _price.AddThemeColorOverride("font_color", color);

            var onSale = false;
            try { onSale = Item.Entry is MerchantCardEntry { IsOnSale: true }; }
            catch { }
            if (_sale != null) _sale.Visible = onSale && !_sold;

            _box.Modulate = new Color(1, 1, 1, _sold ? 0.25f : 1f);
            _stamp.Visible = _sold;
            _glowSecret.Visible = Item.Secret && !_sold;
            _hit.MouseFilter = Clickable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
            if (!Clickable && _hovered) OnExit();
        }

        public void SetSelected(bool on) => _glowSel.Visible = on && _box.Visible;

        public void Point()
        {
            if (!Shown || !_box.Visible) return;
            _pointTween?.Kill();
            _point.Position = Vector2.Zero;
            _point.Scale = Vector2.One;
            _glowPoint.Modulate = new Color(1, 1, 1, 0);
            var t = _point.CreateTween().SetLoops(3);
            _pointTween = t;
            Step(t, new Vector2(0, -14), 1.08f, 1f);
            Step(t, Vector2.Zero, 1f, 0f);
        }

        private void Step(Tween t, Vector2 pos, float scale, float glow)
        {
            t.TweenProperty(_point, "position", pos, 0.3).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            t.Parallel().TweenProperty(_point, "scale", new Vector2(scale, scale), 0.3).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            t.Parallel().TweenProperty(_glowPoint, "modulate:a", glow, 0.3).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        public void Reveal(int gold)
        {
            _revealed = true;
            _built = false; // the entry was repopulated: rebuild from it
            if (_ribbon == null && Item.Secret) BuildRibbon();
            Refresh(gold);
            if (!_built) Build();
            _box.Visible = true;
            _glowSel.Visible = ReferenceEquals(_view._selected, Item);
            if (!_box.IsInsideTree()) return;
            var home = Center - _boxSize / 2;
            _box.Position = home + new Vector2(0, _boxSize.Y * 0.8f);
            _box.Scale = new Vector2(0.4f, 0.4f);
            var a = _box.Modulate.A;
            _box.Modulate = _box.Modulate with { A = 0 };
            var t = _box.CreateTween().SetParallel();
            t.TweenProperty(_box, "position", home, 1.0).SetTrans(Tween.TransitionType.Quint).SetEase(Tween.EaseType.Out);
            t.TweenProperty(_box, "scale", Vector2.One, 1.0).SetTrans(Tween.TransitionType.Quint).SetEase(Tween.EaseType.Out);
            t.TweenProperty(_box, "modulate:a", a, 1.0).SetTrans(Tween.TransitionType.Quint).SetEase(Tween.EaseType.Out);
        }

        // ---------------------------------------------------------------------- mouse

        private void OnEnter()
        {
            if (!Clickable) return;
            _hovered = true;
            HoverTo(new Vector2(0, -6), 1.04f);
            ShowTips();
        }

        private void OnExit()
        {
            _hovered = false;
            HoverTo(Vector2.Zero, 1f);
            HideTips();
        }

        private void HoverTo(Vector2 pos, float scale)
        {
            if (!_hover.IsInsideTree()) return;
            _hoverTween?.Kill();
            _hoverTween = _hover.CreateTween().SetParallel();
            _hoverTween.TweenProperty(_hover, "position", pos, 0.12).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
            _hoverTween.TweenProperty(_hover, "scale", new Vector2(scale, scale), 0.12).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        }

        private void OnInput(InputEvent ev)
        {
            if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
            _hit.AcceptEvent();
            // A release still inside the item counts (not _hovered: the item can become clickable under a resting
            // mouse, e.g. the secret reveal or a Courier restock, without a MouseEntered).
            if (mb.Pressed || !Clickable || !new Rect2(Vector2.Zero, _hit.Size).HasPoint(mb.Position)) return;
            HideTips();
            _view.Clicked?.Invoke(Item);
        }

        private IEnumerable<IHoverTip>? Tips()
        {
            switch (Item.Entry)
            {
                case MerchantCardEntry { CreationResult: { } cr }: return cr.Card.HoverTips;
                case MerchantRelicEntry { Model: { } r }: return r.HoverTips;
                case MerchantPotionEntry { Model: { } p }: return p.HoverTips;
                case MerchantCardRemovalEntry:
                    var desc = new LocString("merchant_room", "MERCHANT.cardRemovalService.description");
                    desc.Add("Amount", (decimal)MerchantCardRemovalEntry.PriceIncrease);
                    return new IHoverTip[] { new HoverTip(new LocString("merchant_room", "MERCHANT.cardRemovalService.title"), desc) };
            }
            return null;
        }

        private void ShowTips()
        {
            try
            {
                HideTips();
                var tips = Tips()?.ToList();
                if (tips == null || tips.Count == 0) return;
                var gs = _hit.GetGlobalTransform().Scale;
                _tipAnchor.Scale = new Vector2(Mathf.Abs(gs.X), Mathf.Abs(gs.Y));
                var set = NHoverTipSet.CreateAndShow(_tipAnchor, tips);
                if (set == null) return;
                LiftAboveStage(set);
                set.SetAlignment(_tipAnchor, HoverTip.GetHoverTipAlignment(_tipAnchor));
            }
            catch (Exception e)
            {
                Log.Warn($"[Doki] RugView hover tip: {e.Message}");
            }
        }

        /// <summary>
        /// The game adds hover tips to NGame's HoverTipsContainer on the root canvas (layer 0), which our stage's
        /// CanvasLayer (and its opaque rug) covers. Move the tip set into the stage's CanvasLayer, on top. Canvas
        /// coordinates are the same in both, and NHoverTipSet.Remove frees it wherever it is.
        /// </summary>
        private void LiftAboveStage(Node set)
        {
            var mine = LayerOf(_tipAnchor);
            if (mine == null || set.GetParent() == null) return; // not added yet (deferred): leave it
            var theirs = LayerOf(set);
            if (theirs != null && (theirs == mine || theirs.Layer > mine.Layer)) return;
            set.Reparent(mine, false);
        }

        private static CanvasLayer? LayerOf(Node n)
        {
            for (var p = n.GetParent(); p != null; p = p.GetParent())
                if (p is CanvasLayer cl) return cl;
            return null;
        }

        private void HideTips()
        {
            try
            {
                if (GodotObject.IsInstanceValid(_tipAnchor)) NHoverTipSet.Remove(_tipAnchor);
            }
            catch { }
        }
    }
}
