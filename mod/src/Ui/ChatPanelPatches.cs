using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace DokiDokiMerchant;

/// <summary>
/// While the shop's chat field has focus, keep key events away from the game's two keyboard hotkey paths:
/// NInputManager._UnhandledKeyInput (maps raw keys like M/D/E/Esc to MegaInput actions via Input.ParseInputEvent)
/// and NHotkeyManager._UnhandledInput (runs the bound hotkey actions). A focused LineEdit already consumes printable
/// keys in the GUI pass, and NHotkeyManager skips editing LineEdits itself; this is the backstop for keys the field
/// lets through and for the moment after Enter when the field is focused but no longer "editing".
/// Inert when no shop is open (<see cref="ChatPanel.Typing"/> is false).
/// </summary>
[HarmonyPatch]
internal static class ChatPanelInputManagerPatch
{
    private static MethodBase? TargetMethod() => AccessTools.DeclaredMethod(typeof(NInputManager), "_UnhandledKeyInput", new[] { typeof(InputEvent) });

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(InputEvent __0) => !(ChatPanel.Typing && __0 is InputEventKey);
}

[HarmonyPatch]
internal static class ChatPanelHotkeyManagerPatch
{
    private static MethodBase? TargetMethod() => AccessTools.DeclaredMethod(typeof(NHotkeyManager), "_UnhandledInput", new[] { typeof(InputEvent) });

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(InputEvent __0) => !(ChatPanel.Typing && __0 is InputEventKey);
}

/// <summary>
/// Keyboard-only mode: an arrow key pressed anywhere (NControllerManager._Input, before the GUI sees it) switches the
/// game to keyboard navigation, disables the mouse and moves focus to the screen's default control. While typing,
/// arrows move the caret in our chat field instead.
/// </summary>
[HarmonyPatch]
internal static class ChatPanelArrowKeyPatch
{
    private static MethodBase? TargetMethod() => AccessTools.DeclaredMethod(typeof(NControllerManager), "CheckForArrowKeyInput", new[] { typeof(InputEvent) });

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(InputEvent __0) => !(ChatPanel.Typing && __0 is InputEventKey);
}
