using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace DokiDokiMerchant;

/// <summary>
/// Haggled prices: an accepted offer replaces the entry's cost; three insults put a 50% markup on everything.
/// Both lists are cleared when the shop closes, so prices outside this visit are untouched.
/// </summary>
[HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.Cost), MethodType.Getter)]
internal static class CostPatch
{
    [ThreadStatic] internal static bool Bypass;

    private static void Postfix(MerchantEntry __instance, ref int __result)
    {
        if (Bypass) return;
        if (Haggle.AngerTax.Contains(__instance))
            __result = (int)Math.Round(__result * 1.5);
        else if (Haggle.AgreedPrices.TryGetValue(__instance, out var agreed) && agreed < __result)
            __result = agreed;
    }
}

[HarmonyPatch(typeof(NMerchantRoom), nameof(NMerchantRoom._Ready))]
internal static class MerchantRoomReadyPatch
{
    private static void Postfix(NMerchantRoom __instance)
    {
        try
        {
            Doki.Attach(__instance);
        }
        catch (Exception e)
        {
            Log.Error($"[Doki] Failed to open the shop: {e}");
        }
    }
}

[HarmonyPatch(typeof(NMerchantRoom), nameof(NMerchantRoom._ExitTree))]
internal static class MerchantRoomExitPatch
{
    private static void Prefix(NMerchantRoom __instance)
    {
        try
        {
            Doki.Detach(__instance);
        }
        catch (Exception e)
        {
            Log.Error($"[Doki] Failed to close the shop: {e}");
        }
    }
}
