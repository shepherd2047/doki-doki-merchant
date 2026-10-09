using MegaCrit.Sts2.Core.Modding;

namespace DokiDokiMerchant;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    private const string HarmonyId = "shepherd2047.dokidokimerchant";
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        MegaCrit.Sts2.Core.Logging.Log.Info("[Doki] Loading Doki Doki Merchant v0.1.0");
        new HarmonyLib.Harmony(HarmonyId).PatchAll();
    }
}
