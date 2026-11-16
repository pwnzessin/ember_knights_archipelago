using System.Collections.Concurrent;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using BepInEx;
using UnityEngine;

namespace EmberKnights.Archipelago;

public sealed class ConnectionService
{
    private readonly ArchipelagoPlugin plugin;
    private readonly SessionStateStore stateStore;
    private readonly ConcurrentQueue<Action> mainThreadActions = new();
    private readonly HashSet<long> pendingChecks = new();
    private readonly HashSet<int> ownedAbilities = new();
    private readonly HashSet<int> startingWeapons = new() { 1 };
    /// <summary>Standard skills this seed starts with, or null when the seed does not gate standard skills.</summary>
    private HashSet<int>? startingStandardSkills;
    /// <summary>Relics this seed starts with, or null when the seed does not gate the 43 default relics.</summary>
    private HashSet<int>? startingRelics;
    private readonly HashSet<int> ownedWeapons = new() { 1 };
    private readonly HashSet<int> ownedRelics = new();
    private readonly List<int> receivedRelicOrder = new();
    private readonly Dictionary<long, ScoutedEntry> scoutedItems = new();
    private readonly string connectionUuid = Guid.NewGuid().ToString();

    private ArchipelagoSession? session;
    private DeathLinkService? deathLinkService;
    private SessionState? state;
    private bool connecting;
    private bool loggedIn;
    private bool itemsDirty;
    private float nextItemSyncAt;
    private float nextProgressionScanAt;
    private string? lastScanError;
    private readonly RoomProgressTracker roomProgress = new();
    private int heatRunSeed;
    private bool wasInRunWorld;
    private bool rerollHeatEachRun = true;
    private DateTime lastDeathSentAt = DateTime.MinValue;
    private DateTime suppressDeathUntil = DateTime.MinValue;
    private bool disposed;

    public ConnectionService(ArchipelagoPlugin plugin)
    {
        this.plugin = plugin;
        stateStore = new SessionStateStore(Path.Combine(Paths.PluginPath, "EmberKnights.Archipelago"));
    }

    private readonly List<string> recentMessages = new(5)
    {
        "Configure the connection in BepInEx/config/com.emberknights.archipelago.cfg",
    };
    private string lastMessage = "Not set up yet: run Install.bat, or set slot_name in BepInEx/config/com.emberknights.archipelago.cfg";

    public bool IsActive { get; private set; }

    /// <summary>
    /// True when the connected seed was generated with the Wrath of the Architect DLC option on. Every DLC behavior in
    /// this class is behind this flag, and it is false for any seed from before the option existed, so those seeds run
    /// exactly the code they always did.
    /// </summary>
    public bool DlcActive { get; private set; }
    public bool IsConnected => loggedIn && session is not null;
    public string StatusText { get; private set; } = "Disconnected";
    public IReadOnlyList<string> RecentMessages => recentMessages;

    /// <summary>Receives every line that reaches the log, for the HUD window to keep its own longer history.</summary>
    public Action<string>? LogSink { get; set; }

    public string LastMessage
    {
        get => lastMessage;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            lastMessage = value;
            LogSink?.Invoke(value);
            if (recentMessages.Count > 0 && string.Equals(recentMessages[^1], value, StringComparison.Ordinal))
                return;
            if (recentMessages.Count >= 5)
                recentMessages.RemoveAt(0);
            recentMessages.Add(value);
        }
    }
    public string Goal { get; private set; } = "praxis";
    public bool DeathLinkEnabled { get; private set; }
    public int StartHeat { get; private set; }
    public int MinimumHeat { get; private set; }
    public int HeatReductionPerItem { get; private set; } = 1;
    public int HeatReductionsReceived { get; private set; }
    public bool RandomHeatAssortment { get; private set; } = true;
    public bool CuratedHeatDifficulty { get; private set; }
    /// <summary>Ash given by each Ash item; null means the seed predates the option and the catalog amount applies.</summary>
    public int? AshFillerAmount { get; private set; }
    public int ActiveHeat => HeatSolver.EffectiveHeat(StartHeat, MinimumHeat, HeatReductionPerItem, HeatReductionsReceived);
    public int StartingArchipelagoCoins { get; private set; } = 1;
    public int ArchipelagoCoinsReceived { get; private set; }
    public int AvailableArchipelagoCoins { get; private set; }
    public int ReceivedItemCount { get; private set; }
    public int PendingCheckCount => pendingChecks.Count;

    public void Connect()
    {
        if (disposed || connecting || IsConnected)
            return;
        if (ConnectionSetup.IsSlotMissing(plugin.SlotName.Value))
        {
            LastMessage = ConnectionSetup.MissingSlotMessage(plugin.Config.ConfigFilePath);
            return;
        }

        DisconnectSessionOnly();
        connecting = true;
        StatusText = $"Connecting to {plugin.Host.Value}:{plugin.Port.Value}...";

        ArchipelagoSession newSession;
        try
        {
            newSession = ArchipelagoSessionFactory.CreateSession(plugin.Host.Value.Trim(), plugin.Port.Value);
            session = newSession;
            RegisterSessionEvents(newSession);
        }
        catch (Exception exception)
        {
            connecting = false;
            StatusText = "Connection setup failed";
            LastMessage = exception.Message;
            ArchipelagoPlugin.LogSource.LogError(exception);
            return;
        }

        Task.Run(() =>
        {
            LoginResult result;
            try
            {
                result = newSession.TryConnectAndLogin(
                    "Ember Knights",
                    plugin.SlotName.Value.Trim(),
                    ItemsHandlingFlags.AllItems,
                    tags: Array.Empty<string>(),
                    uuid: connectionUuid,
                    password: string.IsNullOrEmpty(plugin.Password.Value) ? null : plugin.Password.Value,
                    requestSlotData: true);
            }
            catch (Exception exception)
            {
                mainThreadActions.Enqueue(() => ConnectionFailed(newSession, exception.Message));
                return;
            }

            mainThreadActions.Enqueue(() => CompleteConnection(newSession, result));
        });
    }

    public void Disconnect()
    {
        SaveState();
        DisconnectSessionOnly();
        IsActive = false;
        connecting = false;
        state = null;
        pendingChecks.Clear();
        ownedAbilities.Clear();
        startingWeapons.Clear();
        startingWeapons.Add(1);
        startingStandardSkills = null;
        startingRelics = null;
        DlcActive = false;
        ownedWeapons.Clear();
        ownedWeapons.Add(1);
        ownedRelics.Clear();
        receivedRelicOrder.Clear();
        scoutedItems.Clear();
        roomProgress.Reset();
        wasInRunWorld = false;
        StartHeat = 0;
        MinimumHeat = 0;
        HeatReductionPerItem = 1;
        HeatReductionsReceived = 0;
        StartingArchipelagoCoins = 1;
        ArchipelagoCoinsReceived = 0;
        AvailableArchipelagoCoins = 0;
        ReceivedItemCount = 0;
        DeathLinkEnabled = false;
        GameActions.RestoreVanillaEmberTreeCosts();
        GameActions.RestoreVanillaFirstShop();
        CoinIconPatcher.Restore();
        StatusText = "Disconnected";
        LastMessage = "AP mode disabled; vanilla unlock behavior restored.";
    }

    public void Dispose()
    {
        if (disposed)
            return;
        Disconnect();
        disposed = true;
    }

    public void PumpMainThread()
    {
        while (mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                ArchipelagoPlugin.LogSource.LogError($"Archipelago main-thread action failed: {exception}");
            }
        }

        if (!IsConnected || !IsActive || state is null)
            return;

        ScanProgressionUnlocks();
        FlushPendingChecks();
        TrySendGoal();
        if (itemsDirty && Time.realtimeSinceStartup >= nextItemSyncAt)
        {
            itemsDirty = false;
            nextItemSyncAt = Time.realtimeSinceStartup + 0.5f;
            SyncItems();
        }
    }

    public bool OwnsAbility(int abilityId) => ownedAbilities.Contains(abilityId);

    public bool ResolveAbilityUnlocked(int abilityId, bool vanillaResult)
    {
        if (!IsActive)
            return vanillaResult;
        if (GameCatalog.Data.SkillForAbility(abilityId) is not null)
            return OwnsAbility(abilityId);
        // A DLC skill is controlled by the multiworld only when the seed uses the DLC. Otherwise the game decides.
        if (DlcActive && GameCatalog.Data.DlcSkillForAbility(abilityId) is not null)
            return OwnsAbility(abilityId);
        // A standard skill is free only when the seed does not gate them. Otherwise it has to be a starting skill
        // or one the multiworld has delivered.
        if (GameCatalog.Data.StandardSkillForAbility(abilityId) is not null)
            return StandardSkillPolicy.IsUnlocked(abilityId, startingStandardSkills, ownedAbilities);
        return vanillaResult;
    }

    public bool ShouldSuppressAbilityGrant(int abilityId) =>
        IsActive && (GameCatalog.Data.SkillForAbility(abilityId) is not null ||
                     (DlcActive && GameCatalog.Data.DlcSkillForAbility(abilityId) is not null) ||
                     (startingStandardSkills is not null && GameCatalog.Data.StandardSkillForAbility(abilityId) is not null));

    public bool OwnsWeapon(int weaponId) => weaponId == 1 || ownedWeapons.Contains(weaponId);

    public bool ResolveWeaponUnlocked(int weaponId, bool vanillaResult)
    {
        if (!IsActive)
            return vanillaResult;
        if (weaponId == 1)
            return true;
        if (!IsManagedWeapon(weaponId))
            return vanillaResult;
        return OwnsWeapon(weaponId);
    }

    public bool ShouldSuppressWeaponGrant(int weaponId) =>
        IsActive && !GameActions.ApplyingWeapons && IsManagedWeapon(weaponId);

    /// <summary>
    /// A base weapon is managed whenever the plugin is active. A DLC weapon (Hyper Gloves, Ember Buster) is managed only
    /// when the seed uses the DLC, so a seed without it never adds those weapons to or removes them from the save.
    /// </summary>
    public bool IsManagedWeapon(int weaponId) => DlcPolicy.IsManagedWeapon(
        GameCatalog.Data.IsRandomizedWeapon(weaponId), GameCatalog.Data.IsDlcWeapon(weaponId), DlcActive);

    /// <summary>The weapons the plugin must reconcile with the save: the base ones, plus the DLC ones with the DLC on.</summary>
    public IEnumerable<int> ManagedWeaponIds() =>
        GameCatalog.Data.Weapons.Select(weapon => weapon.WeaponId)
            .Concat(DlcActive ? GameCatalog.Data.Dlc.Weapons.Select(weapon => weapon.WeaponId) : Enumerable.Empty<int>());

    public bool OwnsRelic(int relicDefId) =>
        StartingRelicPolicy.IsUnlocked(relicDefId, startingRelics, ownedRelics);

    /// <summary>
    /// The 53 base orb relics are always controlled by the multiworld, and so are the 43 default relics when the seed
    /// gates them (starting_relic_ids is in the slot data). Every other relic stays vanilla.
    /// </summary>
    public bool ResolveRelicUnlocked(int relicDefId, bool vanillaResult)
    {
        if (!IsActive || !IsManagedRelic(relicDefId))
            return vanillaResult;
        return OwnsRelic(relicDefId);
    }

    /// <summary>
    /// Blocks the base game from unlocking an orb relic (<c>AddRelicUnlockId</c>, <c>AddNewRelic</c>); the multiworld
    /// item is the only source of those relics.
    /// </summary>
    public bool ShouldSuppressRelicGrant(int relicDefId) =>
        IsActive && IsManagedRelic(relicDefId);

    /// <summary>
    /// A base orb relic is managed whenever the plugin is active; a DLC orb relic only with the DLC on; a default relic
    /// only when the seed gates them.
    /// </summary>
    public bool IsManagedRelic(int relicDefId) =>
        DlcPolicy.IsManagedRelic(
            GameCatalog.Data.IsOrbRelic(relicDefId), GameCatalog.Data.IsDlcOrbRelic(relicDefId), DlcActive) ||
        StartingRelicPolicy.IsGated(GameCatalog.Data.IsDefaultRelic(relicDefId), startingRelics);

    /// <summary>
    /// Picks what Esper's reveal shows for the orbs being handed in (see <see cref="RelicOrbPolicy.ChooseReveals"/>)
    /// and records them as shown. Returns one entry per orb, or <c>null</c> when the game should play its own reveal.
    /// </summary>
    internal OrbReveal[]? TakeOrbReveals(int orbsHandedIn)
    {
        if (!IsActive || state is null || orbsHandedIn <= 0)
            return null;
        var chosen = RelicOrbPolicy.ChooseReveals(state.RelicsRevealed, orbsHandedIn, state.OrbsCollected, ScoutedOrbAt);
        // Orbs are identified in the order they were picked up, so every handed-in orb advances the counter,
        // including ones whose item could not be shown.
        state.RelicsRevealed = Math.Min(state.OrbsCollected, state.RelicsRevealed + orbsHandedIn);
        SaveState();
        return chosen;
    }

    /// <summary>
    /// Records that the player picked up a relic orb and reports the matching "Relic Orb N" check straight away.
    /// The orb stays pending in the save so Esper can still identify it later. Orbs are numbered by how many have
    /// been collected, not by the relic inside, because the game's own choice of relic depends on unlock state
    /// that the multiworld now controls.
    /// </summary>
    public void CollectRelicOrb()
    {
        if (!IsActive || state is null)
            return;

        var order = state.OrbsCollected + 1;
        if (GameCatalog.Data.LocationForRelicOrb(order) is not { } locationId)
        {
            ArchipelagoPlugin.LogSource.LogInfo($"Relic orb #{order} is beyond the {GameCatalog.Data.RelicOrbs.Count} randomized orbs; ignored.");
            return;
        }

        state.OrbsCollected = order;
        SaveState();
        ArchipelagoPlugin.LogSource.LogInfo($"Relic orb #{order} collected.");
        CheckLocation(locationId);
    }

    /// <summary>
    /// Records that the player picked up a DLC relic orb and reports the matching "Relic Orb 54 to 63" check straight
    /// away. DLC orbs are counted on their own, because the game hands out DLC orb relics from a separate list.
    /// Does nothing unless the seed uses the DLC.
    /// </summary>
    public void CollectDlcRelicOrb()
    {
        if (!IsActive || !DlcActive || state is null)
            return;

        var order = state.DlcOrbsCollected + 1;
        if (GameCatalog.Data.LocationForDlcRelicOrb(order) is not { } locationId)
        {
            ArchipelagoPlugin.LogSource.LogInfo($"DLC relic orb #{order} is beyond the {GameCatalog.Data.Dlc.RelicOrbs.Count} randomized DLC orbs; ignored.");
            return;
        }

        state.DlcOrbsCollected = order;
        SaveState();
        ArchipelagoPlugin.LogSource.LogInfo($"DLC relic orb #{order} collected.");
        CheckLocation(locationId);
    }

    /// <summary>What the multiworld holds at "Relic Orb <paramref name="order"/>", or null if it was not scouted.</summary>
    internal ScoutedOrb? ScoutedOrbAt(int order)
    {
        if (GameCatalog.Data.LocationForRelicOrb(order) is not { } locationId ||
            !scoutedItems.TryGetValue(locationId, out var item))
            return null;
        int? relicDefId = null;
        if (item.IsOwnPlayer)
        {
            if (GameCatalog.Data.RelicForItem(item.ItemId) is { } relic)
                relicDefId = relic.DefId;
            else if (GameCatalog.Data.DefaultRelicForItem(item.ItemId) is { } defaultRelic)
                relicDefId = defaultRelic.DefId;
        }
        return new ScoutedOrb
        {
            Order = order,
            ItemName = item.ItemName,
            PlayerName = item.PlayerName,
            IsOwnPlayer = item.IsOwnPlayer,
            RelicDefId = relicDefId,
        };
    }

    /// <summary>What the multiworld holds at an Ember Tree node's check, or null if not scouted.</summary>
    internal ScoutedItem? ScoutedItemForEmberTreeNode(int defId) =>
        GameCatalog.Data.LocationForEmberTreeNode(defId) is { } id ? ScoutedItemAt(id) : null;

    /// <summary>What the multiworld holds at a weapon mod's check, or null if not scouted.</summary>
    internal ScoutedItem? ScoutedItemForWeaponMod(int defId) =>
        GameCatalog.Data.LocationForWeaponMod(defId) is { } id ? ScoutedItemAt(id) : null;

    private ScoutedItem? ScoutedItemAt(long locationId) =>
        scoutedItems.TryGetValue(locationId, out var item)
            ? new ScoutedItem { ItemName = item.ItemName, PlayerName = item.PlayerName, IsOwnPlayer = item.IsOwnPlayer }
            : null;

    /// <summary>True once the location has been reported to the server (or the server already had it).</summary>
    public bool IsLocationSent(long locationId)
    {
        if (pendingChecks.Contains(locationId))
            return false;
        try
        {
            return IsConnected && session is not null && session.Locations.AllLocationsChecked.Contains(locationId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Shows a line in the overlay and the log, used by Esper's reveal for items that are not relics.</summary>
    public void ShowEsperMessage(string text)
    {
        LastMessage = text;
        ArchipelagoPlugin.LogSource.LogInfo(text);
    }

    private sealed record ScoutedEntry(long ItemId, string ItemName, string PlayerName, bool IsOwnPlayer);

    /// <summary>
    /// Looks up the item behind every relic orb, Ember Tree and weapon mod location once per connection. This is a
    /// silent scout: it never creates hints, so it does not reveal anything to the rest of the room.
    /// </summary>
    private void ScoutUpgradeLocations(ArchipelagoSession currentSession)
    {
        var catalog = GameCatalog.Data;
        var locationIds = catalog.RelicOrbs.Select(orb => catalog.RelicOrbLocationId(orb.Order - 1))
            .Concat(catalog.EmberTree.Select((_, index) => catalog.EmberTreeLocationId(index)))
            .Concat(catalog.WeaponMods.Select((_, index) => catalog.WeaponModLocationId(index)))
            .ToArray();
        if (locationIds.Length == 0)
            return;

        Task.Run(async () =>
        {
            try
            {
                var scouted = await currentSession.Locations.ScoutLocationsAsync(HintCreationPolicy.None, locationIds);
                var found = new Dictionary<long, ScoutedEntry>();
                foreach (var locationId in locationIds)
                {
                    if (!scouted.TryGetValue(locationId, out var info))
                        continue;
                    var playerName = string.IsNullOrEmpty(info.Player.Alias)
                        ? (info.Player.Name ?? $"player {info.Player.Slot}")
                        : info.Player.Alias;
                    found[locationId] = new ScoutedEntry(
                        info.ItemId,
                        info.ItemName ?? $"item {info.ItemId}",
                        playerName,
                        info.Player.Slot == currentSession.ConnectionInfo.Slot);
                }
                mainThreadActions.Enqueue(() =>
                {
                    if (!ReferenceEquals(session, currentSession))
                        return;
                    scoutedItems.Clear();
                    foreach (var pair in found)
                        scoutedItems[pair.Key] = pair.Value;
                    ArchipelagoPlugin.LogSource.LogInfo(
                        $"Scouted {found.Count} of {locationIds.Length} upgrade/orb locations for tooltips and Esper's reveal.");
                });
            }
            catch (Exception exception)
            {
                ArchipelagoPlugin.LogSource.LogWarning($"Could not scout upgrade locations: {exception.Message}");
            }
        });
    }
    public void CheckSkillTablet(int abilityId)
    {
        if (DlcActive && GameCatalog.Data.DlcSkillForAbility(abilityId) is { } dlcSkill)
        {
            CheckLocation(GameCatalog.Data.LocationForDlcSkill(dlcSkill));
            return;
        }
        var skill = GameCatalog.Data.SkillForAbility(abilityId);
        if (skill is null)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Unmapped skill tablet ability ID {abilityId}; AP location was not sent.");
            return;
        }
        CheckLocation(GameCatalog.Data.LocationForSkill(skill));
    }

    public void CheckMiniboss(int enemyId)
    {
        var encounter = GameCatalog.Data.MinibossForEnemy(enemyId);
        if (encounter is not null)
            CheckLocation(GameCatalog.Data.LocationForMiniboss(encounter));
        if (DlcActive && GameCatalog.Data.DlcMinibossForEnemy(enemyId) is { } dlcEncounter)
            CheckLocation(GameCatalog.Data.LocationForDlcMiniboss(dlcEncounter));
    }

    public void CheckBoss(int enemyId)
    {
        var encounter = GameCatalog.Data.BossForEnemy(enemyId);
        if (encounter is not null)
            CheckLocation(GameCatalog.Data.LocationForBoss(encounter));
        if (DlcActive && GameCatalog.Data.DlcBossForEnemy(enemyId) is { } dlcEncounter)
            CheckLocation(GameCatalog.Data.LocationForDlcBoss(dlcEncounter));
    }

    public void ReportFinalBoss(string goal)
    {
        var encounter = GameCatalog.Data.FinalBoss(goal);
        if (encounter is null)
            return;
        CheckLocation(GameCatalog.Data.LocationForBoss(encounter));
        if (Goal == goal && state is not null)
        {
            state.GoalPending = true;
            SaveState();
            TrySendGoal();
        }
    }

    public void CheckEmberTreeNode(int defId)
    {
        if (GameCatalog.Data.LocationForEmberTreeNode(defId) is { } locationId)
            CheckLocation(locationId);
    }

    public void CheckWeaponMod(int defId)
    {
        if (GameCatalog.Data.LocationForWeaponMod(defId) is { } locationId)
            CheckLocation(locationId);
    }

    public void CheckDlcWeaponMod(int defId)
    {
        if (DlcActive && GameCatalog.Data.LocationForDlcWeaponMod(defId) is { } locationId)
            CheckLocation(locationId);
    }

    /// <summary>
    /// Records entering a room and reports every world-progress milestone reached so far in this run.
    /// </summary>
    public void NotifyRoomEntered(int areaName, int roomInstanceId, int roomType)
    {
        if (!IsActive)
            return;

        if (DlcPolicy.IsRunWorld(areaName, DlcActive))
        {
            wasInRunWorld = true;
        }
        else if (areaName == RoomProgressTracker.NexusArea && wasInRunWorld)
        {
            wasInRunWorld = false;
            // In "once" mode the assortment is rolled a single time for the whole seed, so the seed never moves.
            if (rerollHeatEachRun)
            {
                unchecked
                {
                    heatRunSeed += (int)0x9E3779B9;
                }
            }
        }

        var depth = roomProgress.Enter(areaName, roomInstanceId, roomType, DlcActive);
        EnforceHeat();
        EnforceArchipelagoCoins();
        EnforceWeapons();
        ArchipelagoPlugin.LogSource.LogInfo(
            $"Room entered: area {areaName}, room instance {roomInstanceId}, type {roomType} -> depth {depth}.");
        foreach (var milestone in GameCatalog.Data.MilestonesFor(areaName, depth))
            CheckLocation(GameCatalog.Data.LocationForRoomMilestone(milestone));
        if (DlcActive)
        {
            foreach (var milestone in GameCatalog.Data.DlcMilestonesFor(areaName, depth))
                CheckLocation(GameCatalog.Data.LocationForDlcRoomMilestone(milestone));
        }
    }

    /// <summary>
    /// True while the Praxis Tear difficulty menu must not accept input: the mod chooses the modifiers, and letting
    /// the menu change a rank only for it to be reset leaves the slots showing values that no longer match.
    /// </summary>
    public bool ShouldBlockHeatMenuInput => IsActive && !GameActions.ApplyingHeat;

    /// <summary>Tells the player why the difficulty menu ignored their input. Repeats collapse into one HUD line.</summary>
    public void NoteHeatMenuLocked() =>
        LastMessage = $"Heat is set by Archipelago: {ActiveHeat} (Heat Reduction items lower it).";

    public void EnforceHeat()
    {
        if (!IsActive)
            return;
        GameActions.ApplyHeat(ActiveHeat, RandomHeatAssortment, heatRunSeed, CuratedHeatDifficulty, out _);
    }

    public void EnforceArchipelagoCoins()
    {
        if (!IsActive)
            return;
        if (GameActions.ApplyArchipelagoCoins(ArchipelagoCoinsReceived, out var available))
            AvailableArchipelagoCoins = available;
    }

    public void EnforceWeapons()
    {
        if (!IsActive)
            return;
        GameActions.ApplyWeapons(ownedWeapons, includeDlcWeapons: DlcActive);
    }

    /// <summary>
    /// Reports every Ember Tree node and weapon mod the local save already has unlocked.
    /// Scanning the save is independent of which UI or cutscene unlocked the entry, and checks are
    /// idempotent, so unlocks earned before connecting are credited as well.
    /// </summary>
    private void ScanProgressionUnlocks()
    {
        if (Time.realtimeSinceStartup < nextProgressionScanAt)
            return;
        nextProgressionScanAt = Time.realtimeSinceStartup + 1f;

        try
        {
            // The LocalSaveSlot getter throws while no save is loaded (title screen, loading), and the
            // interop layer logs an error for every such call. A running world implies a loaded save,
            // so only read the slot once one exists.
            if (GameCode.Game?.World is null)
                return;
            var saveSlot = GameCode.Game.LocalSaveSlot;
            if (saveSlot is null)
                return;

            EnforceHeat();
            EnforceArchipelagoCoins();
            EnforceWeapons();
            FlushPendingFillers();
            if (ArchipelagoPlugin.Instance.CoinIconEnabled.Value)
                CoinIconPatcher.TryApply();

            var unlockedNodes = new List<int>();
            foreach (var node in GameCatalog.Data.EmberTree)
            {
                var data = saveSlot.GetEmberTreeUpgradeData(node.DefId);
                if (data is not null && data.Unlocked)
                    unlockedNodes.Add(node.DefId);
            }
            var unlockedMods = new List<int>();
            foreach (var mod in GameCatalog.Data.WeaponMods)
            {
                var data = saveSlot.GetWeaponMod(mod.DefId, 0);
                if (data is not null && data.Unlocked)
                    unlockedMods.Add(mod.DefId);
            }

            var unlockedDlcMods = new List<int>();
            if (DlcActive)
            {
                foreach (var mod in GameCatalog.Data.Dlc.WeaponMods)
                {
                    var data = saveSlot.GetWeaponMod(mod.DefId, 0);
                    if (data is not null && data.Unlocked)
                        unlockedDlcMods.Add(mod.DefId);
                }
            }

            // The first scan of a brand-new seed records what is already unlocked, such as the five Ember Tree nodes
            // the game gives a fresh save, so those never become checks. It runs once per seed.
            if (state is not null && !state.BaselineTaken && !state.BaselineSkipped)
            {
                state.BaselineEmberTree = UnlockBaseline.TakeBaseline(unlockedNodes).OrderBy(id => id).ToList();
                state.BaselineWeaponMods = UnlockBaseline.TakeBaseline(unlockedMods).OrderBy(id => id).ToList();
                state.BaselineTaken = true;
                SaveState();
                ArchipelagoPlugin.LogSource.LogInfo(
                    $"New seed: {state.BaselineEmberTree.Count} Ember Tree node(s) and {state.BaselineWeaponMods.Count} " +
                    "weapon mod(s) were already unlocked and are not checks.");
            }

            // A DLC weapon mod the player already owns when a DLC seed first connects is not a check, just like the
            // base mods. The baseline for them is recorded the first time a seed is seen with the DLC on, even if the
            // base baseline was taken earlier, so turning the DLC on for a seed never reports old mods.
            if (state is not null && DlcActive && !state.BaselineSkipped && state.BaselineDlcWeaponMods.Count == 0 &&
                !state.DlcBaselineTaken)
            {
                state.BaselineDlcWeaponMods = UnlockBaseline.TakeBaseline(unlockedDlcMods).OrderBy(id => id).ToList();
                state.DlcBaselineTaken = true;
                SaveState();
                ArchipelagoPlugin.LogSource.LogInfo(
                    $"New DLC seed: {state.BaselineDlcWeaponMods.Count} DLC weapon mod(s) were already unlocked and are not checks.");
            }

            var baselineNodes = new HashSet<int>(state?.BaselineEmberTree ?? new List<int>());
            var baselineMods = new HashSet<int>(state?.BaselineWeaponMods ?? new List<int>());
            foreach (var id in unlockedNodes)
            {
                if (UnlockBaseline.ShouldReport(id, unlockedNow: true, baselineNodes))
                    CheckEmberTreeNode(id);
            }
            foreach (var id in unlockedMods)
            {
                if (UnlockBaseline.ShouldReport(id, unlockedNow: true, baselineMods))
                    CheckWeaponMod(id);
            }
            var baselineDlcMods = new HashSet<int>(state?.BaselineDlcWeaponMods ?? new List<int>());
            foreach (var id in unlockedDlcMods)
            {
                if (UnlockBaseline.ShouldReport(id, unlockedNow: true, baselineDlcMods))
                    CheckDlcWeaponMod(id);
            }
        }
        catch (Exception exception)
        {
            if (!string.Equals(lastScanError, exception.Message, StringComparison.Ordinal))
            {
                lastScanError = exception.Message;
                ArchipelagoPlugin.LogSource.LogWarning($"Could not scan save unlocks: {exception.Message}");
            }
        }
    }

    public void NotifyLocalDeath()
    {
        var now = DateTime.UtcNow;
        if (!IsConnected || !IsActive || !DeathLinkEnabled || deathLinkService is null ||
            now < suppressDeathUntil || now - lastDeathSentAt < TimeSpan.FromSeconds(3))
            return;

        try
        {
            var slotName = plugin.SlotName.Value.Trim();
            deathLinkService.SendDeathLink(new DeathLink(slotName, $"{slotName} died in Ember Knights."));
            lastDeathSentAt = now;
            LastMessage = "DeathLink sent.";
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Could not send DeathLink: {exception.Message}");
        }
    }

    private void RegisterSessionEvents(ArchipelagoSession currentSession)
    {
        currentSession.Items.ItemReceived += _ => mainThreadActions.Enqueue(() =>
        {
            if (ReferenceEquals(session, currentSession))
                itemsDirty = true;
        });
        currentSession.MessageLog.OnMessageReceived += message => mainThreadActions.Enqueue(() =>
        {
            if (ReferenceEquals(session, currentSession))
                LastMessage = message.ToString();
        });
        currentSession.Socket.ErrorReceived += (exception, message) => mainThreadActions.Enqueue(() =>
        {
            if (ReferenceEquals(session, currentSession))
                ArchipelagoPlugin.LogSource.LogWarning($"Archipelago socket error: {message} ({exception.Message})");
        });
        currentSession.Socket.SocketClosed += reason => mainThreadActions.Enqueue(() => SocketClosed(currentSession, reason));

        var deathLinks = currentSession.CreateDeathLinkService();
        deathLinkService = deathLinks;
        deathLinks.OnDeathLinkReceived += deathLink => mainThreadActions.Enqueue(() =>
        {
            if (ReferenceEquals(session, currentSession))
                ReceiveDeathLink(deathLink);
        });
    }

    private void CompleteConnection(ArchipelagoSession currentSession, LoginResult result)
    {
        if (!ReferenceEquals(session, currentSession))
            return;
        connecting = false;

        if (result is LoginFailure failure)
        {
            ConnectionFailed(currentSession, string.Join("; ", failure.Errors));
            return;
        }
        if (result is not LoginSuccessful success)
        {
            ConnectionFailed(currentSession, "The server returned an unknown login result.");
            return;
        }

        try
        {
            var slotName = plugin.SlotName.Value.Trim();
            var seed = currentSession.RoomState.Seed ?? "";
            if (string.IsNullOrEmpty(seed))
                throw new InvalidDataException("The server did not provide a seed name.");

            var loadedState = stateStore.Load(slotName);
            if (!string.Equals(loadedState.Seed, seed, StringComparison.Ordinal))
                loadedState = new SessionState { SlotName = slotName, Seed = seed };
            loadedState.SlotName = slotName;
            loadedState.Seed = seed;
            // A seed that ran before the baseline existed has already reported what it was going to. Giving it a
            // baseline now could hide a node the player bought but has not been credited for, so it is left alone.
            if (!loadedState.BaselineTaken && !loadedState.BaselineSkipped &&
                UnlockBaseline.SeedHasAlreadyRun(loadedState.NextItemIndex, loadedState.PendingChecks.Count))
            {
                loadedState.BaselineSkipped = true;
                ArchipelagoPlugin.LogSource.LogInfo(
                    "This seed ran before the unlock baseline existed; every unlocked Ember Tree node and weapon mod " +
                    "is still reported as a check.");
            }
            state = loadedState;

            pendingChecks.Clear();
            pendingChecks.UnionWith(state.PendingChecks);
            Goal = ReadSlotString(success.SlotData, "goal", "praxis");
            AshFillerAmount = success.SlotData.ContainsKey("ash_filler_amount")
                ? Math.Clamp(ReadSlotInt(success.SlotData, "ash_filler_amount", 0), 0, 50)
                : null;
            DeathLinkEnabled = ReadSlotBool(success.SlotData, "death_link", plugin.DeathLink.Value);
            StartHeat = Math.Clamp(ReadSlotInt(success.SlotData, "start_heat", 0), 0, HeatSolver.MaxBaseHeat);
            MinimumHeat = Math.Clamp(ReadSlotInt(success.SlotData, "minimum_heat", 15), 0, HeatSolver.MaxBaseHeat);
            HeatReductionPerItem = Math.Max(1, ReadSlotInt(success.SlotData, "heat_reduction_per_item", 1));
            var heatModeText = ReadSlotString(success.SlotData, "heat_mode", plugin.HeatMode.Value);
            RandomHeatAssortment = !string.Equals(heatModeText, "sequential", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(heatModeText, "1", StringComparison.Ordinal);
            // heat_mode alone decides whether the curated tiers apply.
            CuratedHeatDifficulty =
                string.Equals(heatModeText, "curated_heat_difficulty", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(heatModeText, "2", StringComparison.Ordinal);
            var rerollText = ReadSlotString(success.SlotData, "heat_reroll", plugin.HeatReroll.Value);
            rerollHeatEachRun = !string.Equals(rerollText, "once", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(rerollText, "1", StringComparison.Ordinal);
            // string.GetHashCode() is randomized per process in .NET, which would change the "once" roll every
            // time the game restarts. Use a stable hash so the same seed always yields the same assortment.
            heatRunSeed = HeatSolver.StableHash(seed) ^ 0x45D9F3B;
            wasInRunWorld = false;
            StartingArchipelagoCoins = Math.Clamp(
                ReadSlotInt(success.SlotData, "starting_archipelago_coins", plugin.StartingArchipelagoCoins.Value),
                1,
                100);
            ArchipelagoCoinsReceived = StartingArchipelagoCoins;
            startingWeapons.Clear();
            foreach (var weaponId in ReadStartingWeapons(success.SlotData, seed, plugin.ExtraStartingWeapons.Value))
                startingWeapons.Add(weaponId);
            ownedWeapons.Clear();
            ownedWeapons.UnionWith(startingWeapons);
            success.SlotData.TryGetValue("dlc", out var dlcRaw);
            DlcActive = DlcPolicy.ReadDlcActive(dlcRaw);
            success.SlotData.TryGetValue("starting_standard_skill_ids", out var startingSkillsRaw);
            startingStandardSkills = StandardSkillPolicy.ReadStartingSkills(
                startingSkillsRaw, id => GameCatalog.Data.StandardSkillForAbility(id) is not null);
            success.SlotData.TryGetValue("starting_relic_ids", out var startingRelicsRaw);
            startingRelics = StartingRelicPolicy.ReadStartingRelics(
                startingRelicsRaw,
                id => GameCatalog.Data.IsDefaultRelic(id) || GameCatalog.Data.IsOrbRelic(id) ||
                      GameCatalog.Data.IsDlcOrbRelic(id));
            loggedIn = true;
            IsActive = true;
            StatusText = $"Connected - {slotName} @ {currentSession.RoomState.Seed}";
            LastMessage = DeathLinkEnabled ? "Connected with DeathLink enabled." : "Connected.";
            ArchipelagoPlugin.LogSource.LogInfo(
                $"Connected to Archipelago as '{slotName}' (seed {seed}, goal {Goal}, start heat {StartHeat}, min heat {MinimumHeat}, step {HeatReductionPerItem}, starting coins {StartingArchipelagoCoins}, starting weapons [{string.Join(", ", startingWeapons)}]).");
            itemsDirty = true;
            nextItemSyncAt = 0f;
            SaveState();
            EnforceArchipelagoCoins();
            EnforceWeapons();

            if (DeathLinkEnabled)
                deathLinkService?.EnableDeathLink();
            FlushPendingChecks();
            TrySendGoal();
            ScoutUpgradeLocations(currentSession);
        }
        catch (Exception exception)
        {
            loggedIn = false;
            StatusText = "Connected, but AP initialization failed";
            LastMessage = exception.Message;
            ArchipelagoPlugin.LogSource.LogError(exception);
        }
    }

    private void ConnectionFailed(ArchipelagoSession currentSession, string message)
    {
        if (!ReferenceEquals(session, currentSession))
            return;
        connecting = false;
        loggedIn = false;
        StatusText = IsActive ? "Reconnect failed - AP mode still active" : "Connection failed";
        LastMessage = message;
        ArchipelagoPlugin.LogSource.LogWarning($"Archipelago connection failed: {message}");
    }

    private void SocketClosed(ArchipelagoSession currentSession, string reason)
    {
        if (!ReferenceEquals(session, currentSession))
            return;
        loggedIn = false;
        connecting = false;
        SaveState();
        StatusText = IsActive ? "Disconnected - AP mode active; press F6 to reconnect" : "Disconnected";
        LastMessage = string.IsNullOrWhiteSpace(reason) ? "The Archipelago connection closed." : reason;
    }

    private void DisconnectSessionOnly()
    {
        var oldSession = session;
        session = null;
        deathLinkService = null;
        loggedIn = false;
        if (oldSession?.Socket.Connected == true)
            _ = oldSession.Socket.DisconnectAsync();
    }

    private void SyncItems()
    {
        if (!IsConnected || session is null || state is null)
            return;

        try
        {
            var items = session.Items.AllItemsReceived;
            ReceivedItemCount = items.Count;
            ownedAbilities.Clear();
            ownedWeapons.Clear();
            ownedWeapons.UnionWith(startingWeapons);
            ownedRelics.Clear();
            receivedRelicOrder.Clear();
            var heatReductions = 0;
            var coins = StartingArchipelagoCoins;
            foreach (var item in items)
            {
                if (GameCatalog.Data.SkillForItem(item.ItemId) is { } skill)
                    ownedAbilities.Add(skill.AbilityId);
                if (GameCatalog.Data.StandardSkillForItem(item.ItemId) is { } standardSkill)
                    ownedAbilities.Add(standardSkill.AbilityId);
                if (GameCatalog.Data.WeaponForItem(item.ItemId) is { } weapon)
                    ownedWeapons.Add(weapon.WeaponId);
                if (GameCatalog.Data.RelicForItem(item.ItemId) is { } relic && ownedRelics.Add(relic.DefId))
                    receivedRelicOrder.Add(relic.DefId);
                if (GameCatalog.Data.DefaultRelicForItem(item.ItemId) is { } defaultRelic &&
                    ownedRelics.Add(defaultRelic.DefId))
                    receivedRelicOrder.Add(defaultRelic.DefId);
                // DLC items are only ever sent by a seed that has the DLC on, but they are recorded only then, so a stray
                // DLC item in a DLC-off seed can never unlock anything.
                if (DlcActive)
                {
                    if (GameCatalog.Data.DlcSkillForItem(item.ItemId) is { } dlcSkill)
                        ownedAbilities.Add(dlcSkill.AbilityId);
                    if (GameCatalog.Data.DlcWeaponForItem(item.ItemId) is { } dlcWeapon)
                        ownedWeapons.Add(dlcWeapon.WeaponId);
                    if (GameCatalog.Data.DlcRelicForItem(item.ItemId) is { } dlcRelic && ownedRelics.Add(dlcRelic.DefId))
                        receivedRelicOrder.Add(dlcRelic.DefId);
                }
                if (GameCatalog.Data.IsHeatReductionItem(item.ItemId))
                    heatReductions++;
                if (GameCatalog.Data.FillerForItem(item.ItemId) is { Kind: "coin" } coinBundle)
                    coins += coinBundle.Amount;
            }
            HeatReductionsReceived = heatReductions;
            ArchipelagoCoinsReceived = coins;
            EnforceHeat();
            EnforceArchipelagoCoins();
            EnforceWeapons();

            while (state.NextItemIndex < items.Count)
            {
                var index = state.NextItemIndex;
                if (!ApplyItem(items[index]))
                {
                    itemsDirty = true;
                    nextItemSyncAt = Time.realtimeSinceStartup + 1f;
                    return;
                }
                state.NextItemIndex = index + 1;
                SaveState();
            }
        }
        catch (Exception exception)
        {
            itemsDirty = true;
            nextItemSyncAt = Time.realtimeSinceStartup + 2f;
            ArchipelagoPlugin.LogSource.LogError($"Could not reconcile Archipelago items: {exception}");
        }
    }

    private bool ApplyItem(ItemInfo item)
    {
        if (GameCatalog.Data.StandardSkillForItem(item.ItemId) is { } standard)
        {
            LastMessage = $"Received skill: {standard.Name}";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
            return true;
        }
        if (GameCatalog.Data.SkillForItem(item.ItemId) is { } skill)
        {
            LastMessage = $"Received skill: {skill.Name}";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
            return true;
        }
        if (GameCatalog.Data.WeaponForItem(item.ItemId) is { } weapon)
        {
            EnforceWeapons();
            LastMessage = $"Received weapon: {weapon.Name}";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
            return true;
        }
        if (GameCatalog.Data.RelicForItem(item.ItemId) is { } relic)
        {
            LastMessage = $"Received relic: {relic.Name}";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
            return true;
        }
        if (GameCatalog.Data.DefaultRelicForItem(item.ItemId) is { } defaultRelic)
        {
            LastMessage = $"Received relic: {defaultRelic.Name}";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
            return true;
        }
        if (GameCatalog.Data.DlcSkillForItem(item.ItemId) is { } dlcSkill)
        {
            LastMessage = $"Received skill: {dlcSkill.Name}";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
            return true;
        }
        if (GameCatalog.Data.DlcWeaponForItem(item.ItemId) is { } dlcWeapon)
        {
            EnforceWeapons();
            LastMessage = $"Received weapon: {dlcWeapon.Name}";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
            return true;
        }
        if (GameCatalog.Data.DlcRelicForItem(item.ItemId) is { } dlcRelic)
        {
            LastMessage = $"Received relic: {dlcRelic.Name}";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
            return true;
        }
        if (GameCatalog.Data.IsHeatReductionItem(item.ItemId))
        {
            LastMessage = $"Received {CatalogData.HeatReductionItemName} (active heat: {ActiveHeat}).";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
            return true;
        }
        if (GameCatalog.Data.FillerForItem(item.ItemId) is { } filler)
        {
            if (filler.Kind == "coin")
            {
                EnforceArchipelagoCoins();
                LastMessage = $"Received {filler.Name} ({AvailableArchipelagoCoins}/{ArchipelagoCoinsReceived} coins).";
                ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
                return true;
            }

            if (filler.Kind == "ash")
            {
                if (TryGrantAsh(filler))
                    return true;
                if (state is null)
                    return false;
                state.PendingFillers.Add(item.ItemId);
                LastMessage = $"{filler.Name} queued until a save is loaded.";
                ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
                return true;
            }

            var applied = filler.Kind switch
            {
                "ember" => GameActions.GrantEmber(filler.Amount),
                "gold" => GameActions.GrantGold(filler.Amount),
                "trap_ambush" => GameActions.TriggerEnemyAmbushTrap(filler.Amount),
                "trap_boss" => GameActions.TriggerBossAmbushTrap(),
                "trap_praxis" => GameActions.TriggerPraxisArenaTrap(),
                _ => throw new InvalidDataException($"Unknown filler kind '{filler.Kind}'."),
            };
            if (applied)
            {
                LastMessage = filler.Kind.StartsWith("trap_", StringComparison.Ordinal)
                    ? $"Trap triggered: {filler.Name}!"
                    : $"Received {filler.Name}.";
                ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
                return true;
            }

            if (state is not null)
            {
                state.PendingFillers.Add(item.ItemId);
                LastMessage = filler.Kind.StartsWith("trap_", StringComparison.Ordinal)
                    ? $"Trap armed for next battle: {filler.Name}"
                    : $"{filler.Name} queued for next run.";
                ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
                return true;
            }
            return false;
        }

        ArchipelagoPlugin.LogSource.LogWarning($"Ignoring unknown Ember Knights item ID {item.ItemId}.");
        return true;
    }

    /// <summary>
    /// Gives the player the Ash an Ash item is worth and reports what the game actually accepted. Returns false only
    /// when the save is not ready yet, so the caller queues the item; a grant the game clamped is still a success.
    /// </summary>
    private bool TryGrantAsh(FillerDefinition filler)
    {
        var requested = AshFillerAmount ?? filler.Amount;
        if (!GameActions.GrantAsh(requested, out var added))
            return false;

        LastMessage = added <= 0
            ? $"Received {filler.Name}, but no more Ash is needed."
            : added < requested
                ? $"Received {added} Ash (the game only needed {added} more)."
                : $"Received {added} Ash.";
        ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
        return true;
    }

    private void FlushPendingFillers()
    {
        if (state is null || state.PendingFillers.Count == 0)
            return;

        var changed = false;
        for (var i = 0; i < state.PendingFillers.Count; i++)
        {
            var itemId = state.PendingFillers[i];
            if (GameCatalog.Data.FillerForItem(itemId) is not { } filler)
            {
                state.PendingFillers.RemoveAt(i--);
                changed = true;
                continue;
            }

            if (filler.Kind == "ash")
            {
                // TryGrantAsh writes its own message, including how much the game really accepted.
                if (!TryGrantAsh(filler))
                    continue;
                state.PendingFillers.RemoveAt(i--);
                changed = true;
                continue;
            }

            var applied = filler.Kind switch
            {
                "ember" => GameActions.GrantEmber(filler.Amount),
                "gold" => GameActions.GrantGold(filler.Amount),
                "trap_ambush" => GameActions.TriggerEnemyAmbushTrap(filler.Amount),
                "trap_boss" => GameActions.TriggerBossAmbushTrap(),
                "trap_praxis" => GameActions.TriggerPraxisArenaTrap(),
                _ => true,
            };
            if (!applied)
                continue;

            state.PendingFillers.RemoveAt(i--);
            changed = true;
            LastMessage = filler.Kind.StartsWith("trap_", StringComparison.Ordinal)
                ? $"Trap triggered: {filler.Name}!"
                : $"Received {filler.Name}.";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
            // Trigger at most one armed trap per scan tick so multiple queued traps stagger across combat.
            if (filler.Kind.StartsWith("trap_", StringComparison.Ordinal))
                break;
        }

        if (changed)
            SaveState();
    }

    private void CheckLocation(long locationId)
    {
        if (!IsActive || state is null || locationId <= 0)
            return;
        if (IsConnected && session is not null && session.Locations.AllLocationsChecked.Contains(locationId))
            return;
        if (IsConnected && session is not null && session.Locations.AllLocations.Count > 0 &&
            !session.Locations.AllLocations.Contains(locationId))
            return;

        if (pendingChecks.Add(locationId))
        {
            state.PendingChecks = pendingChecks.ToList();
            SaveState();
            LastMessage = $"Check: {GameCatalog.Data.GetLocationName(locationId)}";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
        }
        FlushPendingChecks();
    }

    private void FlushPendingChecks()
    {
        if (!IsConnected || session is null || state is null || pendingChecks.Count == 0)
            return;

        try
        {
            var checkedLocations = session.Locations.AllLocationsChecked;
            var completed = pendingChecks.Where(checkedLocations.Contains).ToArray();
            var toSend = pendingChecks.Where(id => !checkedLocations.Contains(id) &&
                (session.Locations.AllLocations.Count == 0 || session.Locations.AllLocations.Contains(id))).ToArray();
            if (toSend.Length > 0)
                session.Locations.CompleteLocationChecks(toSend);

            var flushed = completed.Length + toSend.Length;
            if (flushed > 0)
            {
                pendingChecks.ExceptWith(completed);
                pendingChecks.ExceptWith(toSend);
                state.PendingChecks = pendingChecks.ToList();
                SaveState();
            }
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Could not flush Archipelago checks: {exception.Message}");
        }
    }

    private void TrySendGoal()
    {
        if (!IsConnected || session is null || state is null || !state.GoalPending)
            return;
        try
        {
            session.SetGoalAchieved();
            state.GoalPending = false;
            SaveState();
            LastMessage = $"Goal achieved: {(Goal == "arch_praxis" ? "Arch Praxis" : "Praxis")}.";
            ArchipelagoPlugin.LogSource.LogInfo(LastMessage);
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogWarning($"Could not report goal: {exception.Message}");
        }
    }

    private void ReceiveDeathLink(DeathLink deathLink)
    {
        if (!IsActive || !DeathLinkEnabled)
            return;
        var now = DateTime.UtcNow;
        if (now < suppressDeathUntil)
            return;

        suppressDeathUntil = now + TimeSpan.FromSeconds(4);
        var killed = GameActions.KillLocalPlayers();
        LastMessage = killed > 0
            ? $"DeathLink from {deathLink.Source}: local death applied."
            : $"DeathLink from {deathLink.Source}: no live local player to apply it to.";
    }

    private void SaveState()
    {
        if (state is null)
            return;
        try
        {
            stateStore.Save(state);
        }
        catch (Exception exception)
        {
            ArchipelagoPlugin.LogSource.LogError($"Could not save Archipelago session state: {exception}");
            LastMessage = "Warning: AP session state could not be saved.";
        }
    }

    private static string ReadSlotString(IReadOnlyDictionary<string, object> slotData, string key, string fallback) =>
        slotData.TryGetValue(key, out var value) && value is not null ? value.ToString() ?? fallback : fallback;

    private static bool ReadSlotBool(IReadOnlyDictionary<string, object> slotData, string key, bool fallback)
    {
        if (!slotData.TryGetValue(key, out var value) || value is null)
            return fallback;
        if (value is bool boolean)
            return boolean;
        if (value is string text && bool.TryParse(text, out var parsed))
            return parsed;
        try
        {
            return Convert.ToInt64(value) != 0;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static int ReadSlotInt(IReadOnlyDictionary<string, object> slotData, string key, int fallback)
    {
        if (!slotData.TryGetValue(key, out var value) || value is null)
            return fallback;
        try
        {
            return Convert.ToInt32(value);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static List<int> ReadStartingWeapons(
        IReadOnlyDictionary<string, object> slotData,
        string seed,
        int fallbackExtraCount)
    {
        var result = new HashSet<int> { 1 };
        if (slotData.TryGetValue("starting_weapon_ids", out var raw) &&
            raw is System.Collections.IEnumerable enumerable &&
            raw is not string)
        {
            foreach (var element in enumerable)
            {
                try
                {
                    var id = Convert.ToInt32(element);
                    // A starting weapon is always a base weapon: the world never starts a seed with a DLC weapon.
                    if (id == 1 || GameCatalog.Data.IsRandomizedWeapon(id))
                        result.Add(id);
                }
                catch (Exception)
                {
                }
            }
            return result.OrderBy(id => id).ToList();
        }

        var extraCount = Math.Clamp(
            ReadSlotInt(slotData, "extra_starting_weapons", fallbackExtraCount),
            0,
            GameCatalog.Data.Weapons.Count);
        var rng = new System.Random(StringComparer.Ordinal.GetHashCode(seed));
        var pool = GameCatalog.Data.Weapons.Select(w => w.WeaponId).OrderBy(_ => rng.Next()).Take(extraCount);
        foreach (var id in pool)
            result.Add(id);
        return result.OrderBy(id => id).ToList();
    }
}
