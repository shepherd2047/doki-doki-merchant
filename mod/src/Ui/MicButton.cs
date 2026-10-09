using Godot;

namespace DokiDokiMerchant;

internal enum MicState { Idle, Listening, Thinking, Speaking, Disabled }

/// <summary>
/// The big pink round microphone perched on the dialogue box's corner (style.css #mic/#talk) with a status line
/// under it. Idle breathes, Listening has pulsing rings, Thinking bobs, Speaking shows sound bars on lilac.
/// Everything is drawn on one Control's Draw event and redrawn every frame; a transparent Button on top takes
/// the clicks.
/// </summary>
internal sealed class MicButton
{
    /// <summary>Clicked: Doki toggles recording (click to start, click again to send).</summary>
    public event Action? Pressed;

    private const float Size = 112f, R = Size / 2, Border = 4f, BoxW = 124f;
    private const float StageRight = ShopStage.W;

    private static readonly Color PinkLight = new("ffb3d6");
    private static readonly Color ListenLight = new("ff9ec7"), ListenDeep = new("ff3d8b");
    private static readonly Color LilacLight = new("d8b8ff"), LilacDeep = new("8a4fd8"), LilacShadow = new("3a1468");
    private static readonly Color RedLight = new("ff9a9a"), RedDeep = new("c0392b");
    private static readonly Color Halo = new(1f, 126 / 255f, 182 / 255f, 0.25f);
    private static readonly Color IconShadow = new(90 / 255f, 10 / 255f, 50 / 255f, 0.6f);
    private static readonly Color StatusColor = new("ffe3f0");
    private static readonly float[] BarDelays = { 0f, -0.2f, -0.45f, -0.1f, -0.3f };

    private readonly Control _box, _face;
    private readonly Button _hit;
    private readonly Label _status;
    private readonly Gradient _gradient;
    private readonly GradientTexture2D _gradTex;
    private readonly StyleBoxFlat _bar;
    private readonly Vector2[] _disc, _discUv;

    private MicState _state = MicState.Idle;
    private bool _hovered, _held;
    private SceneTree? _tree;
    private ulong _lastTicks;
    private float _time, _stateTime;
    // smoothed (CSS transition) values
    private float _hover, _press;               // 0.12s
    private Color _light, _deep, _shadow;       // 0.3s
    private Color _gradLight, _gradDeep;         // what the gradient texture currently holds

    /// <param name="parent">ShopStage.Hud.</param>
    public MicButton(Control parent)
    {
        _box = new Control
        {
            Name = "Mic",
            Position = new Vector2(1790, 700),
            Size = new Vector2(BoxW, Size + 6 + 28),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        _face = new Control { Name = "Face", Size = new Vector2(BoxW, Size), MouseFilter = Control.MouseFilterEnum.Ignore };
        _box.AddChild(_face);

        _hit = new Button
        {
            Name = "Talk",
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            Position = new Vector2((BoxW - Size) / 2, 0),
            Size = new Vector2(Size, Size),
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = "",
        };
        foreach (var k in new[] { "normal", "hover", "pressed", "focus", "disabled", "hover_pressed" })
            _hit.AddThemeStyleboxOverride(k, new StyleBoxEmpty());
        _hit.MouseEntered += () => _hovered = true;
        _hit.MouseExited += () => { _hovered = false; _held = false; };
        _hit.ButtonDown += () => _held = true;
        _hit.ButtonUp += () => _held = false;
        _hit.Pressed += () => Pressed?.Invoke();
        _box.AddChild(_hit);

        _status = Theme.MakeLabel("Tap to talk", 20, StatusColor, bold: true, outline: 0);
        _status.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _status.AddThemeConstantOverride("shadow_offset_x", 0);
        _status.AddThemeConstantOverride("shadow_offset_y", 2);
        _status.AddThemeConstantOverride("shadow_outline_size", 3);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.AutowrapMode = TextServer.AutowrapMode.Off;
        _status.ClipText = false;
        _box.AddChild(_status);
        LayoutStatus();

        // radial-gradient(circle at 35% 30%, light, deep 70%) over the 104px padding box: the circle's size is
        // the distance to the farthest corner, so the deep colour is reached at 70% of that.
        _light = _gradLight = PinkLight;
        _deep = _gradDeep = Theme.PinkDeep;
        _shadow = Theme.Plum;
        _gradient = new Gradient { Offsets = new[] { 0f, 0.7f }, Colors = new[] { _light, _deep } };
        var far = new Vector2(0.65f, 0.7f).Length();
        _gradTex = new GradientTexture2D
        {
            Gradient = _gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.35f, 0.3f),
            FillTo = new Vector2(0.35f + far, 0.3f),
            Width = 128,
            Height = 128,
        };

        _disc = Theme.Circle(Vector2.Zero, R - Border, null, 64);
        _discUv = _disc.Select(p => new Vector2(0.5f + p.X / (2 * (R - Border)), 0.5f + p.Y / (2 * (R - Border)))).ToArray();

        _bar = new StyleBoxFlat { BgColor = Colors.White, AntiAliasing = true };
        _bar.SetCornerRadiusAll(4);

        _face.Draw += DrawFace;
        _box.TreeEntered += Attach;
        _box.TreeExiting += Detach;
        parent.AddChild(_box);
        if (_box.IsInsideTree() && _tree == null) Attach();
    }

    public void SetState(MicState state)
    {
        if (state == _state) return;
        _state = state;
        _stateTime = 0;
        var off = state == MicState.Disabled;
        _hit.Disabled = off;
        _hit.MouseDefaultCursorShape = off ? Control.CursorShape.Arrow : Control.CursorShape.PointingHand;
        if (off) _held = false;
        _face.Modulate = off ? new Color(1, 1, 1, 0.5f) : Colors.White;
        _face.QueueRedraw();
    }

    /// <summary>The line under the button, e.g. "Tap or hold V to talk", "Listening… tap to send".</summary>
    public void SetStatus(string text)
    {
        if (text == _status.Text) return; // Doki calls this every frame
        _status.Text = text;
        LayoutStatus();
    }

    // ------------------------------------------------------------------ layout and frame loop

    /// <summary>Centre the status under the button, letting it run past 124px but never off the stage's right edge.</summary>
    private void LayoutStatus()
    {
        var font = Theme.Bold ?? Theme.Font ?? _status.GetThemeFont("font");
        var w = font != null ? font.GetStringSize(_status.Text, HorizontalAlignment.Left, -1, 20).X + 8 : _status.GetMinimumSize().X;
        w = Mathf.Max(w, BoxW);
        var x = (BoxW - w) / 2;
        var maxX = StageRight - 6 - _box.Position.X - w;   // keep a small margin from the stage's edge
        x = Mathf.Min(x, maxX);
        _status.Position = new Vector2(x, Size + 6);
        _status.Size = new Vector2(w, 28);
    }

    private void Attach()
    {
        if (_tree != null || !_box.IsInsideTree()) return;
        _tree = _box.GetTree();
        _lastTicks = Time.GetTicksMsec();
        _tree.ProcessFrame += Tick;
    }

    private void Detach()
    {
        if (_tree != null) _tree.ProcessFrame -= Tick;
        _tree = null;
    }

    private bool Live => _state is MicState.Listening or MicState.Thinking or MicState.Speaking;
    private bool HangUp => Live && _hovered;

    private void Tick()
    {
        if (!GodotObject.IsInstanceValid(_face)) { Detach(); return; }
        var now = Time.GetTicksMsec();
        var dt = Mathf.Clamp((now - _lastTicks) / 1000f, 0f, 0.1f);
        _lastTicks = now;
        _time += dt;
        _stateTime += dt;

        var hoverTarget = _hovered && !_held && _state != MicState.Disabled ? 1f : 0f;
        var pressTarget = _held && _state != MicState.Disabled ? 1f : 0f;
        _hover = Mathf.MoveToward(_hover, hoverTarget, dt / 0.12f);
        _press = Mathf.MoveToward(_press, pressTarget, dt / 0.12f);

        Color light, deep, shadow = Theme.Plum;
        if (HangUp) { light = RedLight; deep = RedDeep; if (_state != MicState.Listening) shadow = LilacShadow; }
        else if (_state == MicState.Listening) { light = ListenLight; deep = ListenDeep; }
        else if (_state is MicState.Thinking or MicState.Speaking) { light = LilacLight; deep = LilacDeep; shadow = LilacShadow; }
        else { light = PinkLight; deep = Theme.PinkDeep; }
        if (_state == MicState.Disabled) { light = Desat(light); deep = Desat(deep); shadow = Desat(shadow); }
        var k = Mathf.Min(1f, dt / 0.3f * 3f);   // ease towards the target over ~0.3s
        _light = Approach(_light, light, k);
        _deep = Approach(_deep, deep, k);
        _shadow = Approach(_shadow, shadow, k);
        if (!Same(_light, _gradLight) || !Same(_deep, _gradDeep))
        {
            _gradLight = _light;
            _gradDeep = _deep;
            _gradient.SetColor(0, _light);
            _gradient.SetColor(1, _deep);
        }

        _face.QueueRedraw();
    }

    private static Color Approach(Color a, Color b, float k) =>
        Mathf.Abs(b.R - a.R) + Mathf.Abs(b.G - a.G) + Mathf.Abs(b.B - a.B) + Mathf.Abs(b.A - a.A) < 0.004f ? b : a.Lerp(b, k);
    private static bool Same(Color a, Color b) => Mathf.IsEqualApprox(a.R, b.R) && Mathf.IsEqualApprox(a.G, b.G) && Mathf.IsEqualApprox(a.B, b.B);

    private static Color Desat(Color c)
    {
        var l = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
        return c.Lerp(new Color(l, l, l, c.A), 0.85f);
    }

    /// <summary>CSS ease-in-out style 0→1→0 over one period.</summary>
    private static float Pulse(float t, float period) => 0.5f - 0.5f * Mathf.Cos(Mathf.Tau * t / period);

    // ------------------------------------------------------------------ drawing

    private void DrawFace()
    {
        var off = _state == MicState.Disabled;
        var lift = -3f * _hover + 3f * _press;
        var scale = 1f + 0.04f * _hover;
        var centre = new Vector2(BoxW / 2, R + lift);
        _face.DrawSetTransform(centre, 0f, new Vector2(scale, scale));

        // soft drop shadow: 0 10px 24px rgba(0,0,0,.6), shrinking to 0 4px 12px when pressed
        var dropY = Mathf.Lerp(10f, 4f, _press);
        var blur = Mathf.Lerp(24f, 12f, _press);
        const int layers = 10;
        for (var i = 0; i < layers; i++)
        {
            var f = (float)i / layers;
            _face.DrawCircle(new Vector2(0, dropY), R - blur / 2 + blur * (1 - f), new Color(0, 0, 0, 0.6f * 1.4f / layers), true, -1, true);
        }

        // idle breathing halo: 0 0 0 10px rgba(255,126,182,.25) at the middle of each 2.4s breath
        if (_state == MicState.Idle)
        {
            var b = Pulse(_time, 2.4f);
            if (b > 0.01f) _face.DrawCircle(Vector2.Zero, R + 10f * b, Halo with { A = Halo.A * b }, true, -1, true);
        }

        // the "3D" plum base, then the white border, then the gradient face
        var baseY = Mathf.Lerp(6f, 2f, _press);
        _face.DrawCircle(new Vector2(0, baseY), R, _shadow, true, -1, true);
        _face.DrawCircle(Vector2.Zero, R, off ? Desat(Colors.White) : Colors.White, true, -1, true);
        _face.DrawPolygon(_disc, new[] { Colors.White }, _discUv, _gradTex);
        _face.DrawArc(Vector2.Zero, R - Border, 0, Mathf.Tau, 64, off ? Desat(Colors.White) : Colors.White, 1.5f, true);   // smooth the seam

        // listening: two pink rings expanding from the button, 1.6s, the second 0.8s behind
        if (_state == MicState.Listening)
        {
            foreach (var delay in new[] { 0f, 0.8f })
            {
                var t = _stateTime - delay;
                if (t < 0) continue;
                var u = t / 1.6f % 1f;
                var e = 1f - (1f - u) * (1f - u);   // ease-out
                var s = 1f + 0.7f * e;
                var a = 0.9f * (1f - e);
                _face.DrawArc(Vector2.Zero, (R - Border - 2f) * s, 0, Mathf.Tau, 64, Theme.Pink with { A = a }, 4f * s, true);
            }
        }

        var white = off ? Desat(Colors.White) : Colors.White;
        if (_state == MicState.Speaking && !HangUp)
            DrawBars(white);
        else
        {
            var bob = _state == MicState.Thinking && !HangUp ? 5f * Pulse(_stateTime, 0.9f) : 0f;
            var origin = new Vector2(-27f, -27f + bob);
            DrawMic(origin + new Vector2(0, 2), IconShadow, HangUp);
            DrawMic(origin, white, HangUp);
        }

        _face.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>The SVG mic (24-unit viewbox scaled to 54px), with the hang-up slash when <paramref name="slash"/>.</summary>
    private void DrawMic(Vector2 origin, Color c, bool slash)
    {
        const float k = 54f / 24f;
        Vector2 P(float x, float y) => origin + new Vector2(x, y) * k;
        var w = 2f * k;

        // capsule: rect x8.5 y2.5 w7 h12 rx3.5, filled and stroked (so 1 unit fatter all round)
        var cr = 4.5f * k;
        _face.DrawCircle(P(12, 6), cr, c, true, -1, true);
        _face.DrawCircle(P(12, 11), cr, c, true, -1, true);
        _face.DrawRect(new Rect2(P(7.5f, 6), new Vector2(9 * k, 5 * k)), c);

        // U arc: M5 11.5 a7 7 0 0 0 14 0 (the lower half circle around 12,11.5)
        _face.DrawArc(P(12, 11.5f), 7 * k, Mathf.Pi, 0, 32, c, w, true);
        Cap(P(5, 11.5f), w, c);
        Cap(P(19, 11.5f), w, c);
        Stroke(P(12, 18.5f), P(12, 21.5f), w, c);
        Stroke(P(8.5f, 21.5f), P(15.5f, 21.5f), w, c);
        if (slash) Stroke(P(3.5f, 3.5f), P(20.5f, 20.5f), w, c);
    }

    private void Stroke(Vector2 a, Vector2 b, float w, Color c)
    {
        _face.DrawLine(a, b, c, w, true);
        Cap(a, w, c);
        Cap(b, w, c);
    }

    private void Cap(Vector2 p, float w, Color c) => _face.DrawCircle(p, w / 2, c, true, -1, true);

    /// <summary>Five white sound bars (7px wide, gap 6, 50px tall at most) bouncing scaleY 0.25↔1 every 0.7s.</summary>
    private void DrawBars(Color c)
    {
        const float bw = 7f, gap = 6f, h = 50f;
        var total = 5 * bw + 4 * gap;
        _bar.BgColor = c;
        for (var i = 0; i < 5; i++)
        {
            var s = 0.25f + 0.75f * Pulse(_stateTime - BarDelays[i], 0.7f);
            var bh = h * s;
            var x = -total / 2 + i * (bw + gap);
            _face.DrawStyleBox(_bar, new Rect2(x, -bh / 2, bw, bh));
        }
    }
}
