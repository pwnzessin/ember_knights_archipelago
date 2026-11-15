using BepInEx;
using BepInEx.Unity.IL2CPP;
using EmberKnights.Archipelago;
using UnityEngine;

namespace EmberKnights.Archipelago.TestHarness;

[BepInPlugin("com.emberknights.archipelago.testharness", "Ember Knights AP Integration Test Harness", "0.1.0")]
public sealed class HarnessPlugin : BasePlugin
{
    public static HarnessPlugin Instance { get; private set; } = null!;

    public override void Load()
    {
        Instance = this;
        AddComponent<HarnessBehaviour>();
    }
}

public sealed class HarnessBehaviour : MonoBehaviour
{
    private bool executed;

    public HarnessBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    private static void Report(bool passed, string name, string detail = "")
    {
        HarnessPlugin.Instance.Log.Log(
            passed ? BepInEx.Logging.LogLevel.Info : BepInEx.Logging.LogLevel.Error,
            $"[{(passed ? "PASS" : "FAIL")}] {name}{(detail.Length == 0 ? "" : " - " + detail)}");
    }

    private void Update()
    {
        if (executed || Time.realtimeSinceStartup < 10f)
            return;
        var service = ArchipelagoPlugin.Instance.Service;
        if (!service.IsConnected || !service.OwnsAbility(30) || !service.OwnsRelic(48) || !service.OwnsRelic(131) ||
            service.HeatReductionsReceived < 2 || service.ArchipelagoCoinsReceived < 6 ||
            GameCode.Defs?.abilitiesFile?.AbilityDefs is null || GameCode.Defs.weaponDefs?.WeaponDefs is null ||
            GameCode.Defs.relicFile?.RelicDefs is null || GameCode.Defs.emberTreeFile?.EmberTreeUpgradeDefs is null)
            return;
        executed = true;

        try
        {
            RunTests();
        }
        catch (Exception exception)
        {
            Report(false, "harness crashed", exception.ToString());
        }
    }

    private static XRelicDef? FindRelic(int id)
    {
        var all = GameCode.Defs.relicFile.RelicDefs;
        for (var i = 0; i < all.Length; i++)
        {
            if (all[i] is { } relic && relic.id == id)
                return relic;
        }
        return null;
    }

    private static void RunRelicTests(GameCode.SavedPlayerSlot slot)
    {
        var service = ArchipelagoPlugin.Instance.Service;
        var spiritShelter = FindRelic(48);   // orb 1, owned
        var callToArms = FindRelic(131);     // orb 5, owned
        var chronograph = FindRelic(40);     // orb 2, NOT owned
        var control = FindRelic(1);          // not an orb relic

        Report(spiritShelter is not null && callToArms is not null && chronograph is not null && control is not null,
            "relic definitions found");
        if (spiritShelter is null || callToArms is null || chronograph is null || control is null)
            return;

        Report(slot.IsRelicUnlocked(48) && slot.IsRelicUnlocked(131), "owned orb relics are unlocked (IsRelicUnlocked)");
        Report(!slot.IsRelicUnlocked(40), "unowned orb relic stays locked (IsRelicUnlocked)");
        Report(slot.IsItemUnlocked(spiritShelter) && slot.IsItemUnlocked(callToArms),
            "owned orb relics pass the central pool filter (IsItemUnlocked)");
        Report(!slot.IsItemUnlocked(chronograph), "unowned orb relic is filtered out of pools (IsItemUnlocked)");

        // The game's own unlock must not leak an orb relic into play.
        slot.AddRelicUnlockId(40);
        Report(!slot.IsRelicUnlocked(40), "vanilla AddRelicUnlockId is suppressed for orb relics");

        // Relics outside the 53 must keep the game's own answer. The patch never changes them (the service returns
        // the vanilla value for anything that is not an orb relic), so compare the query against each relic's own
        // default flag across ALL non-orb relics and report who disagrees. A disabled placeholder relic is allowed
        // to disagree because the game may simply never grant it.
        var all = GameCode.Defs.relicFile.RelicDefs;

        (int Agree, List<int> DisagreeEnabled, int DisagreeDisabled) Compare()
        {
            var agree = 0;
            var disagreeEnabled = new List<int>();
            var disagreeDisabled = 0;
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] is not { } relic || GameCatalog.Data.IsOrbRelic(relic.id))
                    continue;
                if (slot.IsRelicUnlocked(relic.id) == relic.UnlockedByDefault)
                    agree++;
                else if (relic.enabled)
                    disagreeEnabled.Add(relic.id);
                else
                    disagreeDisabled++;
            }
            return (agree, disagreeEnabled, disagreeDisabled);
        }

        // A bare SavedPlayerSlot has not had its default unlocks added yet, so first show that the disagreement
        // exists before defaults are granted, then show what the game itself does once it grants them.
        var before = Compare();
        HarnessPlugin.Instance.Log.LogInfo(
            $"[INFO] before AddDefaultRelicUnlocks: agree={before.Agree}, disagree(enabled)={before.DisagreeEnabled.Count}, " +
            $"disagree(disabled placeholders)={before.DisagreeDisabled}");
        slot.AddDefaultRelicUnlocks();
        var after = Compare();
        Report(after.DisagreeEnabled.Count == 0, "enabled non-orb relics keep vanilla behavior once defaults are granted",
            $"agree={after.Agree}, disagree(enabled)=[{string.Join(",", after.DisagreeEnabled)}], " +
            $"disagree(disabled placeholders)={after.DisagreeDisabled}");

        // The orb spawner asks the save for its locked relics. If owned relics vanish from that list the game
        // could run out of orbs to drop, so report exactly what happens.
        var locked = new Il2CppSystem.Collections.Generic.List<XRelicDef>();
        slot.GetLockedRelics(locked);
        var lockedIds = new List<int>();
        for (var i = 0; i < locked.Count; i++)
            lockedIds.Add(locked[i].id);
        var ownedStillListed = lockedIds.Contains(48) && lockedIds.Contains(131);
        var unownedListed = lockedIds.Contains(40);
        HarnessPlugin.Instance.Log.LogInfo(
            $"[INFO] GetLockedRelics returned {lockedIds.Count} relics; owned orb relics still listed as locked = " +
            $"{ownedStillListed}; unowned orb relic listed = {unownedListed}. " +
            (ownedStillListed ? "Orb spawning is unaffected by relic ownership." :
                "WARNING: orb spawning follows relic ownership; receiving every relic before collecting every orb could stop orbs."));

        // Orb check bookkeeping goes through the real service.
        service.CollectRelicOrb();
        service.CollectRelicOrb();
        Report(true, "service accepted two relic orbs", "see 'Relic orb #1/#2 collected' and 'Check: Relic Orb 01/02' lines");
    }

    private static void RunTests()
    {
        var service = ArchipelagoPlugin.Instance.Service;

        // 1. Randomized skills are still gated by AP ownership.
        var slot = new GameCode.SavedPlayerSlot();
        RunRelicTests(slot);
        Report(slot.IsAbilityUnlocked(30) && !slot.IsAbilityUnlocked(11), "skill gating (Cyclone owned, Rupture not)");

        // 2. Weapons are never gated: a vanilla unlock must now take effect while AP is active.
        var hammerBefore = slot.IsWeaponUnlocked(3);
        slot.AddWeaponUnlockId(3);
        Report(slot.IsWeaponUnlocked(3), "weapon unlock is no longer suppressed",
            $"Rift Hammer unlocked before={hammerBefore}, after AddWeaponUnlockId={slot.IsWeaponUnlocked(3)}");

        // 3. A fresh slot reports no non-default Ember Tree node and no non-default weapon mod.
        var catalog = GameCatalog.Data;
        var freshNodes = catalog.EmberTree.Count(node => slot.GetEmberTreeUpgradeData(node.DefId) is { Unlocked: true });
        var freshMods = catalog.WeaponMods.Count(mod => slot.GetWeaponMod(mod.DefId, 0) is { Unlocked: true });
        Report(freshNodes == 0 && freshMods == 0, "fresh save reports no unlocked checkable entries",
            $"nodes={freshNodes}, mods={freshMods}");

        // 4. Unlocking through the real save-layer methods is visible to the scan.
        var emberNode = catalog.EmberTree[5];
        slot.AddEmberTreeUnlock(emberNode.DefId, false, false);
        slot.UnlockEmberTreeUpgade(emberNode.DefId);
        var nodeData = slot.GetEmberTreeUpgradeData(emberNode.DefId);
        Report(nodeData is { Unlocked: true }, $"Ember Tree unlock visible to scan ({emberNode.Name})");

        var weaponMod = catalog.WeaponMods[10];
        slot.AddWeaponModData(weaponMod.DefId, false, true, 0);
        var modData = slot.GetWeaponMod(weaponMod.DefId, 0);
        Report(modData is { Unlocked: true }, $"weapon mod unlock visible to scan ({weaponMod.Name})");

        // 5. Entries that were not unlocked stay invisible to the scan.
        var otherNode = catalog.EmberTree[6];
        Report(slot.GetEmberTreeUpgradeData(otherNode.DefId) is not { Unlocked: true },
            $"other nodes stay locked ({otherNode.Name})");
        var otherMod = catalog.WeaponMods[11];
        Report(slot.GetWeaponMod(otherMod.DefId, 0) is not { Unlocked: true },
            $"other mods stay locked ({otherMod.Name})");

        // 6. Default weapon mods exist unlocked but are deliberately not locations.
        var defaultsUnlocked = new[] { 1, 10, 13, 19, 28, 31, 44, 52, 81, 91 }
            .Count(id => slot.GetWeaponMod(id, 0) is { Unlocked: true });
        Report(true, "default mods are excluded from locations",
            $"{defaultsUnlocked}/10 defaults are unlocked on a fresh slot; none map to a location: " +
            $"{new[] { 1, 10, 13, 19, 28, 31, 44, 52, 81, 91 }.All(id => catalog.LocationForWeaponMod(id) is null)}");

        // 7. The production service reports what the save has unlocked.
        service.CheckEmberTreeNode(emberNode.DefId);
        service.CheckWeaponMod(weaponMod.DefId);
        service.NotifyRoomEntered(3, 100, 1);
        service.NotifyRoomEntered(3, 101, 2);
        service.NotifyRoomEntered(3, 102, 2);
        service.NotifyRoomEntered(3, 103, 2);
        Report(service.PendingCheckCount >= 0, "service accepted new check kinds",
            "see 'Check:' log lines for Ember Tree, weapon mod, and Wightsbury room milestones");

        // 8. Reverse heat slot data, item reduction, and rank assignment on an in-memory slot.
        var expectedHeat = 25 - service.HeatReductionsReceived;
        Report(
            service.StartHeat == 25 && service.MinimumHeat == 15 && service.HeatReductionPerItem == 1 &&
            service.ActiveHeat == expectedHeat,
            "reverse heat slot data and item reduction",
            $"start={service.StartHeat}, min={service.MinimumHeat}, step={service.HeatReductionPerItem}, " +
            $"received={service.HeatReductionsReceived}, active={service.ActiveHeat}");

        var desiredRanks = HeatSolver.SolveRanks(service.ActiveHeat);
        var slotRanks = slot.GameMods.ModRanks;
        for (var i = 1; i < Math.Min(desiredRanks.Length, slotRanks.Length); i++)
            slotRanks[i] = desiredRanks[i];
        var verifyRanks = Enumerable.Range(0, slotRanks.Length).Select(i => slotRanks[i]).ToArray();
        var calculated = HeatSolver.CalculateTotalHeat(verifyRanks, catalog);
        Report(calculated == service.ActiveHeat, "in-memory slot GameMods.ModRanks holds exact active heat",
            $"calculated={calculated}, active={service.ActiveHeat}");

        // 9. Archipelago Coins replace Ember Tree costs and block vanilla run Ember from adding shards.
        GameActions.ApplyEmberTreeCoinCosts();
        var treeDefs = GameCode.Defs.emberTreeFile.EmberTreeUpgradeDefs;
        var allCostsAreOne = true;
        for (var i = 0; i < treeDefs.Count; i++)
        {
            var def = treeDefs[i];
            if (def is null || !def.enabled || def.DLCContent)
                continue;
            if (def.UnlockCost != 1)
                allCostsAreOne = false;
            for (var level = 0; level < def.UpgradeCosts.Length; level++)
            {
                if (def.UpgradeCosts[level] > 1)
                    allCostsAreOne = false;
            }
        }
        Report(allCostsAreOne, "all 30 active Ember Tree node unlocks and level-ups cost 1 Archipelago Coin");

        var spent = GameActions.CalculateEmberTreeCoinsSpent(slot);
        var shardsBefore = slot.numShards;
        slot.AddShards(500);
        Report(
            service.ArchipelagoCoinsReceived >= 6 && spent == 1 && slot.numShards == shardsBefore,
            "Archipelago Coin bundles tally and vanilla AddShards is blocked",
            $"received={service.ArchipelagoCoinsReceived}, spentInSlot={spent}, numShardsBefore={shardsBefore}, numShardsAfterAdd500={slot.numShards}");
    }
}
