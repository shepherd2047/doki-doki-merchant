using Godot;

namespace DokiDokiMerchant;

/// <summary>
/// Bottom-left: the "…or type to the Merchant" bar with its pink "Say it ♥" button (style.css #type-bar), and the
/// retractable "▲ Chat log N" tab above it (style.css #chatlog). STUB: owned by the ChatPanel agent.
/// </summary>
internal sealed class ChatPanel
{
    /// <summary>The player sent a typed line (already trimmed, never empty).</summary>
    public event Action<string>? Submitted;

    /// <param name="parent">ShopStage.Hud.</param>
    public ChatPanel(Control parent) { }

    /// <summary>True while the text field has keyboard focus (push-to-talk must ignore V then).</summary>
    public bool HasFocus => false;

    public void SetEnabled(bool enabled) { }

    /// <summary>Append to the log. who: "you", "merchant", "bestie" or "system".</summary>
    public void Add(string who, string text) { }
}
