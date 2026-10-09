using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace DokiDokiMerchant;

/// <summary>
/// Shared look for the Doki Doki shop, matching the web game's style.css: colours, the game's own Kreon font,
/// label and panel helpers. Every UI piece is built from plain Godot nodes (no custom Node subclasses: the mod
/// is compiled without Godot's source generators), driven by tweens and C# events.
/// </summary>
internal static class Theme
{
    // style.css :root
    public static readonly Color Gold = new("efc851");
    public static readonly Color Blue = new("87ceeb");
    public static readonly Color Green = new("7fff00");
    public static readonly Color Red = new("ff6563");
    public static readonly Color Purple = new("ee82ee");
    public static readonly Color Ink = new("fff6e2");
    public static readonly Color Pink = new("ff7eb6");
    public static readonly Color PinkDeep = new("e0407f");
    public static readonly Color Lilac = new("c9a3ff");
    public static readonly Color Plum = new("7a1c4a");      // dark pink outlines and button shadows
    public static readonly Color Brass = new("c9a86a");     // the gold-brown borders on panels and badges
    public static readonly Color PanelBg = new(14 / 255f, 10 / 255f, 18 / 255f, 0.92f);

    /// <summary>The game's Kreon (regular and bold), found on the room's own labels or loaded by path.</summary>
    public static Font? Font { get; private set; }
    public static Font? Bold { get; private set; }

    public static void Init(Node room)
    {
        Font ??= Load<Font>("res://themes/kreon_regular_shared.tres") ?? Load<Font>("res://fonts/kreon_regular.ttf");
        Bold ??= Load<Font>("res://themes/kreon_bold_shared.tres") ?? Load<Font>("res://fonts/kreon_bold.ttf");
        if (Font == null || Bold == null)
        {
            var found = room.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.GetThemeFont("font")).FirstOrDefault(f => f != null);
            Font ??= found;
            Bold ??= found;
        }
    }

    /// <summary>Loads a game resource by its res:// path (null, and a log line, when it isn't there).</summary>
    public static T? Load<T>(string path) where T : Resource
    {
        try
        {
            if (!ResourceLoader.Exists(path)) return null;
            return ResourceLoader.Load<T>(path);
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] Could not load {path}: {e.Message}");
            return null;
        }
    }

    public static Texture2D? Tex(string path) => Load<Texture2D>(path);

    public static Label MakeLabel(string text, int size, Color color, bool bold = false, int outline = 6, Color? outlineColor = null)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        var f = bold ? Bold ?? Font : Font;
        if (f != null) l.AddThemeFontOverride("font", f);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        if (outline > 0)
        {
            l.AddThemeConstantOverride("outline_size", outline);
            l.AddThemeColorOverride("font_outline_color", outlineColor ?? new Color(0, 0, 0, 0.85f));
        }
        return l;
    }

    /// <summary>A rounded panel style. <paramref name="margin"/> is the content padding on every side.</summary>
    public static StyleBoxFlat Box(Color bg, Color? border = null, int borderWidth = 3, int radius = 18, int margin = 0)
    {
        var s = new StyleBoxFlat { BgColor = bg, AntiAliasing = true };
        s.SetCornerRadiusAll(radius);
        if (border is { } b)
        {
            s.BorderColor = b;
            s.SetBorderWidthAll(borderWidth);
        }
        s.SetContentMarginAll(margin);
        return s;
    }

    /// <summary>Applies the same style to a button's normal/hover/pressed/focus/disabled states, tinting hover.</summary>
    public static void StyleButton(Button b, StyleBoxFlat normal, int fontSize, Color fontColor, bool bold = true)
    {
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = normal.BgColor.Lightened(0.12f);
        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = normal.BgColor.Darkened(0.12f);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", pressed);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeStyleboxOverride("disabled", normal);
        var f = bold ? Bold ?? Font : Font;
        if (f != null) b.AddThemeFontOverride("font", f);
        b.AddThemeFontSizeOverride("font_size", fontSize);
        foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            b.AddThemeColorOverride(k, fontColor);
        b.AddThemeColorOverride("font_disabled_color", fontColor with { A = 0.5f });
        b.AddThemeConstantOverride("outline_size", 5);
        b.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
    }

    /// <summary>A vertical two-colour gradient texture (CSS linear-gradient(top, bottom)), for TextureRects.</summary>
    public static GradientTexture2D VGradient(Color top, Color bottom, int w = 64, int h = 64) => new()
    {
        Gradient = new Gradient { Colors = new[] { top, bottom }, Offsets = new[] { 0f, 1f } },
        FillFrom = new Vector2(0, 0),
        FillTo = new Vector2(0, 1),
        Width = w,
        Height = h,
    };

    /// <summary>Polygon points for a circle (or an ellipse) centred on <paramref name="c"/>.</summary>
    public static Vector2[] Circle(Vector2 c, float rx, float? ry = null, int segments = 48)
    {
        var pts = new Vector2[segments];
        for (var i = 0; i < segments; i++)
        {
            var a = Mathf.Tau * i / segments;
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * (ry ?? rx));
        }
        return pts;
    }

    /// <summary>A full-rect Control that lets the mouse through, for grouping.</summary>
    public static Control Layer(string name)
    {
        var c = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
        c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return c;
    }
}

/// <summary>
/// The 1920x1080 stage every piece is laid out on, in the web game's own pixel coordinates (style.css), scaled
/// to fit the window like the web #stage. Children are stacked bottom to top in this order.
/// </summary>
internal sealed class ShopStage
{
    public const float W = 1920, H = 1080;

    public readonly CanvasLayer Canvas;
    /// <summary>The scaled 1920x1080 root. Shake this for the "jolt".</summary>
    public readonly Control Root;
    public readonly Control Rug;     // rug and goods
    public readonly Control Hud;     // heart meter, dialogue box, mic, chat, top strip
    public readonly Control Popups;  // item detail
    public readonly Control Fx;      // stamps, heart pops, vignettes
    public readonly Control Toasts;  // achievements, on top of everything

    public ShopStage(Node room)
    {
        Canvas = new CanvasLayer { Layer = 60, Name = "DokiShop" };
        Root = new Control { Name = "Stage", Size = new Vector2(W, H), MouseFilter = Control.MouseFilterEnum.Ignore };
        Canvas.AddChild(Root);
        Rug = Add("Rug");
        Hud = Add("Hud");
        Popups = Add("Popups");
        Fx = Add("Fx");
        Toasts = Add("Toasts");
        room.AddChild(Canvas);
        Fit();
        Root.GetViewport().SizeChanged += Fit;
    }

    private Control Add(string name)
    {
        var c = new Control { Name = name, Size = new Vector2(W, H), MouseFilter = Control.MouseFilterEnum.Ignore };
        Root.AddChild(c);
        return c;
    }

    /// <summary>Scale the 1920x1080 stage to the visible rect, letterboxed and centred.</summary>
    public void Fit()
    {
        if (!GodotObject.IsInstanceValid(Root)) return;
        var vp = Root.GetViewport().GetVisibleRect().Size;
        var k = Mathf.Min(vp.X / W, vp.Y / H);
        Root.Scale = new Vector2(k, k);
        Root.Position = (vp - new Vector2(W, H) * k) / 2;
    }

    public void Free()
    {
        if (GodotObject.IsInstanceValid(Root)) Root.GetViewport().SizeChanged -= Fit;
        if (GodotObject.IsInstanceValid(Canvas)) Canvas.QueueFree();
    }
}
