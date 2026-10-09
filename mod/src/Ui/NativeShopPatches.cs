using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace DokiDokiMerchant;

// Harmony patches for NativeShop. Every one is inert while NativeShop.Current is null, and only touches the
// merchant room NativeShop was created for (not the Fake Merchant event, which reuses some of these classes).

/// <summary>The native inventory rug never opens (Merchant click, select hotkey, FTUE hitbox all go through here).</summary>
[HarmonyPatch(typeof(NMerchantRoom), nameof(NMerchantRoom.OpenInventory))]
internal static class NativeShopOpenInventoryPatch
{
    private static bool Prefix(NMerchantRoom __instance) => NativeShop.Current?.IsOurRoom(__instance) != true;
}

[HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Open))]
internal static class NativeShopInventoryOpenPatch
{
    private static bool Prefix(NMerchantInventory __instance) => NativeShop.Current?.IsOurInventory(__instance) != true;
}

/// <summary>No first-visit "talk to the merchant" FTUE when leaving: Leave/Proceed goes straight to the map.</summary>
[HarmonyPatch(typeof(NMerchantRoom), "MerchantFtueCheck")]
internal static class NativeShopFtuePatch
{
    private static bool Prefix(NMerchantRoom __instance, ref bool __result)
    {
        if (NativeShop.Current?.IsOurRoom(__instance) != true) return true;
        __result = false;
        return false;
    }
}

/// <summary>Hovering the Merchant: no hover sound or outline skin, unless a potion is being aimed at him.</summary>
[HarmonyPatch(typeof(NMerchantButton), "OnFocus")]
internal static class NativeShopMerchantFocusPatch
{
    private static bool Prefix(NMerchantButton __instance)
    {
        if (NativeShop.Current?.IsOurButton(__instance) != true) return true;
        try
        {
            return NTargetManager.Instance?.IsInSelection == true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Clicking the Merchant does nothing (except finishing a potion throw at him).</summary>
[HarmonyPatch(typeof(NMerchantButton), "OnRelease")]
internal static class NativeShopMerchantReleasePatch
{
    private static readonly AccessTools.FieldRef<NMerchantButton, bool> Targeting =
        AccessTools.FieldRefAccess<NMerchantButton, bool>("_focusedWhileTargeting");

    private static bool Prefix(NMerchantButton __instance)
    {
        if (NativeShop.Current?.IsOurButton(__instance) != true) return true;
        try
        {
            return Targeting(__instance);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>No native speech bubbles over the Merchant; the line is handed to NativeShop.NativeLine instead.</summary>
[HarmonyPatch(typeof(NMerchantButton), nameof(NMerchantButton.PlayDialogue))]
internal static class NativeShopPlayDialoguePatch
{
    private static bool Prefix(NMerchantButton __instance, LocString? line, ref NSpeechBubbleVfx? __result)
    {
        var shop = NativeShop.Current;
        if (shop?.IsOurButton(__instance) != true) return true;
        __result = null;
        if (line != null)
        {
            string text;
            try { text = line.GetFormattedText(); }
            catch { text = ""; }
            if (text.Length > 0) shop.RaiseNativeLine(text);
        }
        return false;
    }
}

/// <summary>The rug's own speech bubble (purchase thanks / can't afford lines) stays quiet.</summary>
[HarmonyPatch(typeof(NMerchantDialogue), nameof(NMerchantDialogue.ShowForPurchaseAttempt))]
internal static class NativeShopPurchaseDialoguePatch
{
    private static bool Prefix(PurchaseStatus status) => NativeShop.Current == null;
}

[HarmonyPatch(typeof(NMerchantDialogue), nameof(NMerchantDialogue.ShowOnInventoryOpen))]
internal static class NativeShopOpenDialoguePatch
{
    private static bool Prefix() => NativeShop.Current == null;
}

/// <summary>The hidden Proceed button re-registers its ui_confirm hotkey whenever it is enabled; drop it again.</summary>
[HarmonyPatch(typeof(NProceedButton), "OnEnable")]
internal static class NativeShopProceedEnablePatch
{
    private static void Postfix(NProceedButton __instance)
    {
        if (NativeShop.Current?.IsOurProceed(__instance) == true) NativeShop.UnregisterHotkeys(__instance);
    }
}
