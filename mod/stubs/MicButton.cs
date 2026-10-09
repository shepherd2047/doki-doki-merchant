using Godot;

namespace DokiDokiMerchant;

internal enum MicState { Idle, Listening, Thinking, Speaking, Disabled }

/// <summary>
/// The big pink round microphone perched on the dialogue box's corner (style.css #mic/#talk) with a status line
/// under it. Idle breathes, Listening has pulsing rings, Thinking bobs, Speaking shows sound bars on lilac.
/// STUB: owned by the MicButton agent.
/// </summary>
internal sealed class MicButton
{
    /// <summary>Clicked: Doki toggles recording (click to start, click again to send).</summary>
    public event Action? Pressed;

    /// <param name="parent">ShopStage.Hud.</param>
    public MicButton(Control parent) { }

    public void SetState(MicState state) { }

    /// <summary>The line under the button, e.g. "Tap or hold V to talk", "Listening… tap to send".</summary>
    public void SetStatus(string text) { }
}
