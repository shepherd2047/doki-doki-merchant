using Godot;

namespace DokiDokiMerchant;

/// <summary>
/// Steam-style achievement toasts at the bottom left (style.css #toasts/.toast), and the achievements themselves
/// (the web game's meta.js list), remembered across runs in achievements.dat next to the mod.
/// STUB: owned by the Toasts agent.
/// </summary>
internal sealed class Toasts
{
    /// <param name="parent">ShopStage.Toasts.</param>
    public Toasts(Control parent) { }

    /// <summary>Unlock an achievement by its web key ("secret", "crush", "friendzone", "transaction", "fool",
    /// "simp", "cheapskate", …) and toast it. Already unlocked (in any run) = nothing happens.</summary>
    public void Unlock(string key) { }

    /// <summary>A toast with any text (trophy icon, small caps header, gold name, small description).</summary>
    public void Show(string header, string name, string description) { }
}
