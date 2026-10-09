using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace DokiDokiMerchant;

/// <summary>
/// Puts the original shop out of sight while keeping it alive (its nodes are still used by the game's purchase
/// code, the card-removal flow and the Merchant's hand), and the strip of info and the "Leave the shop" button
/// that replace it.
///
/// What is changed while active (all undone by <see cref="Dispose"/>):
/// - The native inventory rug never opens: NMerchantRoom.OpenInventory and NMerchantInventory.Open are skipped
///   (NativeShopPatches). That covers clicking the Merchant, the select hotkey and the merchant FTUE's hitbox.
///   The inventory node itself stays alive and closed (its slots sit off screen at y = -1000, as in vanilla).
/// - The Merchant's own button (NMerchantButton) keeps its mouse filter, so the Foul Potion can still be thrown
///   at him, but hovering no longer plays the hover sound or draws the outline skin, and clicking does nothing.
/// - Native speech bubbles are muted: NMerchantButton.PlayDialogue (foul potion, dead-player lines) and the rug's
///   NMerchantDialogue (purchase lines) are skipped and the dialogue node is hidden. Merchant VO sound effects
///   ("merchant_thank_yous", "merchant_welcome") are separate SfxCmd calls and still play. Suppressed bubble lines
///   are raised through <see cref="NativeLine"/> so they can go to our own dialogue box.
/// - The native Proceed button is hidden and its keyboard/controller hotkey (ui_confirm) unregistered, so a stray
///   Enter can't leave the shop. Our "Leave the shop" button calls the room's own handler (NMerchantRoom.HideScreen,
///   which the Proceed button's Released signal is connected to); the first-visit merchant FTUE popup that
///   HideScreen would show is skipped, so it opens the map exactly like Proceed does on later visits.
/// </summary>
internal sealed class NativeShop
{
    /// <summary>Active for the current shop visit; native shop behaviour is restored when this is null.</summary>
    public static NativeShop? Current { get; private set; }

    /// <summary>
    /// The CanvasLayer for ShopStage. The game draws everything (room, top bar, map, deck view, card-select overlays,
    /// pause/settings capstone screens, modals, hover tips, card-fly VFX, transitions) on the root canvas, layer 0;
    /// nothing in game.tscn / run.tscn is a CanvasLayer (only the dev console). So no layer number sits between the
    /// room and those screens: any layer above 0 covers all of them. 1 is the lowest that draws above the room;
    /// hide the stage while <see cref="ShouldHideShopUi"/> is true, or use <see cref="EmbedBelowGameUi"/> to put the
    /// stage on layer 0 between the room and the game's UI instead.
    /// </summary>
    public static int SuggestedLayer => 1;

    /// <summary>Height of the game's top bar in stage pixels (top_bar.tscn: LeftAlignedStuff/RightAlignedStuff are 80 tall).</summary>
    public const float TopBarHeight = 80f;

    /// <summary>A native Merchant speech line that was suppressed (foul potion, dead-player lines), already formatted.</summary>
    public event Action<string>? NativeLine;

    private static readonly AccessTools.FieldRef<NMerchantInventory, NMerchantDialogue> DialogueField =
        AccessTools.FieldRefAccess<NMerchantInventory, NMerchantDialogue>("_merchantDialogue");
    private static readonly System.Reflection.MethodInfo? HideScreenMethod = AccessTools.Method(typeof(NMerchantRoom), "HideScreen");
    private static readonly System.Reflection.MethodInfo? RegisterHotkeysMethod = AccessTools.Method(typeof(NButton), "RegisterHotkeys");
    private static readonly System.Reflection.MethodInfo? UnregisterHotkeysMethod = AccessTools.Method(typeof(NButton), "UnregisterHotkeys");

    private readonly NMerchantRoom _room;
    private readonly NProceedButton? _proceed;
    private readonly bool _proceedWasVisible;
    private readonly NMerchantDialogue? _dialogue;
    private readonly bool _dialogueWasVisible;
    private readonly CanvasItem? _merchantVisual;
    private readonly Color _visualModulate, _visualSelfModulate;
    private readonly RichTextLabel _info;
    private readonly Button _leave;
    private bool _disposed;

    /// <param name="room">The merchant room.</param>
    /// <param name="hud">ShopStage.Hud, for the info strip and Leave button.</param>
    public NativeShop(NMerchantRoom room, Control hud)
    {
        Current?.Dispose();
        _room = room;
        Current = this;

        // Proceed: hidden, hotkey off (re-unregistered whenever the game re-enables it, see NativeShopPatches).
        _proceed = room.ProceedButton;
        if (Valid(_proceed))
        {
            _proceedWasVisible = _proceed.Visible;
            _proceed.Visible = false;
            UnregisterHotkeys(_proceed);
        }

        // The rug's speech bubble node: hidden on top of the patches that stop it from showing.
        try
        {
            if (Valid(room.Inventory)) _dialogue = DialogueField(room.Inventory);
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] NativeShop: no merchant dialogue node: {e.Message}");
        }
        if (Valid(_dialogue))
        {
            _dialogueWasVisible = _dialogue.Visible;
            _dialogue.Visible = false;
        }

        // The Merchant's Spine sprite (merchant_button.tscn: MerchantButton/%MerchantVisual, a SpineSprite).
        var button = room.MerchantButton;
        if (Valid(button))
        {
            _merchantVisual = button.GetNodeOrNull<CanvasItem>("%MerchantVisual")
                ?? button.GetNodeOrNull<CanvasItem>("MerchantVisual")
                ?? button.FindChildren("*", "SpineSprite", true, false).OfType<CanvasItem>().FirstOrDefault();
            if (_merchantVisual != null)
            {
                _visualModulate = _merchantVisual.Modulate;
                _visualSelfModulate = _merchantVisual.SelfModulate;
            }
        }

        _hud = hud;
        _info = BuildInfo();
        hud.AddChild(_info);
        _leave = BuildLeave();
        hud.AddChild(_leave);

        // The player's relic row hangs under the top bar (run.tscn: GlobalUi/RelicInventory, y >= 82, 68px holders);
        // keep the strip (and the Leave button, if relics reach that far right) below it.
        _tree = hud.GetTree();
        _tree.ProcessFrame += OnFrame;
        _info.TreeExiting += Unsubscribe;
    }

    private readonly Control _hud;
    private SceneTree? _tree;
    private int _frame;

    private void Unsubscribe()
    {
        if (_tree != null && GodotObject.IsInstanceValid(_tree)) _tree.ProcessFrame -= OnFrame;
        _tree = null;
    }

    private void OnFrame()
    {
        if (_frame++ % 10 != 0) return;
        if (!Valid(_info) || !Valid(_leave) || !Valid(_hud)) { Unsubscribe(); return; }
        float stripY = TopBarHeight + 10, leaveY = TopBarHeight + 10;
        try
        {
            var relics = NRun.Instance?.GlobalUi?.RelicInventory;
            if (Valid(relics) && relics.IsVisibleInTree())
            {
                var toStage = _hud.GetGlobalTransformWithCanvas().AffineInverse();
                var leaveLeft = _leave.Position.X - 12;
                foreach (var c in relics.GetChildren().OfType<Control>())
                {
                    if (!c.Visible) continue;
                    var r = c.GetGlobalTransformWithCanvas();
                    var br = toStage * (r * c.Size);
                    stripY = Mathf.Max(stripY, br.Y + 6);
                    if (br.X > leaveLeft) leaveY = Mathf.Max(leaveY, br.Y + 6);
                }
            }
        }
        catch
        {
            // keep the defaults
        }
        if (!Mathf.IsEqualApprox(_info.Position.Y, stripY)) _info.Position = new Vector2(_info.Position.X, stripY);
        if (!Mathf.IsEqualApprox(_leave.OffsetTop, leaveY)) { _leave.OffsetTop = leaveY; _leave.OffsetBottom = leaveY; }
    }

    /// <summary>The Merchant character node (for HeartMeter's glow), or null.</summary>
    public CanvasItem? MerchantVisual => Valid(_merchantVisual) ? _merchantVisual : null;

    /// <summary>
    /// True while a full-screen game screen is over the room: map, deck view, pause menu / settings (capstone
    /// screens), card-select overlays (card removal), modals, card/relic inspect, feedback. Detection: the game's
    /// own ActiveScreenContext.GetCurrentScreen() returns the merchant room only when none of those are open (it
    /// checks a handful of properties in order, cheap enough to poll every frame). Also true once the room has
    /// left the tree.
    /// </summary>
    public bool ShouldHideShopUi
    {
        get
        {
            if (_disposed || !Valid(_room) || !_room.IsInsideTree()) return true;
            try
            {
                return !ActiveScreenContext.Instance.IsCurrent(_room);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>e.g. "Bought: 1 card, 0 relics  ·  Act 1 · Floor 6  ·  Visit 2  ·  Haggled off: 30 gold".</summary>
    public void SetInfo(string text)
    {
        if (!Valid(_info)) return;
        var parts = text.Split("  ·  ");
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append("  ·  ");
            var p = parts[i].Replace("[", "[lb]");
            if (parts[i].StartsWith("Haggled", StringComparison.OrdinalIgnoreCase))
                sb.Append("[color=#").Append(Theme.Green.ToHtml(false)).Append(']').Append(p).Append("[/color]");
            else
                sb.Append(p);
        }
        _info.Text = sb.ToString();
    }

    /// <summary>
    /// Optional: moves the stage root (ShopStage.Root) out of its CanvasLayer onto the game's own layer-0 canvas,
    /// as a sibling right after NRun's RoomContainer. It then draws above the room but below the top bar, map,
    /// deck view, card-select overlays, capstone screens, card-fly VFX (TopBar.TrailContainer), hover tips and
    /// modals, and those also take mouse input first. The root is freed when its old CanvasLayer leaves the tree
    /// (ShopStage.Free or the room closing), so ShopStage's lifetime is unchanged. Returns false (and leaves the
    /// stage where it is) if NRun's room container can't be found. Call ShopStage.Fit() afterwards.
    /// </summary>
    public bool EmbedBelowGameUi(Control stageRoot)
    {
        try
        {
            var canvas = stageRoot.GetParent() as CanvasLayer;
            Node? container = null;
            for (Node? n = _room.GetParent(); n != null; n = n.GetParent())
            {
                if (n.GetParent() is NRun) { container = n; break; }
            }
            if (canvas == null || container == null) return false;
            var run = container.GetParent();
            canvas.RemoveChild(stageRoot);
            run.AddChild(stageRoot);
            run.MoveChild(stageRoot, container.GetIndex() + 1);
            canvas.TreeExiting += () =>
            {
                if (GodotObject.IsInstanceValid(stageRoot)) stageRoot.QueueFree();
            };
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] NativeShop: could not embed the stage below the game UI: {e.Message}");
            return false;
        }
    }

    /// <summary>Leave the shop exactly like the native Proceed button (opens the map).</summary>
    public void Leave()
    {
        if (_disposed || !Valid(_room) || ShouldHideShopUi) return;
        try
        {
            if (HideScreenMethod != null)
                HideScreenMethod.Invoke(_room, new object?[] { _proceed });
            else
                NMapScreen.Instance.Open();
        }
        catch (Exception e)
        {
            Log.Error($"[Doki] Leave failed, opening the map directly: {e}");
            try { NMapScreen.Instance.Open(); } catch { }
        }
    }

    /// <summary>Called by the patches: a suppressed native speech line.</summary>
    internal void RaiseNativeLine(string text)
    {
        try { NativeLine?.Invoke(text); }
        catch (Exception e) { Log.Warn($"[Doki] NativeLine handler failed: {e.Message}"); }
    }

    internal bool IsOurProceed(NProceedButton b) => ReferenceEquals(b, _proceed);
    internal bool IsOurRoom(NMerchantRoom r) => ReferenceEquals(r, _room);
    internal bool IsOurInventory(NMerchantInventory i) => Valid(_room) && ReferenceEquals(i, _room.Inventory);
    internal bool IsOurButton(NMerchantButton b) => Valid(_room) && ReferenceEquals(b, _room.MerchantButton);

    internal static void UnregisterHotkeys(NButton b)
    {
        try { UnregisterHotkeysMethod?.Invoke(b, null); }
        catch (Exception e) { Log.Warn($"[Doki] NativeShop: could not unregister Proceed hotkeys: {e.Message}"); }
    }

    /// <summary>Undo everything (called when the shop closes or the mod gives up on this room).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Current == this) Current = null;
        Unsubscribe();
        try
        {
            if (Valid(_proceed))
            {
                _proceed.Visible = _proceedWasVisible;
                if (_proceed.IsEnabled && _proceed.IsInsideTree()) RegisterHotkeysMethod?.Invoke(_proceed, null);
            }
            if (Valid(_dialogue)) _dialogue.Visible = _dialogueWasVisible;
            if (Valid(_merchantVisual))
            {
                _merchantVisual.Modulate = _visualModulate;
                _merchantVisual.SelfModulate = _visualSelfModulate;
            }
            if (Valid(_info)) _info.QueueFree();
            if (Valid(_leave)) _leave.QueueFree();
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] NativeShop.Dispose: {e.Message}");
        }
    }

    // ───────────────────────── UI ─────────────────────────

    private static RichTextLabel BuildInfo()
    {
        var l = new RichTextLabel
        {
            Name = "DokiInfoStrip",
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(60, TopBarHeight + 10),
            Size = new Vector2(1500, 44),
            ClipContents = false,
        };
        var bold = Theme.Bold ?? Theme.Font;
        if (bold != null)
        {
            l.AddThemeFontOverride("normal_font", bold);
            l.AddThemeFontOverride("bold_font", bold);
        }
        l.AddThemeFontSizeOverride("normal_font_size", 30);
        l.AddThemeFontSizeOverride("bold_font_size", 30);
        l.AddThemeColorOverride("default_color", Theme.Ink);
        // text-shadow: 0 2px 4px rgba(0,0,0,.8), plus a thin outline so it reads over the tent.
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        l.AddThemeConstantOverride("shadow_offset_x", 0);
        l.AddThemeConstantOverride("shadow_offset_y", 3);
        l.AddThemeConstantOverride("shadow_outline_size", 6);
        l.AddThemeConstantOverride("outline_size", 4);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
        return l;
    }

    private Button BuildLeave()
    {
        var b = new Button
        {
            Name = "DokiLeave",
            Text = "Leave the shop",
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        // button.danger: linear-gradient(#8f3b3b, #5c2424) is approximated by its midpoint colour.
        var box = Theme.Box(new Color("74302f"), new Color("f0a09f"), 3, 14);
        box.ContentMarginLeft = box.ContentMarginRight = 20;
        box.ContentMarginTop = box.ContentMarginBottom = 6;
        box.ShadowColor = new Color(0, 0, 0, 0.45f);
        box.ShadowSize = 6;
        box.ShadowOffset = new Vector2(0, 3);
        Theme.StyleButton(b, box, 26, Theme.Ink);
        // Top right, just under the game's top bar (whose right-hand icons end at y = 80).
        b.AnchorLeft = b.AnchorRight = 1;
        b.AnchorTop = b.AnchorBottom = 0;
        b.GrowHorizontal = Control.GrowDirection.Begin;
        b.OffsetRight = -28;
        b.OffsetTop = TopBarHeight + 10;
        b.Pressed += Leave;
        return b;
    }

    private static bool Valid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] GodotObject? o) => o != null && GodotObject.IsInstanceValid(o);
}
