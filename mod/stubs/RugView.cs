using Godot;

namespace DokiDokiMerchant;

/// <summary>
/// The web game's rug on the left half of the room (style.css #rug/#goods, main.js SLOTS/renderGoods): the
/// original rug texture with the goods on it, each with a number badge, a coin price, sale tag and SOLD stamp.
/// STUB: owned by the RugView agent.
/// </summary>
internal sealed class RugView
{
    /// <summary>The player clicked an item (sold items and the hidden secret card can't be clicked).</summary>
    public event Action<Haggle.Item>? Clicked;

    /// <param name="parent">ShopStage.Rug (1920x1080 stage coordinates).</param>
    /// <param name="items">Everything on the rug in shelf order, including the hidden secret card (Hidden = true).</param>
    /// <param name="gold">The player's current gold, for red unaffordable prices.</param>
    public RugView(Control parent, IReadOnlyList<Haggle.Item> items, Func<int> gold) { }

    /// <summary>The rug drops in from above (0.7s quint ease-out), as when the web game opens.</summary>
    public void DropIn() { }

    /// <summary>Re-read prices (entry.Cost; Agreed/AngerTax for strike-through), sold state, affordability.</summary>
    public void Refresh() { }

    /// <summary>Highlight the item the detail popup is showing (null clears it).</summary>
    public void SetSelected(Haggle.Item? item) { }

    /// <summary>The Merchant is talking about this item: bounce it with a blue glow (web .pointed).</summary>
    public void Point(Haggle.Item item) { }

    /// <summary>The secret card was just stocked into item.Entry: rebuild its visual and rise it into view with the ♥ SECRET ♥ ribbon.</summary>
    public void RevealSecret(Haggle.Item item) { }

    /// <summary>Centre of the item's slot in stage coordinates.</summary>
    public Vector2 SlotCenter(Haggle.Item item) => Vector2.Zero;
}
