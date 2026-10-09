using Godot;

namespace DokiDokiMerchant;

/// <summary>
/// The item detail panel (style.css #detail, main.js renderDetail): art, "#N Name", kind line, description, a
/// "Buy for N gold" button and a round close button. STUB: owned by the DetailPopup agent.
/// </summary>
internal sealed class DetailPopup
{
    /// <summary>Buy pressed for this item. Doki runs the game's own purchase and calls Refresh/Hide.</summary>
    public event Action<Haggle.Item>? BuyPressed;
    /// <summary>Closed with the × button, Escape or a click outside.</summary>
    public event Action? Closed;

    /// <param name="parent">ShopStage.Popups.</param>
    public DetailPopup(Control parent) { }

    public bool IsOpen => false;
    public Haggle.Item? Item => null;

    /// <summary>Show (or switch to) an item. gold decides whether Buy is enabled.</summary>
    public void Show(Haggle.Item item, int gold) { }

    /// <summary>Re-read price, gold and sold state of the item on show (after a deal or a purchase).</summary>
    public void Refresh(int gold) { }

    public void Hide() { }
}
