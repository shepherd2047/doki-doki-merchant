using Godot;

namespace DokiDokiMerchant;

/// <summary>
/// The heart meter above the Merchant (style.css #affection): a row of hearts, a pink bar and "Lv 2 · Customer";
/// plus the glow on the Merchant himself (pink blush at Favourite and above, blue while he speaks).
/// STUB: owned by the HeartMeter agent.
/// </summary>
internal sealed class HeartMeter
{
    /// <param name="parent">ShopStage.Hud.</param>
    /// <param name="merchantVisual">The room's Merchant character node (CanvasItem) to tint, or null.</param>
    public HeartMeter(Control parent, CanvasItem? merchantVisual) { }

    /// <param name="hearts">e.g. "♥♥♡♡♡" (Affection.Hearts).</param>
    /// <param name="progress">0..1 bar fill (Affection.Value / 100).</param>
    /// <param name="levelNo">1..5.</param>
    /// <param name="levelName">Stranger, Customer, Regular, Favourite, Darling.</param>
    /// <param name="blush">Favourite or above.</param>
    public void Set(string hearts, float progress, int levelNo, string levelName, bool blush) { }

    public void SetSpeaking(bool speaking) { }

    /// <summary>Where "+N ♥" pops should rise from, in stage coordinates.</summary>
    public Vector2 PopAnchor => new(1345, 440);
}
