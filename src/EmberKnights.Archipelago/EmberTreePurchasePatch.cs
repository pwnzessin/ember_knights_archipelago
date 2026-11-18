using HarmonyLib;

namespace EmberKnights.Archipelago;

/// <summary>
/// Sends an Ember Tree node's check when the player buys it, and nowhere else.
///
/// The game calls <c>SavedPlayerSlot.UnlockEmberTreeUpgade</c> exactly when a node is bought. While it sets the tree up
/// it calls <c>AddEmberTreeUnlock</c> instead, so the two never overlap. Reporting from the purchase call means the
/// check does not depend on guessing from the save whether a node was bought or merely given: the scan used to take a
/// snapshot of what was unlocked on the first connect, and anything bought before that first scan (or unlocked by the
/// game for free) was swallowed by the snapshot and never sent.
///
/// The prefix remembers whether the node was already unlocked; the postfix reports the check only when the call turned
/// it from locked to unlocked. It changes nothing in the game; it only reads the save.
/// </summary>
[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.UnlockEmberTreeUpgade))]
internal static class EmberTreePurchaseScanPatch
{
    [HarmonyPrefix]
    private static void Prefix(GameCode.SavedPlayerSlot __instance, int __0, ref bool __state)
    {
        __state = false;
        try
        {
            __state = __instance?.GetEmberTreeUpgradeData(__0)?.Unlocked ?? false;
        }
        catch (Exception exception)
        {
            PatchSafety.Report("SavedPlayerSlot.UnlockEmberTreeUpgade (pre)", exception);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(GameCode.SavedPlayerSlot __instance, int __0, bool __state)
    {
        try
        {
            var service = ArchipelagoPlugin.Instance?.Service;
            if (service is null || !service.IsActive)
                return;
            var unlockedAfter = __instance?.GetEmberTreeUpgradeData(__0)?.Unlocked ?? false;
            if (UnlockBaseline.IsEmberTreePurchase(unlockedAfter, __state))
                service.ReportEmberTreePurchase(__0);
            // The regular scan still runs once a second for weapon mods; run it now so the tooltip, which the game
            // redraws right after the purchase, already sees the check as sent.
            service.ScanUnlocksAfterPurchase();
        }
        catch (Exception exception)
        {
            PatchSafety.Report("SavedPlayerSlot.UnlockEmberTreeUpgade", exception);
        }
    }
}
