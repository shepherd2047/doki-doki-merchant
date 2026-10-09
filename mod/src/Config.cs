using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Logging;

namespace DokiDokiMerchant;

/// <summary>
/// Settings and API keys, read from DokiDokiMerchant.cfg (JSON) next to the mod's DLL. The file is not named
/// .json on purpose: the game's mod loader treats every .json under mods/ as a manifest candidate.
/// </summary>
internal sealed class Config
{
    public string OpenAiKey = "";
    public string OpenAiBaseUrl = "https://api.openai.com/v1";
    public string OpenAiModel = "gpt-4.1-mini";
    public string FishKey = "";
    public string FishTtsModel = "s2.1-pro";
    public string MerchantVoiceId = "739d07bfe41e4cc68cfd8fb4682c69c5";
    public string BestieVoiceId = "18e2b286c7dd4aa19f6720de4ea2b772";
    public bool BestieEnabled = true;
    public string Language = "en";
    public string PushToTalkKey = "V";

    public static string ModDir => Path.GetDirectoryName(typeof(Config).Assembly.Location) ?? ".";
    public static string FilePath => Path.Combine(ModDir, "DokiDokiMerchant.cfg");

    /// <summary>What's missing, in words the player can act on; null when ready to talk.</summary>
    public string? Problem =>
        !File.Exists(FilePath) ? $"Doki Doki Merchant: create {FilePath} (copy DokiDokiMerchant.cfg.example) and add your keys."
        : string.IsNullOrWhiteSpace(OpenAiKey) ? "Doki Doki Merchant: add openai_api_key to DokiDokiMerchant.cfg."
        : string.IsNullOrWhiteSpace(FishKey) ? "Doki Doki Merchant: add fish_api_key to DokiDokiMerchant.cfg."
        : null;

    public static Config Load()
    {
        var c = new Config();
        try
        {
            if (!File.Exists(FilePath)) return c;
            var j = JsonNode.Parse(File.ReadAllText(FilePath))?.AsObject();
            if (j == null) return c;
            string S(string k, string d) => j[k]?.GetValue<string>() is { Length: > 0 } v ? v.Trim() : d;
            c.OpenAiKey = S("openai_api_key", c.OpenAiKey);
            c.OpenAiBaseUrl = S("openai_base_url", c.OpenAiBaseUrl).TrimEnd('/');
            c.OpenAiModel = S("openai_model", c.OpenAiModel);
            c.FishKey = S("fish_api_key", c.FishKey);
            c.FishTtsModel = S("fish_tts_model", c.FishTtsModel);
            c.MerchantVoiceId = S("merchant_voice_id", c.MerchantVoiceId);
            c.BestieVoiceId = S("bestie_voice_id", c.BestieVoiceId);
            c.Language = S("language", c.Language);
            c.PushToTalkKey = S("push_to_talk_key", c.PushToTalkKey);
            if (j["bestie_enabled"] is JsonValue b && b.TryGetValue(out bool be)) c.BestieEnabled = be;
        }
        catch (Exception e)
        {
            Log.Error($"[Doki] Could not read {FilePath}: {e.Message}");
        }
        return c;
    }
}
