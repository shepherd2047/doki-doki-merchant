using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace DokiDokiMerchant;

/// <summary>
/// Puts the original shop out of sight while keeping it alive (its nodes are still used by the game's purchase
/// code, the card-removal flow and the Merchant's hand), and the strip of info and the "Leave the shop" button
/// that replace it. STUB: owned by the NativeShop agent.
/// </summary>
internal sealed class NativeShop
{
    /// <summary>Active for the current shop visit; native shop behaviour is restored when this is null.</summary>
    public static NativeShop? Current { get; private set; }

    /// <param name="room">The merchant room.</param>
    /// <param name="hud">ShopStage.Hud, for the info strip and Leave button.</param>
    public NativeShop(NMerchantRoom room, Control hud) { Current = this; }

    /// <summary>The Merchant character node (for HeartMeter's glow), or null.</summary>
    public CanvasItem? MerchantVisual => null;

    /// <summary>e.g. "Bought: 1 card, 0 relics  ·  Act 1 · Floor 6  ·  Visit 2  ·  Haggled off: 30 gold".</summary>
    public void SetInfo(string text) { }

    /// <summary>Undo everything (called when the shop closes or the mod gives up on this room).</summary>
    public void Dispose() { if (Current == this) Current = null; }
}
