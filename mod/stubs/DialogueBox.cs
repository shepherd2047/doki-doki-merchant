using Godot;

namespace DokiDokiMerchant;

/// <summary>
/// The visual-novel text box at the bottom right (style.css #bubble) with its "The Merchant ♥" nameplate, a
/// typewriter and the bouncing ▼ caret; plus the white "You" bubble above it (style.css #you).
/// STUB: owned by the DialogueBox agent.
/// </summary>
internal sealed class DialogueBox
{
    /// <param name="parent">ShopStage.Hud.</param>
    public DialogueBox(Control parent) { }

    /// <summary>Type out a line. who: "merchant" (nameplate "The Merchant ♥"), "bestie" ("Your Bestie ♥",
    /// lilac), or "system" (setup problems, no voice; nameplate "Doki Doki Merchant"). seconds: how long the
    /// voice clip lasts, so typing finishes with the voice (0 = default speed).</summary>
    public void Say(string who, string text, double seconds) { }

    /// <summary>Shake the box (an insult or a lowball).</summary>
    public void Shake() { }

    /// <summary>Show the player's words above the box. live = still transcribing (grey italic with "…").</summary>
    public void ShowYou(string text, bool live) { }

    /// <summary>Fade the You bubble out.</summary>
    public void HideYou() { }
}
