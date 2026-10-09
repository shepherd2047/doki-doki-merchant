using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace DokiDokiMerchant;

/// <summary>
/// Escape closes the open item popup instead of reaching the game. NInputManager._UnhandledKeyInput turns the raw
/// Escape key into the MegaInput "cancel" and "pauseAndBack" actions (Input.ParseInputEvent), and the top bar's
/// pause button runs on both the pressed and the released binding of pauseAndBack (NButton.RegisterHotkeys), so
/// the press that closed the popup and its release (and echoes in between) are all kept from the game.
/// Inert when no popup is open and visible.
/// </summary>
[HarmonyPatch]
internal static class DetailPopupInputManagerPatch
{
    /// <summary>True once the patch is applied (DetailPopup then stops polling Escape itself).</summary>
    internal static bool Active { get; private set; }

    private static bool _swallowing;

    private static MethodBase? TargetMethod() => AccessTools.DeclaredMethod(typeof(NInputManager), "_UnhandledKeyInput", new[] { typeof(InputEvent) });

    private static bool Prepare()
    {
        Active = TargetMethod() != null;
        return Active;
    }

    private static bool Prefix(InputEvent __0)
    {
        if (__0 is not InputEventKey key || (key.Keycode != Key.Escape && key.PhysicalKeycode != Key.Escape)) return true;
        if (key.Pressed && !key.Echo)
        {
            _swallowing = DetailPopup.Current?.TryCancel() == true;
            return !_swallowing;
        }
        if (!_swallowing) return true;
        if (!key.Pressed) _swallowing = false; // the release of the press we used
        return false;
    }
}

/// <summary>
/// The controller's back/cancel button reaches the game as a MegaInput action in NHotkeyManager._UnhandledInput;
/// close the popup with it too, and keep that press and its release from the game.
/// </summary>
[HarmonyPatch]
internal static class DetailPopupHotkeyManagerPatch
{
    private static bool _swallowing;

    private static MethodBase? TargetMethod() => AccessTools.DeclaredMethod(typeof(NHotkeyManager), "_UnhandledInput", new[] { typeof(InputEvent) });

    private static bool Prepare() => TargetMethod() != null;

    private static bool IsBack(InputEvent e, bool pressed)
    {
        foreach (var a in new[] { MegaInput.cancel, MegaInput.pauseAndBack })
        {
            if (!InputMap.HasAction(a)) continue;
            if (pressed ? e.IsActionPressed(a) : e.IsActionReleased(a)) return true;
        }
        return false;
    }

    private static bool Prefix(InputEvent __0)
    {
        if (IsBack(__0, pressed: true) && !__0.IsEcho())
        {
            _swallowing = DetailPopup.Current?.TryCancel() == true;
            return !_swallowing;
        }
        if (_swallowing && IsBack(__0, pressed: false))
        {
            _swallowing = false;
            return false;
        }
        return true;
    }
}
