using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace DokiDokiMerchant;

/// <summary>
/// Steam-style achievement toasts at the bottom left (style.css #toasts/.toast), and the achievements themselves
/// (the web game's meta.js list), remembered across runs in achievements.dat next to the mod.
/// </summary>
internal sealed class Toasts
{
    /// <summary>meta.js ACH: key → (name, description).</summary>
    public static readonly IReadOnlyDictionary<string, (string Name, string Description)> All =
        new Dictionary<string, (string, string)>
        {
            ["crush"] = ("Hooded Crush", "Complimented a man whose face you've never seen."),
            ["fool"] = ("Paid Full Price Like a Fool", "He's laughing all the way to the bank."),
            ["friendzone"] = ("Friend-Zoned by a Hooded Man", "You were a Regular. You blew it."),
            ["transaction"] = ("Love Is a Transaction", "Reached Darling. Your wallet weeps."),
            ["cheapskate"] = ("Cheapskate", "He refuses to haggle with you anymore."),
            ["simp"] = ("Simp", "Bought 3 things at list price. For him."),
            ["secret"] = ("Under the Counter", "He showed you the good stuff."),
        };

    private const float Width = 470, Left = 24, Bottom = 1080 - 110, Gap = 10;
    private const float InTime = 0.4f, Hold = 4.2f, OutTime = 0.6f;
    private static readonly Color BgFrom = new("1d2633"), BgTo = new("2a3646");
    private static readonly Color HeaderColor = new("9fb4c8"), DescColor = new("ccccdd");

    private static HashSet<string>? _unlocked;
    private static string FilePath => Path.Combine(Config.ModDir, "achievements.dat");

    private readonly VBoxContainer _stack;

    /// <param name="parent">ShopStage.Toasts.</param>
    public Toasts(Control parent)
    {
        // Column whose bottom edge stays at y = 970 while it grows upwards; newest toast is the last child.
        _stack = new VBoxContainer
        {
            Name = "Toasts",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            GrowVertical = Control.GrowDirection.Begin,
        };
        _stack.AddThemeConstantOverride("separation", (int)Gap);
        _stack.AnchorLeft = _stack.AnchorRight = _stack.AnchorTop = _stack.AnchorBottom = 0;
        _stack.OffsetLeft = Left;
        _stack.OffsetRight = Left + Width;
        _stack.OffsetTop = Bottom;
        _stack.OffsetBottom = Bottom;
        parent.AddChild(_stack);
    }

    /// <summary>Whether an achievement has been unlocked in any run.</summary>
    public static bool IsUnlocked(string key) => Unlocked.Contains(key);

    /// <summary>Unlock an achievement by its web key ("secret", "crush", "friendzone", "transaction", "fool",
    /// "simp", "cheapskate", …) and toast it. Already unlocked (in any run) = nothing happens.</summary>
    public void Unlock(string key)
    {
        if (!All.TryGetValue(key, out var a) || !Unlocked.Add(key)) return;
        Save();
        Show("Achievement unlocked", a.Name, a.Description);
    }

    /// <summary>A toast with any text (trophy icon, small caps header, gold name, small description).</summary>
    public void Show(string header, string name, string description)
    {
        if (!GodotObject.IsInstanceValid(_stack)) return;

        // The slot is what the VBox lays out; the card inside it slides freely.
        var slot = new Control { Name = "Toast", MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        var card = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(-60, 0) };
        slot.AddChild(card);

        // Gold rounded rect with the shadow; the 6px left strip that stays visible is the border-left.
        var back = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var backStyle = Theme.Box(Theme.Gold, radius: 10);
        backStyle.ShadowColor = new Color(0, 0, 0, 0.67f);
        backStyle.ShadowSize = 20;
        backStyle.ShadowOffset = new Vector2(0, 8);
        back.AddThemeStyleboxOverride("panel", backStyle);
        card.AddChild(back);

        // Horizontal gradient clipped to a rounded shape inset by the border width.
        var mask = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, ClipChildren = CanvasItem.ClipChildrenMode.Only };
        mask.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        mask.OffsetLeft = 6;
        var maskStyle = new StyleBoxFlat { BgColor = Colors.White, AntiAliasing = true };
        maskStyle.CornerRadiusTopLeft = maskStyle.CornerRadiusBottomLeft = 4;
        maskStyle.CornerRadiusTopRight = maskStyle.CornerRadiusBottomRight = 10;
        mask.AddThemeStyleboxOverride("panel", maskStyle);
        var grad = new TextureRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = new[] { BgFrom, BgTo }, Offsets = new[] { 0f, 1f } },
                FillFrom = new Vector2(0, 0),
                FillTo = new Vector2(1, 0),
                Width = 64,
                Height = 8,
            },
        };
        grad.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        mask.AddChild(grad);
        card.AddChild(mask);

        // Content: padding 12/18 (plus the 6px border), trophy, then the text column.
        var content = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(Width, 0) };
        content.AddThemeConstantOverride("margin_left", 6 + 18);
        content.AddThemeConstantOverride("margin_right", 18);
        content.AddThemeConstantOverride("margin_top", 12);
        content.AddThemeConstantOverride("margin_bottom", 12);
        card.AddChild(content);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Begin };
        row.AddThemeConstantOverride("separation", 14);
        content.AddChild(row);

        var trophy = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(44, 50),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        trophy.Draw += () => DrawTrophy(trophy);
        row.AddChild(trophy);

        var col = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        col.AddThemeConstantOverride("separation", 0);
        row.AddChild(col);

        var head = Theme.MakeLabel(header, 18, HeaderColor, bold: true, outline: 0);
        head.Uppercase = true;
        if ((Theme.Bold ?? Theme.Font) is { } hf)
            head.AddThemeFontOverride("font", new FontVariation { BaseFont = hf, SpacingGlyph = 1 });
        col.AddChild(head);

        var nameLabel = Theme.MakeLabel(name, 28, Theme.Gold, bold: true, outline: 0);
        nameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(nameLabel);

        var textWidth = Width - 24 - 18 - 44 - 14;
        var desc = Theme.MakeLabel(description, 18, DescColor, outline: 0);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.CustomMinimumSize = new Vector2(textWidth, 0);
        nameLabel.CustomMinimumSize = new Vector2(textWidth, 0);
        col.AddChild(desc);

        // Keep card and slot sized to the content (the wrapped description decides the height).
        void Fit()
        {
            if (!GodotObject.IsInstanceValid(content)) return;
            var size = new Vector2(Width, content.GetCombinedMinimumSize().Y);
            content.Size = size;
            card.Size = size;
            slot.CustomMinimumSize = size;
        }
        content.MinimumSizeChanged += Fit;
        _stack.AddChild(slot);
        Fit();
        Callable.From(Fit).CallDeferred();

        // toastin: slide from -60px and fade in, ease-out; then .out: -40px and fade over 0.6s; then free.
        var tween = slot.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(slot, "modulate:a", 1f, InTime).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(card, "position:x", 0f, InTime).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.Chain().TweenInterval(Hold - InTime);
        tween.Chain().TweenProperty(slot, "modulate:a", 0f, OutTime).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(card, "position:x", -40f, OutTime).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        tween.Chain().TweenCallback(Callable.From(slot.QueueFree));
    }

    /// <summary>A gold trophy cup (the web uses the 🏆 emoji, which Kreon lacks), drawn in a 44x50 box.</summary>
    private static void DrawTrophy(Control c)
    {
        var gold = Theme.Gold;
        var dark = new Color("a87a1c");
        var light = new Color("fff1b0");
        var outline = new Color("5a3d08");

        // Handles: thick arcs either side of the cup.
        c.DrawArc(new Vector2(8, 15), 7, Mathf.Pi * 0.5f, Mathf.Pi * 1.5f, 16, outline, 5.5f, true);
        c.DrawArc(new Vector2(36, 15), 7, -Mathf.Pi * 0.5f, Mathf.Pi * 0.5f, 16, outline, 5.5f, true);
        c.DrawArc(new Vector2(8, 15), 7, Mathf.Pi * 0.5f, Mathf.Pi * 1.5f, 16, gold, 3f, true);
        c.DrawArc(new Vector2(36, 15), 7, -Mathf.Pi * 0.5f, Mathf.Pi * 0.5f, 16, gold, 3f, true);

        // Cup bowl: flat rim, rounded bottom.
        var bowl = new List<Vector2> { new(7, 4), new(37, 4) };
        for (var i = 0; i <= 12; i++)
        {
            var a = Mathf.Pi * i / 12f; // 0..pi, right side down to left side
            bowl.Add(new Vector2(22 + Mathf.Cos(a) * 15, 16 + Mathf.Sin(a) * 13));
        }
        var bowlPts = bowl.ToArray();
        c.DrawColoredPolygon(bowlPts, gold);
        // Shading on the right half and a highlight on the left.
        var shade = new List<Vector2> { new(28, 4), new(37, 4) };
        for (var i = 0; i <= 6; i++)
        {
            var a = Mathf.Pi * 0.5f * i / 6f;
            shade.Add(new Vector2(22 + Mathf.Cos(a) * 15, 16 + Mathf.Sin(a) * 13));
        }
        shade.Add(new Vector2(26, 28));
        c.DrawColoredPolygon(shade.ToArray(), dark with { A = 0.55f });
        c.DrawColoredPolygon(new[] { new Vector2(11, 7), new Vector2(15, 7), new Vector2(15, 20), new Vector2(12, 17) }, light with { A = 0.8f });
        var closed = bowl.Append(bowl[0]).ToArray();
        c.DrawPolyline(closed, outline, 1.5f, true);

        // Stem, knot and base.
        var stem = new[] { new Vector2(19, 28), new Vector2(25, 28), new Vector2(24, 37), new Vector2(20, 37) };
        c.DrawColoredPolygon(stem, dark);
        c.DrawPolyline(stem.Append(stem[0]).ToArray(), outline, 1.2f, true);
        var plinth = new[] { new Vector2(14, 37), new Vector2(30, 37), new Vector2(32, 42), new Vector2(12, 42) };
        c.DrawColoredPolygon(plinth, gold);
        c.DrawPolyline(plinth.Append(plinth[0]).ToArray(), outline, 1.2f, true);
        var foot = new[] { new Vector2(9, 42), new Vector2(35, 42), new Vector2(35, 48), new Vector2(9, 48) };
        c.DrawColoredPolygon(foot, dark);
        c.DrawPolyline(foot.Append(foot[0]).ToArray(), outline, 1.2f, true);
        c.DrawLine(new Vector2(12, 44.5f), new Vector2(32, 44.5f), gold with { A = 0.7f }, 1.5f);
    }

    // ---------------------------------------------------------------- persistence

    private static HashSet<string> Unlocked => _unlocked ??= Load();

    private static HashSet<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new HashSet<string>();
            var keys = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath));
            return keys == null ? new HashSet<string>() : new HashSet<string>(keys);
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] Could not read achievements: {e.Message}");
            return new HashSet<string>();
        }
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Unlocked.OrderBy(k => k).ToList()));
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] Could not save achievements: {e.Message}");
        }
    }
}
