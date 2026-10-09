using Godot;

namespace DokiDokiMerchant;

internal enum StampKind { Love, Bad, Deal, Info }

/// <summary>
/// The juice (style.css .stamp/.heart-pop/.vignette/#stage.jolt): big rotated judgement stamps with a sub-pill,
/// ray burst and flying hearts; floating "+8 ♥" pops; level-up/down screen vignettes; the screen jolt.
/// STUB: owned by the Fx agent.
/// </summary>
internal sealed class Fx
{
    /// <param name="parent">ShopStage.Fx.</param>
    /// <param name="stageRoot">ShopStage.Root, shaken by Jolt.</param>
    public Fx(Control parent, Control stageRoot) { }

    /// <summary>e.g. Stamp("CRITICAL FLIRT!", StampKind.Love, "+8 ♥"). Centred at (1290, 330) like the web.</summary>
    public void Stamp(string text, StampKind kind, string? sub = null) { }

    /// <summary>A "+N ♥" (pink, rising) or "−N ♥" (grey) pop at a stage position.</summary>
    public void HeartPop(int delta, Vector2 at) { }

    /// <summary>A small pink note rising from the heart meter, e.g. "he won't warm up more this visit".</summary>
    public void Note(string text, Vector2 at) { }

    /// <summary>Full-screen pink (up) or blue-grey (down) inner glow with a big title and smaller sub line.</summary>
    public void Vignette(bool up, string title, string? sub = null) { }

    /// <summary>Shake the whole stage briefly.</summary>
    public void Jolt() { }
}
