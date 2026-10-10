using MegaCrit.Sts2.Core.Localization;

namespace DokiDokiMerchant;

/// <summary>
/// The shop's own words in English or Simplified Chinese. The config's "language" picks one; "auto" (the default)
/// follows the game's language setting. Set once per shop visit, before the UI is built.
/// </summary>
internal static class Loc
{
    public static bool Zh { get; private set; }

    /// <summary>The language code for Fish speech recognition and the model's prompts: "zh" or "en".</summary>
    public static string Code => Zh ? "zh" : "en";

    public static string T(string en, string zh) => Zh ? zh : en;

    public static void Apply(string configured)
    {
        var lang = (configured ?? "").Trim().ToLowerInvariant();
        if (lang is "" or "auto") lang = GameLanguage();
        Zh = lang.StartsWith("zh") || lang is "chinese" or "中文";
    }

    private static string GameLanguage()
    {
        try
        {
            // The game's three-letter codes: "zhs" / "zht" for Chinese, "eng", "jpn", …
            return LocManager.Instance?.Language ?? "eng";
        }
        catch
        {
            return "eng";
        }
    }

    /// <summary>An affection level's name for the UI (the model always gets the English one).</summary>
    public static string Level(string en) => !Zh ? en : en switch
    {
        "Stranger" => "陌生人",
        "Customer" => "顾客",
        "Regular" => "熟客",
        "Favourite" => "心头好",
        "Darling" => "亲爱的",
        _ => en,
    };

    /// <summary>"Rare", "Uncommon", "Attack"… as shown on the item popup.</summary>
    public static string Word(string en) => !Zh ? en : en switch
    {
        "Basic" => "基础",
        "Common" => "普通",
        "Uncommon" => "罕见",
        "Rare" => "稀有",
        "Special" => "特殊",
        "Curse" => "诅咒",
        "Status" => "状态",
        "Event" => "事件",
        "Shop" => "商店",
        "Boss" => "首领",
        "Starter" => "初始",
        "Ancient" => "远古",
        "Token" => "衍生",
        "Attack" => "攻击",
        "Skill" => "技能",
        "Power" => "能力",
        "Quest" => "任务",
        "None" => "",
        _ => en,
    };
}
