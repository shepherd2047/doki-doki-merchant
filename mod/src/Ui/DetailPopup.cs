using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace DokiDokiMerchant;

/// <summary>
/// The item detail panel (style.css #detail, main.js renderDetail): art, "#N Name", kind line, description, a
/// "Buy for N gold" button and a round close button. Closes with ×, Escape, or a click on empty space.
/// </summary>
internal sealed class DetailPopup
{
    /// <summary>Buy pressed for this item. Doki runs the game's own purchase and calls Refresh/Hide.</summary>
    public event Action<Haggle.Item>? BuyPressed;
    /// <summary>Closed with the × button, Escape or a click outside.</summary>
    public event Action? Closed;

    private const float Width = 620, Pad = 24, Gap = 20, AnimTime = 0.12f;
    private static readonly Color Meta = new("bbbbbb");
    private static readonly Color BtnBg = new("30587a");      // between #3b6b8f and #24435c
    private static readonly Color BtnBorder = new("9fd0f0");

    private readonly Control _root;          // positioned at (1210, 90); pivot for the scale animation
    private readonly PanelContainer _panel;
    private readonly Control _artHolder;
    private readonly Label _title;
    private readonly Label _meta;
    private readonly RichTextLabel _desc;
    private readonly Control _buyRow;        // holds the buy button or the "Sold" label
    private Tween? _tween;
    private Haggle.Item? _item;
    private object? _shownModel;             // the entry's model when the content was built (The Courier restocks)
    private bool _open;
    private bool _escDown, _mouseDown;
    private ulong _shownFrame;
    private SceneTree? _tree;

    /// <summary>The popup of the open shop, for <see cref="DetailPopupInputManagerPatch"/> (Escape / back).</summary>
    internal static DetailPopup? Current { get; private set; }

    /// <param name="parent">ShopStage.Popups.</param>
    public DetailPopup(Control parent)
    {
        _root = new Control { Name = "Detail", Position = new Vector2(1210, 90), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(Width, 0) };
        _panel.AddThemeStyleboxOverride("panel", Theme.Box(Theme.PanelBg, Theme.Brass, 2, 18, (int)Pad));
        _root.AddChild(_panel);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", (int)Gap);
        _panel.AddChild(row);

        _artHolder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        row.AddChild(_artHolder);

        var info = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", 0);
        row.AddChild(info);

        _title = Theme.MakeLabel("", 40, Theme.Ink, bold: true, outline: 0);
        Shadow(_title);
        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _title.CustomMinimumSize = new Vector2(0, 0);
        info.AddChild(_title);
        info.AddChild(Spacer(6));

        _meta = Theme.MakeLabel("", 24, Meta, outline: 0);
        Shadow(_meta);
        _meta.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        info.AddChild(_meta);
        info.AddChild(Spacer(10));

        _desc = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        StyleRich(_desc, 24, Theme.Ink);
        _desc.AddThemeConstantOverride("line_separation", 6); // line-height 1.25 at 24px
        info.AddChild(_desc);

        _buyRow = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        info.AddChild(_buyRow);

        // Round × in the top-right corner (#detail .close).
        var close = new Button { Text = "×", FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = "Close" };
        Theme.StyleButton(close, ButtonBox(24, 0, 0), 34, Theme.Ink);
        close.CustomMinimumSize = new Vector2(48, 48);
        close.Size = new Vector2(48, 48);
        close.Position = new Vector2(Width - 10 - 48, 8);
        close.Pressed += () => Close();
        _root.AddChild(close);

        _panel.Resized += () =>
        {
            _root.Size = _panel.Size;
            _root.PivotOffset = _panel.Size / 2;
        };

        parent.AddChild(_root);
        if (_root.IsInsideTree()) Hook();
        else _root.TreeEntered += Hook;
        _root.TreeExiting += Unhook;
        Current = this;
    }

    public bool IsOpen => _open;
    public Haggle.Item? Item => _open ? _item : null;

    /// <summary>Show (or switch to) an item. gold decides whether Buy is enabled.</summary>
    public void Show(Haggle.Item item, int gold)
    {
        if (!GodotObject.IsInstanceValid(_root)) return;
        _item = item;
        _shownFrame = Engine.GetProcessFrames();
        Fill(item, gold);
        if (!_open) Animate(true);
        _open = true;
    }

    private void Fill(Haggle.Item item, int gold)
    {
        _shownModel = ModelOf(item.Entry);
        BuildArt(item);
        _title.Text = $"#{item.Shelf} {Title(item)}";
        _meta.Text = MetaLine(item);
        _desc.Text = DescriptionBbcode(item);
        BuildBuyRow(item, gold);
        _panel.Size = Vector2.Zero; // shrink to the new content
    }

    private static object? ModelOf(MerchantEntry e)
    {
        try
        {
            return e switch
            {
                MerchantCardEntry c => c.CreationResult?.Card,
                MerchantRelicEntry r => r.Model,
                MerchantPotionEntry p => p.Model,
                _ => null,
            };
        }
        catch { return null; }
    }

    /// <summary>Re-read price, gold and sold state of the item on show (after a deal or a purchase).</summary>
    public void Refresh(int gold)
    {
        if (!_open || _item == null || !GodotObject.IsInstanceValid(_root)) return;
        // The Courier restocks a bought slot with a new item: show that item, not the old art/name with the new price.
        if (_item.Entry.IsStocked && !ReferenceEquals(ModelOf(_item.Entry), _shownModel))
        {
            Fill(_item, gold);
            return;
        }
        BuildBuyRow(_item, gold);
        _panel.Size = Vector2.Zero;
    }

    public void Hide()
    {
        if (!_open) return;
        _open = false;
        Animate(false);
    }

    private void Close()
    {
        if (!_open) return;
        Hide();
        Closed?.Invoke();
    }

    // ------------------------------------------------------------------ input

    private void Hook()
    {
        if (_tree != null) return;
        _tree = _root.GetTree();
        _tree.ProcessFrame += OnFrame;
    }

    private void Unhook()
    {
        if (_tree != null) _tree.ProcessFrame -= OnFrame;
        _tree = null;
        if (Current == this) Current = null;
    }

    /// <summary>Open and on screen (the shop stage is hidden while the map, deck view, pause menu etc. are up).</summary>
    private bool Interactive => _open && GodotObject.IsInstanceValid(_root) && _root.IsVisibleInTree();

    /// <summary>
    /// Escape / back pressed: close the popup if it is open and visible, and say whether the key was used (the
    /// Harmony patch then keeps it from the game, which would otherwise also open the pause menu).
    /// </summary>
    internal bool TryCancel()
    {
        if (!Interactive) return false;
        Close();
        return true;
    }

    private void OnFrame()
    {
        if (!GodotObject.IsInstanceValid(_root)) { Unhook(); return; }
        var esc = Input.IsKeyPressed(Key.Escape);
        var mouse = Input.IsMouseButtonPressed(MouseButton.Left);
        var escEdge = esc && !_escDown;
        var mouseEdge = mouse && !_mouseDown;
        _escDown = esc;
        _mouseDown = mouse;
        if (!Interactive) return;
        // Normally Escape is handled (and swallowed) by DetailPopupInputManagerPatch; this is only the fallback.
        if (escEdge && !DetailPopupInputManagerPatch.Active)
        {
            Close();
            return;
        }
        // A click on empty space closes, like the web's stage click; skip the frame the popup was (re)shown in.
        if (mouseEdge && Engine.GetProcessFrames() > _shownFrame + 1)
        {
            var vp = _root.GetViewport();
            if (vp != null && !KeepsOpen(vp.GuiGetHoveredControl())) Close();
        }
    }

    /// <summary>
    /// "Empty space" is anything but our own interactive controls (rug items, HUD, this popup) and the game's
    /// buttons (top bar etc.). The room underneath has mouse-catching controls of its own (the room root is a
    /// Control, background art), so "no control under the mouse" alone would almost never be true.
    /// </summary>
    private bool KeepsOpen(Control? hovered)
    {
        if (hovered == null) return false;
        Node? canvas = _root;
        // The stage root ("Stage") may live in our CanvasLayer or be embedded on the game's canvas.
        while (canvas != null && canvas is not CanvasLayer && canvas.Name != "Stage") canvas = canvas.GetParent();
        for (Node? n = hovered; n != null; n = n.GetParent())
        {
            if (canvas != null && n == canvas) return true;
            if (n is BaseButton or NClickableControl or LineEdit or TextEdit) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ animation

    private void Animate(bool open)
    {
        _tween?.Kill();
        _root.PivotOffset = _panel.Size / 2;
        _tween = _root.CreateTween().SetParallel();
        if (open)
        {
            _root.Visible = true;
            _root.Modulate = _root.Modulate with { A = 0 };
            _root.Scale = new Vector2(0.92f, 0.92f);
            _tween.TweenProperty(_root, "modulate:a", 1f, AnimTime);
            _tween.TweenProperty(_root, "scale", Vector2.One, AnimTime).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
        else
        {
            _tween.TweenProperty(_root, "modulate:a", 0f, AnimTime);
            _tween.TweenProperty(_root, "scale", new Vector2(0.92f, 0.92f), AnimTime);
            _tween.Chain().TweenCallback(Callable.From(() =>
            {
                if (!_open) ClearArt();
                if (!_open) _root.Visible = false;
            }));
        }
    }

    // ------------------------------------------------------------------ content

    private void ClearArt()
    {
        foreach (var c in _artHolder.GetChildren()) c.QueueFree();
    }

    private void BuildArt(Haggle.Item item)
    {
        ClearArt();
        Vector2 size;
        try
        {
            size = item.Entry switch
            {
                MerchantCardEntry c when c.CreationResult != null => CardArt(c.CreationResult.Card),
                MerchantRelicEntry r when r.Model != null => RelicArt(r.Model),
                MerchantPotionEntry p when p.Model != null => IconArt(p.Model.Image, 130),
                MerchantCardRemovalEntry => IconArt(Theme.Tex("res://images/rooms/merchant_room/card_removal_00.png"), 120),
                _ => Vector2.Zero,
            };
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] Detail art failed: {e.Message}");
            ClearArt();
            size = Vector2.Zero;
        }
        _artHolder.CustomMinimumSize = size;
        _artHolder.Visible = size != Vector2.Zero;
    }

    private Vector2 CardArt(CardModel card)
    {
        const float scale = 0.6f; // 300 px card -> 180 px
        var holderSize = NCard.defaultSize * scale;
        NCard? node = null;
        try { node = NCard.Create(card); }
        catch (Exception e) { Log.Warn($"[Doki] NCard.Create failed: {e.Message}"); }
        if (node == null)
        {
            var tex = SafeTex(() => Theme.Tex(card.PortraitPath));
            return IconArt(tex, 180);
        }
        _artHolder.AddChild(node);
        node.Scale = new Vector2(scale, scale);
        node.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
        IgnoreMouse(node);
        CenterCard(node, holderSize / 2, scale);
        // Layout inside the card scene may only settle after a frame; re-centre then.
        Callable.From(() => { if (GodotObject.IsInstanceValid(node)) CenterCard(node, holderSize / 2, scale); }).CallDeferred();
        return holderSize;
    }

    /// <summary>Put the card's body centre on <paramref name="center"/> whatever the scene's origin is.</summary>
    private static void CenterCard(NCard node, Vector2 center, float scale)
    {
        var local = Vector2.Zero; // NCard is normally centred on its origin
        try
        {
            var body = node.Body;
            if (body != null && GodotObject.IsInstanceValid(body) && body.Size != Vector2.Zero)
                local = node.GetGlobalTransform().AffineInverse() * (body.GetGlobalTransform() * (body.Size / 2));
        }
        catch { }
        node.Position = center - local * scale;
    }

    private Vector2 RelicArt(RelicModel relic)
    {
        // Let the game's NRelic pick the texture (and any special material), then show it in a plain TextureRect
        // so the layout doesn't depend on the relic scene.
        Texture2D? tex = null;
        Material? mat = null;
        try
        {
            var node = NRelic.Create(relic, NRelic.IconSize.Large);
            if (node != null)
            {
                _artHolder.AddChild(node); // runs _Ready, which sets Icon
                tex = node.Icon?.Texture;
                mat = node.Icon?.Material;
                _artHolder.RemoveChild(node);
                node.QueueFree();
            }
        }
        catch (Exception e) { Log.Warn($"[Doki] NRelic.Create failed: {e.Message}"); }
        tex ??= SafeTex(() => relic.BigIcon) ?? SafeTex(() => relic.Icon);
        var size = IconArt(tex, 130);
        if (mat != null && _artHolder.GetChildCount() > 0 && _artHolder.GetChild(_artHolder.GetChildCount() - 1) is TextureRect tr) tr.Material = mat;
        return size;
    }

    private Vector2 IconArt(Texture2D? tex, float width)
    {
        if (tex == null) return Vector2.Zero;
        var ts = tex.GetSize();
        var h = ts.X > 0 ? width * ts.Y / ts.X : width;
        var size = new Vector2(width, h);
        _artHolder.AddChild(new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        return size;
    }

    private void BuildBuyRow(Haggle.Item item, int gold)
    {
        foreach (var c in _buyRow.GetChildren()) c.QueueFree();
        _buyRow.AddChild(Spacer(18));
        if (!item.Entry.IsStocked)
        {
            var sold = Theme.MakeLabel("Sold", 30, Theme.Red, bold: true, outline: 0);
            Shadow(sold);
            _buyRow.AddChild(sold);
            return;
        }
        var cost = item.Entry.Cost;
        var deal = item.Agreed != null;
        var taxed = Haggle.AngerTax.Contains(item.Entry);
        var price = deal ? $"[color=#{Theme.Green.ToHtml(false)}]{cost}[/color]"
            : taxed ? $"[color=#{Theme.Red.ToHtml(false)}]{cost}[/color]"
            : cost.ToString();
        if ((deal || taxed) && item.ListPrice > 0 && item.ListPrice != cost)
            price = $"[s][color=#aaaaaa]{item.ListPrice}[/color][/s] {price}";
        var label = item.Kind == "service" || item.Entry is MerchantCardRemovalEntry ? "Remove a card for" : "Buy for";

        var btn = new Button { FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Stop, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        Theme.StyleButton(btn, ButtonBox(14, 26, 12), 30, Theme.Ink);
        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = $"{label} {price} gold",
        };
        StyleRich(text, 30, Theme.Ink, bold: true);
        btn.AddChild(text);
        void Fit()
        {
            if (!GodotObject.IsInstanceValid(btn) || !GodotObject.IsInstanceValid(text)) return;
            var ms = text.GetCombinedMinimumSize();
            if (ms.X < 1) ms = new Vector2(text.GetContentWidth(), text.GetContentHeight());
            btn.CustomMinimumSize = ms + new Vector2(26 * 2 + 6, 12 * 2 + 6);
            text.Size = ms;
            text.Position = new Vector2(29, 15);
        }
        text.MinimumSizeChanged += Fit;
        Fit();
        Callable.From(Fit).CallDeferred();

        var enabled = cost <= gold;
        btn.Disabled = !enabled;
        btn.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.5f);
        btn.MouseDefaultCursorShape = enabled ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;
        btn.Pressed += () => { if (_open && _item == item) BuyPressed?.Invoke(item); };
        _buyRow.AddChild(btn);
    }

    // ------------------------------------------------------------------ text

    private static string Title(Haggle.Item item) =>
        item.Entry is MerchantCardRemovalEntry ? "Card Removal" : ItemText.Name(item);

    private static string MetaLine(Haggle.Item item)
    {
        try
        {
            switch (item.Entry)
            {
                case MerchantCardEntry c when c.CreationResult != null:
                {
                    var card = c.CreationResult.Card;
                    return $"{(item.Secret ? "♥ Secret · " : "")}{CardOwner(card)} · {Words(card.Rarity.ToString())} {Words(card.Type.ToString())}";
                }
                case MerchantRelicEntry r when r.Model != null: return $"{Words(r.Model.Rarity.ToString())} relic";
                case MerchantPotionEntry p when p.Model != null: return $"{Words(p.Model.Rarity.ToString())} potion";
                case MerchantCardRemovalEntry: return "Service";
            }
        }
        catch { }
        return item.Kind == "service" ? "Service" : item.Kind;
    }

    private static string CardOwner(CardModel card)
    {
        try { if (card.Pool.IsColorless) return "Colorless"; } catch { }
        try
        {
            var t = card.Owner?.Character?.Title.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(t)) return ItemText.Clean(t);
        }
        catch { }
        try
        {
            var p = card.Pool.Title;
            if (!string.IsNullOrEmpty(p)) return char.ToUpperInvariant(p[0]) + p[1..];
        }
        catch { }
        return "Card";
    }

    /// <summary>"RareCard" -> "Rare Card".</summary>
    private static string Words(string s) => Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");

    private static string DescriptionBbcode(Haggle.Item item)
    {
        try
        {
            var raw = item.Entry switch
            {
                MerchantCardEntry c when c.CreationResult != null => c.CreationResult.Card.GetDescriptionForPile(PileType.None),
                MerchantRelicEntry r when r.Model != null => r.Model.DynamicDescription.GetFormattedText(),
                MerchantPotionEntry p when p.Model != null => p.Model.DynamicDescription.GetFormattedText(),
                MerchantCardRemovalEntry => "Remove one card of your choice from your deck.",
                _ => "",
            };
            return ToBbcode(raw);
        }
        catch
        {
            return Escape(ItemText.Describe(item));
        }
    }

    private static readonly Dictionary<string, Color> TagColors = new()
    {
        ["gold"] = Theme.Gold, ["blue"] = Theme.Blue, ["green"] = Theme.Green, ["red"] = Theme.Red,
        ["purple"] = Theme.Purple, ["pink"] = Theme.Pink, ["aqua"] = new("7fffd4"), ["orange"] = new("ffa64d"),
    };
    private static readonly HashSet<string> KeepTags = new() { "b", "i", "u", "s", "center", "font_size" };

    /// <summary>
    /// The game's description markup to RichTextLabel BBCode: colour effects become [color], inline icons stay as
    /// sized [img] when the texture exists (otherwise a word), and unknown effect tags ([sine], [jitter]...) go.
    /// </summary>
    internal static string ToBbcode(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Inline icons first.
        s = Regex.Replace(s, @"\[img[^\]]*\](.*?)\[/img\]", m =>
        {
            var path = m.Groups[1].Value.Trim();
            var word = path.Contains("energy") ? " energy" : path.Contains("star") ? " star" : path.Contains("gold") ? " gold" : "";
            try
            {
                if (path.StartsWith("res://") && ResourceLoader.Exists(path)) return $"\u0001img=28x28\u0002{path}\u0001/img\u0002";
            }
            catch { }
            return word;
        }, RegexOptions.Singleline);
        s = Regex.Replace(s, @"\[(/?)([a-zA-Z_]+)([^\]]*)\]", m =>
        {
            var closing = m.Groups[1].Value == "/";
            var tag = m.Groups[2].Value.ToLowerInvariant();
            if (TagColors.TryGetValue(tag, out var col))
                return closing ? "\u0001/color\u0002" : $"\u0001color=#{col.ToHtml(false)}\u0002";
            if (KeepTags.Contains(tag)) return $"\u0001{m.Groups[1].Value}{tag}{m.Groups[3].Value}\u0002";
            return "";
        });
        // Any bracket left is literal text; then restore our tags.
        s = s.Replace("[", "[lb]").Replace("]", "[rb]").Replace("[lb[rb]", "[lb]");
        s = s.Replace("\u0001", "[").Replace("\u0002", "]");
        return s.Trim();
    }

    private static string Escape(string s) => s.Replace("[", "\u0001").Replace("]", "[rb]").Replace("\u0001", "[lb]");

    // ------------------------------------------------------------------ styling helpers

    private static StyleBoxFlat ButtonBox(int radius, int padX, int padY)
    {
        var b = Theme.Box(BtnBg, BtnBorder, 3, radius);
        b.ContentMarginLeft = b.ContentMarginRight = padX;
        b.ContentMarginTop = b.ContentMarginBottom = padY;
        b.ShadowColor = new Color(0, 0, 0, 0.35f);
        b.ShadowSize = 4;
        b.ShadowOffset = new Vector2(0, 2);
        return b;
    }

    private static void StyleRich(RichTextLabel r, int size, Color color, bool bold = false)
    {
        var regular = bold ? Theme.Bold ?? Theme.Font : Theme.Font;
        if (regular != null) r.AddThemeFontOverride("normal_font", regular);
        if ((Theme.Bold ?? Theme.Font) is { } b) r.AddThemeFontOverride("bold_font", b);
        foreach (var k in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size", "mono_font_size" })
            r.AddThemeFontSizeOverride(k, size);
        r.AddThemeColorOverride("default_color", color);
        r.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        r.AddThemeConstantOverride("shadow_offset_x", 0);
        r.AddThemeConstantOverride("shadow_offset_y", 2);
        r.AddThemeConstantOverride("shadow_outline_size", 2);
    }

    private static void Shadow(Label l)
    {
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        l.AddThemeConstantOverride("shadow_offset_x", 0);
        l.AddThemeConstantOverride("shadow_offset_y", 2);
        l.AddThemeConstantOverride("shadow_outline_size", 2);
    }

    private static Control Spacer(float h) => new() { CustomMinimumSize = new Vector2(0, h), MouseFilter = Control.MouseFilterEnum.Ignore };

    private static void IgnoreMouse(Node n)
    {
        if (n is Control c) c.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var child in n.GetChildren()) IgnoreMouse(child);
    }

    private static Texture2D? SafeTex(Func<Texture2D?> f)
    {
        try { return f(); }
        catch { return null; }
    }
}
