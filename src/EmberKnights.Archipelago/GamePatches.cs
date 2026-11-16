using HarmonyLib;

namespace EmberKnights.Archipelago;

internal static class PatchSafety
{
    public static void Report(string hook, Exception exception) =>
        ArchipelagoPlugin.LogSource.LogError($"Archipelago hook '{hook}' failed: {exception}");
}

[HarmonyPatch(typeof(GameCode.PlayerUtils), "PickupSkillTablet")]
internal static class SkillTabletPickupPatch
{
    private static readonly HashSet<IntPtr> ConsumedDrops = new();

    [HarmonyPostfix]
    private static void Postfix(GameCode.XEntity DropENT, GameCode.XDropCMP DropCMP, bool __result)
    {
        try
        {
            var service = ArchipelagoPlugin.Instance.Service;
            if (!__result || !service.IsActive || DropCMP is null ||
                (GameCatalog.Data.SkillForAbility(DropCMP.SkillTabletDefId) is null &&
                 !(service.DlcActive && GameCatalog.Data.DlcSkillForAbility(DropCMP.SkillTabletDefId) is not null)))
                return;
            if (DropENT is not null && !ConsumedDrops.Add(DropENT.Pointer))
                return;
            service.CheckSkillTablet(DropCMP.SkillTabletDefId);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("PickupSkillTablet", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.PlayerUtils), "PickupUnknownRelicDrop")]
internal static class RelicOrbPickupPatch
{
    private static readonly HashSet<IntPtr> ConsumedDrops = new();

    [HarmonyPostfix]
    private static void Postfix(GameCode.XEntity DropENT, GameCode.XDropCMP DropCMP, bool __result)
    {
        try
        {
            var service = ArchipelagoPlugin.Instance.Service;
            // Only the 53 base-game orbs are randomized; DLC orbs and anything else keep vanilla behavior.
            // The check is sent the moment the orb is picked up. The orb itself still goes into the save's pending
            // list (nothing suppresses that), so Esper can identify it later.
            if (!__result || !service.IsActive || DropCMP is null)
                return;
            var relicDefId = DropCMP.UnknownRelicDefId;
            var isBaseOrb = GameCatalog.Data.IsOrbRelic(relicDefId);
            var isDlcOrb = service.DlcActive && GameCatalog.Data.IsDlcOrbRelic(relicDefId);
            if (!isBaseOrb && !isDlcOrb)
                return;
            if (DropENT is not null && !ConsumedDrops.Add(DropENT.Pointer))
                return;
            if (isBaseOrb)
                service.CollectRelicOrb();
            else
                service.CollectDlcRelicOrb();
        }
        catch (Exception exception)
        {
            PatchSafety.Report("PickupUnknownRelicDrop", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.XGameState), nameof(GameCode.XGameState.AddSkillTabletCollected))]
internal static class SkillTabletRunStatGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(int __0, ref bool __result)
    {
        try
        {
            if (!ArchipelagoPlugin.Instance.Service.ShouldSuppressAbilityGrant(__0))
                return true;
            __result = false;
            return false;
        }
        catch (Exception exception)
        {
            PatchSafety.Report("AddSkillTabletCollected", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.IsRelicUnlocked))]
internal static class RelicUnlockQueryPatch
{
    [HarmonyPostfix]
    private static void Postfix(int __0, ref bool __result)
    {
        try
        {
            __result = ArchipelagoPlugin.Instance.Service.ResolveRelicUnlocked(__0, __result);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("IsRelicUnlocked", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.AddRelicUnlockId))]
internal static class RelicGrantGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(int __0)
    {
        try
        {
            return !ArchipelagoPlugin.Instance.Service.ShouldSuppressRelicGrant(__0);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("AddRelicUnlockId", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.AddNewRelic))]
internal static class NewRelicMarkerGuardPatch
{
    // Esper marks every relic she identifies as "new". Orb relics are never unlocked in the save (the multiworld
    // owns them), so a marker for them would point at a relic the save does not contain.
    [HarmonyPrefix]
    private static bool Prefix(int __0)
    {
        try
        {
            return !ArchipelagoPlugin.Instance.Service.ShouldSuppressRelicGrant(__0);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("AddNewRelic", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(GameCode.GameUtils), nameof(GameCode.GameUtils.GenerateShopItems))]
internal static class FirstShopPatch
{
    // The first shop of a new save is scripted: it forces Bone Plate and Tainted Conduit whatever the player owns.
    // Before any shop stock is generated, turn those forced relic slots into ordinary ones so the slot is rolled from
    // the normal shop pool, which only offers relics the player has unlocked. Doing it here, right before the stock is
    // built, means it holds however the shop room was reached and does not depend on a timer.
    [HarmonyPrefix]
    private static void Prefix()
    {
        try
        {
            if (ArchipelagoPlugin.Instance.Service.IsActive)
                GameActions.ApplyFirstShopRandomization();
        }
        catch (Exception exception)
        {
            PatchSafety.Report("GenerateShopItems", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.GameUtils), nameof(GameCode.GameUtils.StartEsperIdentifyRelicsSequence))]
internal static class EsperTurnInPatch
{
    // The game unlocks every pending relic and clears the pending list inside this single call, so the number of
    // pending orb relics has to be read before it runs. The checks were already sent when the orbs were picked up;
    // this only decides what Esper's reveal shows, and only if the call completed.
    [HarmonyPrefix]
    private static void Prefix(GameCode.SavedPlayerSlot SaveSlot, ref int __state)
    {
        __state = 0;
        try
        {
            var service = ArchipelagoPlugin.Instance.Service;
            if (!service.IsActive || SaveSlot?.unknownRelics is not { } pending)
                return;
            var defIds = new List<int>(pending.Count);
            for (var i = 0; i < pending.Count; i++)
                defIds.Add(pending[i]);
            __state = RelicOrbPolicy.CountPendingOrbRelics(defIds, GameCatalog.Data.IsOrbRelic);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("StartEsperIdentifyRelicsSequence(pre)", exception);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(GameCode.XRoomCMP RoomCMP, int __state)
    {
        try
        {
            if (__state <= 0)
                return;
            var service = ArchipelagoPlugin.Instance.Service;
            ShowRevealedItems(service, RoomCMP, __state);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("StartEsperIdentifyRelicsSequence(post)", exception);
        }
    }

    /// <summary>
    /// The reveal cutscene walks <c>RoomCMP.UnknownRelicsIdentified</c>. For every orb relic in it the game would
    /// show its own relic. Show what the multiworld really holds for that orb instead: when it is one of this
    /// game's relics the cutscene shows that relic, and when it is anything else (another player's item, gold, a
    /// trap) the cutscene keeps the game's relic and the overlay names the real item.
    /// </summary>
    private static void ShowRevealedItems(ConnectionService service, GameCode.XRoomCMP? roomCmp, int orbsHandedIn)
    {
        if (service.TakeOrbReveals(orbsHandedIn) is not { } reveals)
            return;

        var identified = roomCmp?.UnknownRelicsIdentified;
        var allRelics = GameCode.Defs?.relicFile?.RelicDefs;
        var canSwap = identified is not null && allRelics is not null;
        if (!canSwap)
            ArchipelagoPlugin.LogSource.LogWarning("Esper reveal: relic lists unavailable; the game's own relics will be shown.");

        var slot = 0;
        var listIndex = 0;
        while (slot < reveals.Length)
        {
            // Walk the cutscene list for the next orb relic; outside the list there is nothing to swap.
            XRelicDef? shown = null;
            if (canSwap)
            {
                while (listIndex < identified!.Count)
                {
                    var candidate = identified[listIndex];
                    if (candidate is not null && GameCatalog.Data.IsOrbRelic(candidate.id))
                    {
                        shown = candidate;
                        break;
                    }
                    listIndex++;
                }
            }

            var reveal = reveals[slot++];
            if (reveal.RelicDefId is { } wanted)
            {
                if (shown is not null && TrySwapRelic(identified!, allRelics!, listIndex, wanted))
                    ArchipelagoPlugin.LogSource.LogInfo($"Esper reveal: showing relic {wanted} instead of {shown.id}.");
            }
            else if (reveal.Text is { } text)
            {
                service.ShowEsperMessage(text);
            }

            if (shown is not null)
                listIndex++;
        }
    }

    private static bool TrySwapRelic(
        Il2CppSystem.Collections.Generic.List<XRelicDef> identified,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<XRelicDef> allRelics,
        int index,
        int wantedDefId)
    {
        for (var r = 0; r < allRelics.Count; r++)
        {
            if (allRelics[r]?.id != wantedDefId)
                continue;
            identified[index] = allRelics[r];
            return true;
        }
        return false;
    }
}

[HarmonyPatch(typeof(GameCode.XRoomSYS), nameof(GameCode.XRoomSYS.CompleteMinibossFight))]
internal static class MinibossCompletionPatch
{
    // The game tears down the fight data while completing the fight, so the participants are read
    // before the original method runs and reported afterwards.
    private static readonly List<int> participants = new();

    [HarmonyPrefix]
    private static void Prefix(GameCode.XWorld World)
    {
        participants.Clear();
        try
        {
            if (!ArchipelagoPlugin.Instance.Service.IsActive || World is null || World.IsBossRush ||
                World.IsDailyDungeon || World.MiniBossFight?.EntryList is null)
                return;

            for (var index = 0; index < World.MiniBossFight.EntryList.Count; index++)
            {
                var entry = World.MiniBossFight.EntryList[index];
                if (entry is not null)
                    participants.Add((int)entry.MiniBossType);
            }
        }
        catch (Exception exception)
        {
            PatchSafety.Report("CompleteMinibossFight (capture)", exception);
        }
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            var service = ArchipelagoPlugin.Instance.Service;
            if (!service.IsActive)
                return;

            ArchipelagoPlugin.LogSource.LogInfo(
                $"Miniboss fight completed with enemy IDs [{string.Join(", ", participants)}].");
            foreach (var enemyId in participants)
                service.CheckMiniboss(enemyId);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("CompleteMinibossFight", exception);
        }
        finally
        {
            participants.Clear();
        }
    }
}

[HarmonyPatch(typeof(GameCode.EnemyUtils), nameof(GameCode.EnemyUtils.DispatchEnemyKilledEvent))]
internal static class BossKillPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameCode.EnemyParams_t P)
    {
        try
        {
            var service = ArchipelagoPlugin.Instance.Service;
            if (!service.IsActive || P.World is null || P.World.IsBossRush || P.World.IsDailyDungeon)
                return;
            service.CheckBoss((int)P.EnemyType);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("DispatchEnemyKilledEvent", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.XRoomSYS), nameof(GameCode.XRoomSYS.PraxisDefeatedGameOver))]
internal static class FinalBossPatch
{
    [HarmonyPostfix]
    private static void Postfix(bool IsTrueEnding, bool IsArchitectFight)
    {
        try
        {
            var service = ArchipelagoPlugin.Instance.Service;
            if (!service.IsActive || IsArchitectFight)
                return;
            service.ReportFinalBoss(IsTrueEnding ? "arch_praxis" : "praxis");
        }
        catch (Exception exception)
        {
            PatchSafety.Report("PraxisDefeatedGameOver", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.IsAbilityUnlocked))]
internal static class AbilityUnlockQueryPatch
{
    [HarmonyPostfix]
    private static void Postfix(int __0, ref bool __result)
    {
        try
        {
            __result = ArchipelagoPlugin.Instance.Service.ResolveAbilityUnlocked(__0, __result);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("IsAbilityUnlocked", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.IsWeaponUnlocked))]
internal static class WeaponUnlockQueryPatch
{
    [HarmonyPostfix]
    private static void Postfix(int __0, ref bool __result)
    {
        try
        {
            __result = ArchipelagoPlugin.Instance.Service.ResolveWeaponUnlocked(__0, __result);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("IsWeaponUnlocked", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.IsItemUnlocked))]
internal static class ItemUnlockQueryPatch
{
    [HarmonyPostfix]
    private static void Postfix(Il2CppSystem.Object item, ref bool __result)
    {
        try
        {
            var service = ArchipelagoPlugin.Instance.Service;
            if (!service.IsActive || item is null)
                return;
            if (item.TryCast<XAbilityDef>() is { } ability)
                __result = service.ResolveAbilityUnlocked(ability.id, __result);
            else if (item.TryCast<XWeaponDef>() is { } weapon)
                __result = service.ResolveWeaponUnlocked(weapon.id, __result);
            else if (item.TryCast<XRelicDef>() is { } relic)
                __result = service.ResolveRelicUnlocked(relic.id, __result);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("IsItemUnlocked", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.AddAbilityUnlockId))]
internal static class AbilityGrantGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(int __0, ref bool __result)
    {
        try
        {
            if (!ArchipelagoPlugin.Instance.Service.ShouldSuppressAbilityGrant(__0))
                return true;
            __result = false;
            return false;
        }
        catch (Exception exception)
        {
            PatchSafety.Report("AddAbilityUnlockId", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.AddWeaponUnlockId))]
internal static class WeaponGrantGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(int __0)
    {
        try
        {
            return !ArchipelagoPlugin.Instance.Service.ShouldSuppressWeaponGrant(__0);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("AddWeaponUnlockId", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(GameCode.GameUtils), nameof(GameCode.GameUtils.CanUnlockWeaponDef))]
internal static class WeaponDefUnlockConditionPatch
{
    [HarmonyPostfix]
    private static void Postfix(XWeaponDef __0, ref bool __result)
    {
        try
        {
            // Managed weapons are the base ones, plus the DLC ones only when the seed uses the DLC.
            if (ArchipelagoPlugin.Instance.Service.IsActive && __0 is not null &&
                ArchipelagoPlugin.Instance.Service.IsManagedWeapon(__0.id))
            {
                __result = false;
            }
        }
        catch (Exception exception)
        {
            PatchSafety.Report("CanUnlockWeaponDef", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.GameInst), nameof(GameCode.GameInst.CheckForWeaponPodiumUnlocksCutscene))]
internal static class WeaponPodiumCutsceneGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        try
        {
            if (!ArchipelagoPlugin.Instance.Service.IsActive)
                return true;
            __result = false;
            return false;
        }
        catch (Exception exception)
        {
            PatchSafety.Report("CheckForWeaponPodiumUnlocksCutscene", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(GameCode.XWorld), nameof(GameCode.XWorld.SetCurrentRoom))]
internal static class RoomEnteredPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameCode.XWorld __instance, GameCode.XRoomCMP roomCMP)
    {
        try
        {
            var service = ArchipelagoPlugin.Instance.Service;
            if (!service.IsActive || roomCMP is null || __instance?.currentArea is null)
                return;
            // Extra game modes reuse the world layouts but are not campaign progress.
            if (__instance.IsBossRush || __instance.IsDailyDungeon)
                return;
            service.NotifyRoomEntered((int)__instance.currentArea.areaName, roomCMP.RoomInstId, (int)roomCMP.roomType);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("SetCurrentRoom", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.XWorld), nameof(GameCode.XWorld.SetGameModRank))]
internal static class GameModRankLockPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            if (!GameActions.ApplyingHeat && ArchipelagoPlugin.Instance.Service.IsActive)
                ArchipelagoPlugin.Instance.Service.EnforceHeat();
        }
        catch (Exception exception)
        {
            PatchSafety.Report("SetGameModRank", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.XWorld), nameof(GameCode.XWorld.SetGameModRankBit))]
internal static class GameModRankBitLockPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            if (!GameActions.ApplyingHeat && ArchipelagoPlugin.Instance.Service.IsActive)
                ArchipelagoPlugin.Instance.Service.EnforceHeat();
        }
        catch (Exception exception)
        {
            PatchSafety.Report("SetGameModRankBit", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.GameUtils), nameof(GameCode.GameUtils.CanInteractWithEmberTree))]
internal static class EmberTreeInteractablePatch
{
    [HarmonyPostfix]
    private static void Postfix(ref bool __result)
    {
        try
        {
            if (ArchipelagoPlugin.Instance.Service.IsActive)
                __result = true;
        }
        catch (Exception exception)
        {
            PatchSafety.Report("CanInteractWithEmberTree", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.IsWeaponModsUnlocked))]
internal static class WeaponModsUnlockedPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref bool __result)
    {
        try
        {
            if (ArchipelagoPlugin.Instance.Service.IsActive)
                __result = true;
        }
        catch (Exception exception)
        {
            PatchSafety.Report("IsWeaponModsUnlocked", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.AddShards))]
internal static class BlockVanillaEmberShardsPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        try
        {
            var service = ArchipelagoPlugin.Instance.Service;
            if (!service.IsActive)
                return true;
            service.EnforceArchipelagoCoins();
            return false;
        }
        catch (Exception exception)
        {
            PatchSafety.Report("AddShards", exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.SpendShards))]
internal static class SyncArchipelagoCoinsOnSpendPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            var service = ArchipelagoPlugin.Instance.Service;
            if (service.IsActive)
                service.EnforceArchipelagoCoins();
        }
        catch (Exception exception)
        {
            PatchSafety.Report("SpendShards", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.XPlayerSYS), "EnterDeadState")]
internal static class LocalDeathPatch
{
    [HarmonyPrefix]
    private static void Prefix(GameCode.PlayerParams_t P)
    {
        try
        {
            if (P.IsLocalPlayer)
                ArchipelagoPlugin.Instance.Service.NotifyLocalDeath();
        }
        catch (Exception exception)
        {
            PatchSafety.Report("EnterDeadState", exception);
        }
    }
}

// The difficulty menu changes a rank and then redraws the slot from the values it was given. Resetting the rank
// afterwards (GameModRankLockPatch) leaves the slot showing the attempted rank, so only the first press looks like it
// works and later presses start from a wrong value. Ignoring the input before it acts keeps menu and data identical.
[HarmonyPatch(typeof(GameCode.UIGameMods), "OnRightArrowPressed")]
internal static class HeatMenuRightArrowLockPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => HeatMenuLock.AllowInput("OnRightArrowPressed");
}

[HarmonyPatch(typeof(GameCode.UIGameMods), "OnLeftArrowPressed")]
internal static class HeatMenuLeftArrowLockPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => HeatMenuLock.AllowInput("OnLeftArrowPressed");
}

[HarmonyPatch(typeof(GameCode.UIGameMods), nameof(GameCode.UIGameMods.ActivateAllMods))]
internal static class HeatMenuActivateAllLockPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => HeatMenuLock.AllowInput("ActivateAllMods");
}

[HarmonyPatch(typeof(GameCode.UIGameMods), nameof(GameCode.UIGameMods.SetRankAndDispatchEvent))]
internal static class HeatMenuSetRankLockPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => HeatMenuLock.AllowInput("SetRankAndDispatchEvent");
}

internal static class HeatMenuLock
{
    /// <summary>Returns false (skip the original) while Archipelago controls heat. Never throws into the game.</summary>
    public static bool AllowInput(string hook)
    {
        try
        {
            var service = ArchipelagoPlugin.Instance.Service;
            if (!service.ShouldBlockHeatMenuInput)
                return true;
            service.NoteHeatMenuLocked();
            return false;
        }
        catch (Exception exception)
        {
            PatchSafety.Report(hook, exception);
            return true;
        }
    }
}