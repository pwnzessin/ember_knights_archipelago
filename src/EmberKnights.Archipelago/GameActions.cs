using UnityEngine;

namespace EmberKnights.Archipelago;

public static class GameActions
{
    private static readonly ePraxisEffectType[] PraxisTrapEffects =
    {
        ePraxisEffectType.SkillDisabler,
        ePraxisEffectType.DarkMeteors,
        ePraxisEffectType.SlowHands,
        ePraxisEffectType.GravityWells,
        ePraxisEffectType.LaserHands,
    };

    private static readonly Dictionary<int, (int UnlockCost, int[] UpgradeCosts)> VanillaEmberTreeCosts = new();
    private static bool emberTreeCostsOverridden;

    public static bool ApplyingHeat { get; private set; }
    public static bool ApplyingWeapons { get; private set; }

    public static bool ApplyWeapons(IReadOnlySet<int> ownedWeapons) => ApplyWeapons(ownedWeapons, includeDlcWeapons: false);

    /// <summary>
    /// Brings the save in line with the weapons the multiworld owns. With <paramref name="includeDlcWeapons"/> the DLC
    /// weapons are reconciled too; without it they are left exactly as the save has them.
    /// </summary>
    public static bool ApplyWeapons(IReadOnlySet<int> ownedWeapons, bool includeDlcWeapons)
    {
        try
        {
            var game = GameCode.Game;
            var world = game?.World;
            if (world is null)
                return false;
            var saveSlot = game!.LocalSaveSlot;
            if (saveSlot?.weaponUnlocks is null)
                return false;

            var changed = false;
            ApplyingWeapons = true;
            try
            {
                if (!saveSlot.weaponUnlocks.Contains(1))
                {
                    saveSlot.AddWeaponUnlockId(1);
                    changed = true;
                }

                // The DLC weapons are reconciled only for a seed that uses the DLC. A seed without it must never add the
                // Hyper Gloves or the Ember Buster to the save, or take them away from a player who owns them.
                var managed = includeDlcWeapons
                    ? GameCatalog.Data.Weapons.Concat(GameCatalog.Data.Dlc.Weapons)
                    : GameCatalog.Data.Weapons;
                foreach (var weapon in managed)
                {
                    var unlockedInSave = saveSlot.weaponUnlocks.Contains(weapon.WeaponId);
                    var shouldBeUnlocked = ownedWeapons.Contains(weapon.WeaponId);
                    if (shouldBeUnlocked && !unlockedInSave)
                    {
                        saveSlot.AddWeaponUnlockId(weapon.WeaponId);
                        changed = true;
                    }
                    else if (!shouldBeUnlocked && unlockedInSave)
                    {
                        saveSlot.weaponUnlocks.Remove(weapon.WeaponId);
                        changed = true;
                    }
                }
            }
            finally
            {
                ApplyingWeapons = false;
            }

            if (changed && (int)(world.currentArea?.areaName ?? 0) == RoomProgressTracker.NexusArea)
                GameCode.XEventTriggerSYS.UpdateAllWeaponPlatformState(saveSlot, world, false, true);
            return true;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Weapon sync is not ready: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Overrides every active base-game Ember Tree node unlock and upgrade level to cost 1 Archipelago Coin,
    /// and synchronizes the save slot's shard balance with <paramref name="totalCoinsReceived"/> minus the
    /// number of upgrades currently invested in the tree.
    /// </summary>
    public static bool ApplyArchipelagoCoins(int totalCoinsReceived, out int availableCoins)
    {
        availableCoins = Math.Max(0, totalCoinsReceived);
        try
        {
            ApplyEmberTreeCoinCosts();

            var game = GameCode.Game;
            if (game?.World is null)
                return false;
            var saveSlot = game.LocalSaveSlot;
            if (saveSlot is null)
                return false;

            // CanInteractWithEmberTree requires the RecoverEmberShards cutscene flag (0x80) to be marked seen,
            // and weapon-mod checks require Marvin's weapon-mod system to be unlocked on a fresh save.
            if (!saveSlot.HasSeenCutscene(eCutscene.RecoverEmberShards))
                saveSlot.MarkCutsceneAsSeen(eCutscene.RecoverEmberShards);
            if (saveSlot.MarvinState != eMarvinState.WeaponModsUnlocked)
                saveSlot.MarvinState = eMarvinState.WeaponModsUnlocked;

            var spent = CalculateEmberTreeCoinsSpent(saveSlot);
            availableCoins = Math.Max(0, totalCoinsReceived - spent);
            saveSlot.shardsSpent = spent;
            saveSlot.numShards = availableCoins;
            return true;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Archipelago Coin sync is not ready: {exception.Message}");
            return false;
        }
    }

    public static int CalculateEmberTreeCoinsSpent(GameCode.SavedPlayerSlot saveSlot) =>
        Math.Max(0, saveSlot.GetNumberOfEmberTreeUpgrades());

    public static void ApplyEmberTreeCoinCosts()
    {
        var defs = GameCode.Defs?.emberTreeFile?.EmberTreeUpgradeDefs;
        if (defs is null)
            return;

        for (var i = 0; i < defs.Count; i++)
        {
            var def = defs[i];
            if (def is null || !def.enabled || def.DLCContent)
                continue;

            if (!VanillaEmberTreeCosts.ContainsKey(def.id))
            {
                var backup = new int[def.UpgradeCosts?.Length ?? 0];
                if (def.UpgradeCosts is not null)
                {
                    for (var level = 0; level < backup.Length; level++)
                        backup[level] = def.UpgradeCosts[level];
                }
                VanillaEmberTreeCosts[def.id] = (def.UnlockCost, backup);
            }

            def.UnlockCost = 1;
            if (def.UpgradeCosts is not null)
            {
                for (var level = 0; level < def.UpgradeCosts.Length; level++)
                {
                    if (def.UpgradeCosts[level] > 0)
                        def.UpgradeCosts[level] = 1;
                }
            }
        }
        emberTreeCostsOverridden = true;
    }

    /// <summary>
    /// The forced relic entries of the scripted first shop, as the game ships them: the entry, and whether it forced an
    /// item, which one, and at what price. Kept so a disconnect can put the scripted shop back exactly.
    /// </summary>
    private static readonly List<(XShopItemDef Entry, bool HasOverrides, int DefId, int Cost)> VanillaFirstShopEntries = new();

    /// <summary>
    /// Turns the scripted relic slots of the first shop (the "FTUE Shop") into ordinary relic slots. The game forces Bone
    /// Plate and Tainted Conduit there whatever the player owns, which would hand out relics the multiworld has not
    /// delivered. With the forced item cleared, the slot is rolled from the normal shop pool, which only offers relics
    /// the player has unlocked. The free Apple Pie and the other food slots are left as they are. Safe to call again.
    /// </summary>
    public static void ApplyFirstShopRandomization()
    {
        try
        {
            var shops = GameCode.Defs?.shopDataFile?.ShopDefs;
            if (shops is null)
                return;

            for (var s = 0; s < shops.Length; s++)
            {
                var shop = shops[s];
                if (shop?.Slots is null)
                    continue;
                for (var slot = 0; slot < shop.Slots.Count; slot++)
                {
                    var items = shop.Slots[slot]?.Items;
                    if (items is null)
                        continue;
                    for (var i = 0; i < items.Length; i++)
                    {
                        var entry = items[i];
                        if (entry is null || !FirstShopPolicy.ShouldClearOverride(entry.HasFTUEOverrides, (int)entry.DropType))
                            continue;
                        if (!VanillaFirstShopEntries.Exists(saved => saved.Entry.Pointer == entry.Pointer))
                            VanillaFirstShopEntries.Add((entry, entry.HasFTUEOverrides, entry.OverrideDefId, entry.OverrideCost));
                        entry.HasFTUEOverrides = false;
                        entry.OverrideDefId = 0;
                        entry.OverrideCost = 0;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Could not randomize the first shop: {exception.Message}");
        }
    }

    public static void RestoreVanillaFirstShop()
    {
        if (VanillaFirstShopEntries.Count == 0)
            return;
        try
        {
            foreach (var (entry, hasOverrides, defId, cost) in VanillaFirstShopEntries)
            {
                entry.HasFTUEOverrides = hasOverrides;
                entry.OverrideDefId = defId;
                entry.OverrideCost = cost;
            }
            VanillaFirstShopEntries.Clear();
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Could not restore the scripted first shop: {exception.Message}");
        }
    }

    public static void RestoreVanillaEmberTreeCosts()
    {
        if (!emberTreeCostsOverridden)
            return;
        try
        {
            var defs = GameCode.Defs?.emberTreeFile?.EmberTreeUpgradeDefs;
            if (defs is null)
                return;

            for (var i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def is null || !VanillaEmberTreeCosts.TryGetValue(def.id, out var vanilla))
                    continue;
                def.UnlockCost = vanilla.UnlockCost;
                if (def.UpgradeCosts is not null)
                {
                    for (var level = 0; level < Math.Min(def.UpgradeCosts.Length, vanilla.UpgradeCosts.Length); level++)
                        def.UpgradeCosts[level] = vanilla.UpgradeCosts[level];
                }
            }
            emberTreeCostsOverridden = false;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Could not restore vanilla Ember Tree costs: {exception.Message}");
        }
    }

    public static bool ApplyHeat(
        int targetHeat,
        bool randomAssortment,
        int randomSeed,
        bool curatedTiers,
        out int gameReportedTotal)
    {
        gameReportedTotal = 0;
        try
        {
            var game = GameCode.Game;
            var world = game?.World;
            if (world is null)
                return false;
            var saveSlot = game!.LocalSaveSlot;
            if (saveSlot is null)
                return false;

            // Unlock the Praxis Tear difficulty system from the very start of a new save.
            if (!saveSlot.HasSeenRegularPraxisDeath)
                saveSlot.HasSeenRegularPraxisDeath = true;
            if (saveSlot.NumVictories < 1)
                saveSlot.NumVictories = 1;
            if (!saveSlot.HasSeenCutscene(eCutscene.PraxisBossFirstDefeat_C2))
                saveSlot.MarkCutsceneAsSeen(eCutscene.PraxisBossFirstDefeat_C2);
            if (!saveSlot.HasSeenCutscene(eCutscene.EsperPostPraxisDefeat_C3))
                saveSlot.MarkCutsceneAsSeen(eCutscene.EsperPostPraxisDefeat_C3);
            if (!saveSlot.HasSeenCutscene(eCutscene.ActivateTrueEnding_C4))
                saveSlot.MarkCutsceneAsSeen(eCutscene.ActivateTrueEnding_C4);
            if (!saveSlot.HasSeenCutscene(eCutscene.PraxisStabilizeTear_C5))
                saveSlot.MarkCutsceneAsSeen(eCutscene.PraxisStabilizeTear_C5);

            var desired = HeatSolver.SolveRanks(targetHeat, randomAssortment, randomSeed, curatedTiers);
            var changed = false;
            ApplyingHeat = true;
            try
            {
                var saveRanks = saveSlot.GameMods?.ModRanks;
                if (saveRanks is not null)
                {
                    var count = Math.Min(desired.Length, saveRanks.Length);
                    for (var modId = 1; modId < count; modId++)
                    {
                        if (saveRanks[modId] != desired[modId])
                        {
                            saveRanks[modId] = desired[modId];
                            changed = true;
                        }
                    }
                }

                var worldRanks = world.GameMods?.ModRanks;
                if (worldRanks is not null)
                {
                    // The rank-changed event is handled by systems that look up live players. With none yet (title
                    // screen, hub load, just connected) that handler throws a NullReferenceException for every rank,
                    // so the ranks are only written directly until a player exists.
                    var dispatch = world.LivePlayers is { Count: > 0 };
                    var count = Math.Min(desired.Length, worldRanks.Length);
                    for (var modId = 1; modId < count; modId++)
                    {
                        if (worldRanks[modId] != desired[modId])
                        {
                            if (dispatch)
                                world.SetGameModRank(modId, desired[modId], true);
                            worldRanks[modId] = desired[modId];
                            changed = true;
                        }
                    }
                }
            }
            finally
            {
                ApplyingHeat = false;
            }

            gameReportedTotal = GameCode.GameUtils.GetTotalGameModifierOrbs(world);
            if (changed)
            {
                ArchipelagoPlugin.LogSource.LogInfo(
                    $"Enforced reverse heat {targetHeat} (game reports {gameReportedTotal} orbs).");
            }
            return true;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Heat enforcement is not ready: {exception.Message}");
            return false;
        }
    }

    public static bool GrantEmber(int amount)
    {
        try
        {
            var game = GameCode.Game;
            if (game?.World is null || game.LocalSaveSlot is null)
                return false;
            GameCode.GameUtils.AddEmberShardsToLocalSaveSlot(game.World, amount);
            return true;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Ember grant is not ready: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Adds Ash (the game's mod energy) to the save. The game only accepts as much as is still needed for unbought weapon
    /// mods and ability upgrades, so <paramref name="amountAdded"/> can be less than requested, or zero. Returns false only
    /// when no save is loaded yet, which is worth retrying; a clamped grant is not.
    /// </summary>
    public static bool GrantAsh(int amount, out int amountAdded)
    {
        amountAdded = 0;
        try
        {
            var game = GameCode.Game;
            var world = game?.World;
            if (world?.GameState is null || game!.LocalSaveSlot is null)
                return false;
            game.LocalSaveSlot.AddModEnergy(Math.Max(0, amount), world.GameState, out amountAdded);
            return true;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Ash grant is not ready: {exception.Message}");
            return false;
        }
    }

    public static bool GrantGold(int amount)
    {
        try
        {
            var game = GameCode.Game;
            var world = game?.World;
            if (world?.LivePlayers is null || world.LivePlayers.Count == 0)
                return false;
            GameCode.GameUtils.AddGoldToLivePlayers(world, amount);
            return true;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Gold grant is not ready: {exception.Message}");
            return false;
        }
    }

    /// <summary>One trapped enemy that is waiting for its assets to finish loading before it can be spawned.</summary>
    private sealed class PendingTrapSpawn
    {
        public eEnemy Type;
        public Vector2 Position;
        public int RoomInstId;
        public UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<
            Il2CppSystem.Collections.Generic.IList<GameObject>>? Handle;
        public float StartedAt;
    }

    private static readonly List<PendingTrapSpawn> PendingTrapSpawns = new();

    /// <summary>
    /// How long a pending other-world enemy may wait for its assets before it is dropped, in seconds. A load that never
    /// completes must not hold a trap forever.
    /// </summary>
    private const float TrapAssetLoadTimeoutSeconds = 8f;

    /// <summary>
    /// Fires the Enemy Ambush Trap. The enemies stand in a small grid around where a live enemy of the room stands, because
    /// nothing in the game collides and a stack would die to a single area attack. With the seed's
    /// <c>trap_enemies_any_world</c> on, each enemy is drawn from every world the seed includes instead of copying the
    /// room's own enemies; an enemy from another world first has its assets loaded, then it is spawned.
    /// </summary>
    public static bool TriggerEnemyAmbushTrap(int count)
    {
        try
        {
            if (!TryGetActiveBattleRoom(out var world, out var room, out var activeEnemies))
                return false;

            var service = ArchipelagoPlugin.Instance.Service;
            var anyWorld = service.TrapEnemiesAnyWorld;
            var spawnCount = Math.Max(1, count);

            // The grid is centred on a live enemy of the room, which is where the old code put the whole stack.
            var centre = activeEnemies[System.Random.Shared.Next(activeEnemies.Count)].Position;
            var offsets = TrapSpawnPolicy.GridOffsets(spawnCount);

            var pool = anyWorld ? BuildAnyWorldEnemyPool(service.DlcActive) : null;
            if (anyWorld && (pool is null || pool.Count == 0))
            {
                ArchipelagoPlugin.LogSource.LogWarning(
                    "Trap enemies from any world: no eligible enemy definitions were found; using the room's own enemies.");
                anyWorld = false;
            }

            var spawned = 0;
            for (var i = 0; i < spawnCount; i++)
            {
                var type = anyWorld
                    ? pool![TrapSpawnPolicy.PickIndex(pool.Count, System.Random.Shared.Next)]
                    : activeEnemies[System.Random.Shared.Next(activeEnemies.Count)].Type;
                var wanted = new Vector2(centre.x + offsets[i].X, centre.y + offsets[i].Y);
                var position = SnapToWalkable(wanted, centre);

                if (anyWorld && !world.IsEntityPrefabLoaded(eEntityType.Enemy, (int)type))
                {
                    if (QueueTrapSpawnAfterLoad(world, type, position, room.RoomInstId))
                        spawned++;
                    continue;
                }

                if (SpawnTrapEnemy(world, type, position, room.RoomInstId))
                    spawned++;
            }

            if (spawned == 0)
                return false;
            if (spawned < spawnCount)
                ArchipelagoPlugin.LogSource.LogInfo($"Enemy ambush trap: placed {spawned} of {spawnCount} enemies.");
            return true;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Enemy ambush trap is not ready: {exception.Message}");
            return false;
        }
    }

    private static bool SpawnTrapEnemy(GameCode.XWorld world, eEnemy type, Vector2 position, int roomInstId)
    {
        try
        {
            world.SpawnEnemy(
                true,
                type,
                position,
                eEnemyRank.Normal,
                -1,
                roomInstId,
                -1,
                -1,
                false,
                GameCode.eEnemyDispatchEventMask.ALL,
                world.GetNextNetId(),
                true);
            return true;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Could not spawn trap enemy {type}: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Moves a grid cell onto ground the enemy can stand on. A cell that is not walkable is pulled to the nearest safe
    /// point; if the game cannot find one, the cell falls back to the centre, which is where an enemy already stands.
    /// </summary>
    private static Vector2 SnapToWalkable(Vector2 wanted, Vector2 fallback)
    {
        try
        {
            var game = GameCode.Game;
            if (game is null)
                return wanted;
            if (game.IsWalkableNode(wanted) && !game.IsInsidePit(wanted))
                return wanted;

            var grid = GameCode.PathGrid;
            if (grid is null)
                return fallback;
            var safe = grid.GetClosestSafePosition(wanted, out var found, false, 16f, false);
            return found ? safe : fallback;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Could not check a trap enemy position: {exception.Message}");
            return fallback;
        }
    }

    /// <summary>
    /// Every enemy the any-world trap may draw: ordinary base-rank enemies of the base worlds, and of the DLC worlds only
    /// when the seed uses the DLC. Built from the game's own enemy definitions each time, so it follows the installed
    /// game version. Returns an empty list if the definitions are not available.
    /// </summary>
    private static List<eEnemy> BuildAnyWorldEnemyPool(bool dlcActive)
    {
        var pool = new List<eEnemy>();
        var defs = GameCode.Defs?.enemyFile?.enemyDefs;
        if (defs is null)
            return pool;

        for (var i = 0; i < defs.Count; i++)
        {
            var def = defs[i];
            if (def is null)
                continue;
            if (!TrapSpawnPolicy.IsEligibleEnemy(
                    def.enabled,
                    def.DLCContent,
                    (int)def.Area,
                    def.IsBoss,
                    def.IsMiniBoss,
                    def.EnemyRank == eEnemyRank.Normal,
                    def.IsStationary,
                    def.IsPlayerClone,
                    def.MaxSpawnCount,
                    dlcActive))
                continue;
            pool.Add(def.EnemyType);
        }

        // The pool comes from the game's own definitions, so the first time it is built for a given DLC setting the log
        // records what it holds. That makes the pool verifiable from LogOutput.log without any extra tooling.
        if (loggedPoolForDlc != dlcActive)
        {
            loggedPoolForDlc = dlcActive;
            ArchipelagoPlugin.LogSource.LogInfo(
                $"Trap enemies from any world (DLC {(dlcActive ? "on" : "off")}): {pool.Count} eligible enemies: " +
                string.Join(", ", pool.Select(type => type.ToString())));
        }
        return pool;
    }

    private static bool? loggedPoolForDlc;

    /// <summary>
    /// Asks the game to load the assets of an enemy that is not part of the current world, using the same loader the game
    /// uses when it enters a world. The enemy is spawned later by <see cref="TickPendingTrapSpawns"/>, once the load has
    /// finished. Returns false only when the load could not even be started.
    /// </summary>
    private static bool QueueTrapSpawnAfterLoad(GameCode.XWorld world, eEnemy type, Vector2 position, int roomInstId)
    {
        try
        {
            var game = GameCode.Game;
            var def = XEnemyDef.Get(type);
            if (game is null || def is null)
                return false;

            // The game builds the list of asset keys for an enemy (its prefab and the projectiles, gibs and props it
            // uses) in this static helper, and loads them with LoadAddressableKeys when it enters a world.
            var keys = new Il2CppSystem.Collections.Generic.List<string>();
            GameCode.GameInst.AddEnemyAddressableAssets(def, keys);
            var handle = game.LoadAddressableKeys(keys, false);
            PendingTrapSpawns.Add(new PendingTrapSpawn
            {
                Type = type,
                Position = position,
                RoomInstId = roomInstId,
                Handle = handle,
                StartedAt = Time.realtimeSinceStartup,
            });
            return true;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Could not load assets for trap enemy {type}: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Spawns the other-world trap enemies whose assets have finished loading, and drops those whose load failed or ran
    /// past the timeout. Called every frame. Does nothing when nothing is waiting.
    /// </summary>
    public static void TickPendingTrapSpawns()
    {
        if (PendingTrapSpawns.Count == 0)
            return;

        try
        {
            var world = GameCode.Game?.World;
            for (var i = PendingTrapSpawns.Count - 1; i >= 0; i--)
            {
                var pending = PendingTrapSpawns[i];
                var timedOut = Time.realtimeSinceStartup - pending.StartedAt > TrapAssetLoadTimeoutSeconds;
                if (world is null || timedOut)
                {
                    PendingTrapSpawns.RemoveAt(i);
                    ArchipelagoPlugin.LogSource.LogWarning(
                        $"Trap enemy {pending.Type} was dropped: its assets did not finish loading in time.");
                    continue;
                }

                // A load that finished with a failure will never produce the prefab, so stop waiting for it now.
                if (pending.Handle is { } handle && handle.IsValid() && handle.IsDone &&
                    handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Failed)
                {
                    PendingTrapSpawns.RemoveAt(i);
                    ArchipelagoPlugin.LogSource.LogWarning($"Trap enemy {pending.Type} was dropped: its assets failed to load.");
                    continue;
                }

                // The prefab being registered is what SpawnEnemy actually needs, so that is the signal to spawn.
                if (!world.IsEntityPrefabLoaded(eEntityType.Enemy, (int)pending.Type))
                    continue;

                PendingTrapSpawns.RemoveAt(i);
                // The player may have left the room while the assets loaded; an enemy spawned into another room would
                // be orphaned, so only spawn while the trap's room is still the current one.
                if (world.currentRoomCMP is null || world.currentRoomCMP.RoomInstId != pending.RoomInstId)
                    continue;
                SpawnTrapEnemy(world, pending.Type, pending.Position, pending.RoomInstId);
            }
        }
        catch (Exception exception)
        {
            PendingTrapSpawns.Clear();
            ArchipelagoPlugin.LogSource.LogWarning($"Pending trap enemies were cleared: {exception.Message}");
        }
    }

    public static void ClearPendingTrapSpawns() => PendingTrapSpawns.Clear();

    public static bool TriggerBossAmbushTrap()
    {
        try
        {
            if (!TryGetActiveBattleRoom(out var world, out var room, out var activeEnemies))
                return false;

            var chosen = activeEnemies[System.Random.Shared.Next(activeEnemies.Count)];
            var miniboss = world.currentArea?.MinibossId ?? eEnemy.None;
            if (miniboss != eEnemy.None)
            {
                world.SpawnEnemy(
                    true,
                    miniboss,
                    chosen.Position,
                    eEnemyRank.Normal,
                    -1,
                    room.RoomInstId,
                    -1,
                    -1,
                    false,
                    GameCode.eEnemyDispatchEventMask.ALL,
                    world.GetNextNetId(),
                    true,
                    0,
                    0f,
                    0f,
                    0f,
                    0,
                    0f,
                    ePropType.None,
                    eEnemy.None,
                    true);
            }
            else
            {
                world.SpawnEnemy(
                    true,
                    chosen.Type,
                    chosen.Position,
                    eEnemyRank.Champion,
                    -1,
                    room.RoomInstId,
                    -1,
                    -1,
                    false,
                    GameCode.eEnemyDispatchEventMask.ALL,
                    world.GetNextNetId(),
                    true,
                    0,
                    1.5f,
                    0f,
                    0f,
                    0,
                    0f,
                    ePropType.None,
                    eEnemy.None,
                    true);
            }
            return true;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Boss ambush trap is not ready: {exception.Message}");
            return false;
        }
    }

    public static bool TriggerPraxisArenaTrap()
    {
        try
        {
            if (!TryGetActiveBattleRoom(out var world, out _, out _))
                return false;

            var praxisData = world.GameState?.PraxisEffectData;
            if (praxisData is null || praxisData.RoomActive)
                return false;

            var effect = PraxisTrapEffects[System.Random.Shared.Next(PraxisTrapEffects.Length)];
            praxisData.NextEffect = effect;
            praxisData.Seed = (ulong)System.Random.Shared.Next(1, int.MaxValue);
            praxisData.RoomActive = true;
            praxisData.SpawnTime = 0.25f;
            PraxisDiagnostics.NoteTrapFired(world, effect);
            return true;
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Praxis arena trap is not ready: {exception.Message}");
            return false;
        }
    }

    private static bool TryGetActiveBattleRoom(
        out GameCode.XWorld world,
        out GameCode.XRoomCMP room,
        out List<(eEnemy Type, Vector2 Position)> activeEnemies)
    {
        world = null!;
        room = null!;
        activeEnemies = new List<(eEnemy, Vector2)>();

        var game = GameCode.Game;
        if (game?.World is null)
            return false;
        world = game.World;
        var area = (int)(world.currentArea?.areaName ?? 0);
        if (area < RoomProgressTracker.FirstWorld || area > RoomProgressTracker.LastWorld)
            return false;

        if (world.currentRoomCMP is null ||
            world.currentRoomCMP.roomType != eRoomType.Battle ||
            world.currentRoomCMP.roomState != GameCode.eRoomState.NewBattleRoomSpawning ||
            world.LivePlayers is null || world.LivePlayers.Count == 0 ||
            world.LiveEnemies is null || world.LiveEnemies.Count == 0)
            return false;

        room = world.currentRoomCMP;
        for (var i = 0; i < world.LiveEnemies.Count; i++)
        {
            var enemy = world.LiveEnemies[i];
            if (enemy is null || GameCode.EnemyUtils.IsBossOrMiniboss(enemy))
                continue;
            var p = GameCode.EnemyUtils.GetParams(enemy);
            if (p.EnemyType is eEnemy.None or eEnemy.Hatchery or eEnemy.MysticHatchery)
                continue;
            activeEnemies.Add((p.EnemyType, p.Position));
        }

        return activeEnemies.Count > 0;
    }

    public static int KillLocalPlayers()
    {
        var killed = 0;
        try
        {
            var world = GameCode.Game?.World;
            if (world?.LivePlayers is null)
                return 0;

            for (var index = world.LivePlayers.Count - 1; index >= 0; index--)
            {
                var player = world.LivePlayers[index];
                if (player is null)
                    continue;
                var parameters = GameCode.PlayerUtils.GetParams(player);
                if (!parameters.IsLocalPlayer || parameters.HealthCMP is null || parameters.DmgQueueCMP?.Damages is null ||
                    parameters.HealthCMP.CurrentHP <= 0f)
                    continue;

                var damage = new DamageParams(parameters.HealthCMP.CurrentHP + 10000f)
                {
                    DamageAmount = parameters.HealthCMP.CurrentHP + 10000f,
                    Flags = DMG.Forced | DMG.OneHitKill | DMG.IgnoreFX | DMG.IgnoreHitFX | DMG.IgnoreSFX,
                    AttackerEntityId = -1,
                    AttackerEntityType = eEntityType.Enemy,
                    DamagePos = parameters.Position,
                };
                damage.UpdateVersion();
                parameters.DmgQueueCMP.Damages.Add(damage);
                killed++;
            }
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogError($"Could not apply DeathLink damage: {exception}");
        }
        return killed;
    }
}
