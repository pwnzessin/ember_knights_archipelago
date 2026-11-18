using HarmonyLib;

namespace EmberKnights.Archipelago;

/// <summary>
/// Adds a line to the info panel of Ember Tree nodes and Ash (weapon mod) upgrades naming the Archipelago check that
/// buying it sends and, when known, the item that check holds. The patches only append to the description text
/// after the game has written it; they never change what an upgrade does.
/// </summary>
internal static class UpgradeTooltipText
{
    public static bool Enabled => ArchipelagoPlugin.Instance?.Service?.IsActive == true;

    public static void ForEmberTreeNode(UITinyText? descLabel, int nodeDefId)
    {
        if (!Enabled || descLabel is null)
            return;
        var service = ArchipelagoPlugin.Instance.Service;
        var catalog = GameCatalog.Data;
        if (catalog.LocationForEmberTreeNode(nodeDefId) is not { } locationId)
            return;
        // Only reveal what a node holds while it can actually be bought (not bought yet, stage open), or once its
        // check has been sent, when the item is no longer a surprise and the line reads "(sent)".
        var sent = service.IsLocationSent(locationId);
        if (!UpgradeTooltip.ShouldShowEmberTreeLine(sent, CanUnlockNow(nodeDefId)))
            return;
        var line = UpgradeTooltip.Format(
            catalog.GetLocationName(locationId),
            service.ScoutedItemForEmberTreeNode(nodeDefId),
            sent);
        descLabel.text = UpgradeTooltip.Append(descLabel.text, line);
    }

    /// <summary>
    /// Reads the live save and the game's own definitions to decide whether the node can be bought right now.
    /// Any failure to read them hides the line rather than showing it, so nothing leaks by mistake.
    /// </summary>
    private static bool CanUnlockNow(int nodeDefId)
    {
        try
        {
            var saveSlot = GameCode.Game?.LocalSaveSlot;
            var def = XEmberTreeUpgradeDef.Get(nodeDefId);
            var levels = GameCode.Defs?.emberTreeFile?.EmberTreeLevels;
            if (saveSlot is null || def is null || levels is null)
                return false;

            var thresholds = new List<int>(levels.Count);
            for (var i = 0; i < levels.Count; i++)
                thresholds.Add(levels[i]);

            // A node the game gave for free (Thick Skin, Slayer) is unlocked at level 0, so it still holds a check
            // until its first level is bought. "Bought" therefore means unlocked with at least one level.
            var data = saveSlot.GetEmberTreeUpgradeData(nodeDefId);
            var bought = data is not null && UnlockBaseline.IsEmberTreeNodePurchased(data.Unlocked, data.Level);
            return UpgradeTooltip.CanUnlockEmberTreeNode(
                bought, def.UnlockStage, saveSlot.GetNumberOfEmberTreeUpgrades(), thresholds);
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Could not tell whether Ember Tree node {nodeDefId} is unlockable: {exception.Message}");
            return false;
        }
    }

    public static void ForWeaponMod(UITinyText? descLabel, int modDefId)
    {
        if (!Enabled || descLabel is null)
            return;
        var service = ArchipelagoPlugin.Instance.Service;
        var catalog = GameCatalog.Data;
        if (catalog.LocationForWeaponMod(modDefId) is not { } locationId)
            return;
        var line = UpgradeTooltip.Format(
            catalog.GetLocationName(locationId),
            service.ScoutedItemForWeaponMod(modDefId),
            service.IsLocationSent(locationId));
        descLabel.text = UpgradeTooltip.Append(descLabel.text, line);
    }
}

[HarmonyPatch(typeof(GameCode.UIEmberTree), nameof(GameCode.UIEmberTree.RefreshInfoSectionWithNode))]
internal static class EmberTreeTooltipPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameCode.UIEmberTree __instance, UIEmberTreeNodeRefs NodeUI)
    {
        try
        {
            if (NodeUI is null)
                return;
            UpgradeTooltipText.ForEmberTreeNode(__instance.Refs?.DescLabel, NodeUI.EmberTreeUpgradeId);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("UIEmberTree.RefreshInfoSectionWithNode", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.UIEmberTreeNew), nameof(GameCode.UIEmberTreeNew.RefreshInfoSectionWithNode))]
internal static class EmberTreeNewTooltipPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameCode.UIEmberTreeNew __instance, UIEmberTreeNodeRefs NodeUI)
    {
        try
        {
            if (NodeUI is null)
                return;
            UpgradeTooltipText.ForEmberTreeNode(__instance.Refs?.DescLabel, NodeUI.EmberTreeUpgradeId);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("UIEmberTreeNew.RefreshInfoSectionWithNode", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.UIWeaponInfoMods), nameof(GameCode.UIWeaponInfoMods.SelectedModSlot))]
internal static class WeaponModTooltipPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameCode.UIWeaponInfoMods __instance)
    {
        try
        {
            var slot = __instance.CurrentModSlot;
            if (slot is null || __instance.IsUltimateSlotSelected)
                return;
            UpgradeTooltipText.ForWeaponMod(__instance.Refs?.ModDesc, slot.ModDefId);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("UIWeaponInfoMods.SelectedModSlot", exception);
        }
    }
}
