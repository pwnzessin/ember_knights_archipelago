using HarmonyLib;
using UnityEngine;

namespace EmberKnights.Archipelago;

/// <summary>
/// Read-only logging for the Praxis Arena Trap. It records what the game does with the effect data after the trap
/// writes it, so it is clear which effect types the game really runs in an ordinary battle room.
/// None of the patches below change game behavior.
/// </summary>
internal static class PraxisDiagnostics
{
    private const float WatchSeconds = 12f;
    private const float LogInterval = 0.5f;

    private static float watchUntil;
    private static float nextLogAt;
    private static ePraxisEffectType watchedEffect;

    public static bool Enabled => ArchipelagoPlugin.Instance?.PraxisDiagnosticsEnabled?.Value ?? false;

    public static void Log(string message) =>
        ArchipelagoPlugin.LogSource.LogInfo($"[PraxisDiag] {message}");

    public static string Describe(GameCode.PraxisRoomEffectData? data) =>
        data is null
            ? "data=null"
            : $"active={data.RoomActive} next={data.NextEffect} spawnTime={data.SpawnTime:0.00} " +
              $"roomsUntil={data.RoomsUntilEffect} skillIntro={data.IsShowingSkillIntroCutscene}";

    /// <summary>Called by the trap right after it writes the effect data.</summary>
    public static void NoteTrapFired(GameCode.XWorld world, ePraxisEffectType effect)
    {
        if (!Enabled)
            return;
        watchedEffect = effect;
        watchUntil = Time.realtimeSinceStartup + WatchSeconds;
        nextLogAt = 0f;
        handEffectSeen = false;
        outcomeReported = false;
        Log($"Trap wrote effect {effect} ({Describe(world.GameState?.PraxisEffectData)}); {RoomSummary(world)}");
        // The facts that decide whether the missing pieces could be supplied, taken at the moment of firing.
        if (effect is ePraxisEffectType.SlowHands or ePraxisEffectType.LaserHands)
            Log($"BEFORE {effect}: {DescribeEngineHandRefs()}; {DescribeRoomEffect(world)}");
        if (effect == ePraxisEffectType.SkillDisabler)
            Log($"BEFORE {effect}: {DescribeSkillDisabler(world)}");
    }

    public static string RoomSummary(GameCode.XWorld world)
    {
        var room = world.currentRoomCMP;
        return room is null
            ? "room=null"
            : $"area={(int)(world.currentArea?.areaName ?? 0)} room='{RoomName(room)}' roomInst={room.RoomInstId} " +
              $"roomType={room.roomType} roomState={room.roomState} " +
              $"enemies={world.LiveEnemies?.Count ?? 0} players={world.LivePlayers?.Count ?? 0}";
    }

    /// <summary>
    /// The room's prefab name, which identifies its layout. The same layout has the same name every time it appears, so
    /// a table of effect against room name shows whether an effect depends on the room.
    /// </summary>
    public static string RoomName(GameCode.XRoomCMP room)
    {
        try
        {
            return room.Refs?.name ?? "?";
        }
        catch (Exception)
        {
            return "?";
        }
    }

    /// <summary>
    /// The engine-wide hand objects the game keeps (<c>GameEngine.PraxisHandRefs</c>). The room's own hand refs are null in
    /// an ordinary battle room, so this is the candidate the hand effects could use instead. It records whether the object
    /// exists, whether it is active in the scene, and how many hands and laser rays it holds, so one test answers whether
    /// handing it to the effect could possibly work.
    /// </summary>
    public static string DescribeEngineHandRefs()
    {
        try
        {
            var refs = GameEngine.Instance?.PraxisHandRefs;
            if (refs is null)
                return "engine PraxisHandRefs=null";
            var active = refs.gameObject is { } go && go.activeInHierarchy;
            var hands = refs.Hands;
            var firstHand = hands is { Count: > 0 } ? hands[0] : null;
            // HandMoveBounds is a physics collider, and the plugin does not reference the physics module, so it is not read here.
            return $"engine PraxisHandRefs exists activeInHierarchy={active} hands={hands?.Count ?? -1} " +
                   $"rays={refs.HandRays?.Count ?? -1} " +
                   $"firstHand={(firstHand is null ? "none" : firstHand.name)} " +
                   $"firstHandActive={(firstHand is not null && firstHand.gameObject.activeInHierarchy)}";
        }
        catch (Exception exception)
        {
            return $"engine PraxisHandRefs unreadable ({exception.GetType().Name}: {exception.Message})";
        }
    }

    /// <summary>
    /// What the game says about the Skill Disabler obelisk: whether this room has the marker the game spawns it at, how
    /// many obelisks the game counts alive, how many Skill Disabler enemies are really in the room, and the ids the room
    /// keeps for it. Compared before and after the trap, it shows whether anything was spawned.
    /// </summary>
    public static string DescribeSkillDisabler(GameCode.XWorld world)
    {
        try
        {
            var room = world.currentRoomCMP;
            var spawnPoint = room?.Refs?.SkillDisablerObeliskSpawnPointT;
            var enemies = 0;
            var live = world.LiveEnemies;
            for (var i = 0; live is not null && i < live.Count; i++)
            {
                var enemy = live[i];
                if (enemy is not null && GameCode.EnemyUtils.GetParams(enemy).EnemyType == eEnemy.SkillDisabler)
                    enemies++;
            }
            var state = world.GameState;
            return $"skillDisabler: roomHasSpawnPoint={(spawnPoint is null ? "no" : $"yes at {spawnPoint.position}")} " +
                   $"obelisksAlive={state?.NumSkillDisablerObelisksAlive.ToString() ?? "?"} " +
                   $"areAlive={state?.AreSkillDisablerObelisksAlive.ToString() ?? "?"} liveEnemiesOfType={enemies} " +
                   $"roomNetId={room?.PraxisSkillDisablerNetId.ToString() ?? "?"} roomEntityId={room?.SkillDisablerEntityId.ToString() ?? "?"}";
        }
        catch (Exception exception)
        {
            return $"skillDisabler unreadable ({exception.GetType().Name}: {exception.Message})";
        }
    }

    /// <summary>
    /// What the game's room-effect data holds for the hand and laser effects: whether the hand objects exist, how many
    /// hands and laser rays there are, the box the effect may spawn in, and how many spawn points the room offered. A
    /// missing hand object or an empty spawn box is the kind of thing that would make a hand effect show nothing.
    /// </summary>
    public static string DescribeRoomEffect(GameCode.XWorld world)
    {
        try
        {
            var data = world.A5RoomEffectData;
            if (data is null)
                return "A5RoomEffectData=null";
            var hands = data.HandRefs;
            var handText = hands is null
                ? "HandRefs=null"
                : $"HandRefs hands={hands.Hands?.Count ?? -1} rays={hands.HandRays?.Count ?? -1}";
            return $"A5 init={data.IsInitialized} state={data.State} spawnPoints={data.SpawnPoints?.Count ?? -1} " +
                   $"spawnBounds={data.SpawnBounds.size} fromPraxis={data.IsFromPraxisEffect} {handText}";
        }
        catch (Exception exception)
        {
            return $"A5RoomEffectData unreadable ({exception.GetType().Name})";
        }
    }

    /// <summary>Called from the game's per-frame Praxis tick; logs on a coarse cadence while a trap is being watched.</summary>
    public static void NoteTick(GameCode.XWorld world)
    {
        var now = Time.realtimeSinceStartup;
        ReportOutcomeIfDue(world);
        if (now > watchUntil || now < nextLogAt)
            return;
        nextLogAt = now + LogInterval;
        Log($"tick (trap effect {watchedEffect}): {Describe(world.GameState?.PraxisEffectData)}; {RoomSummary(world)}; " +
            $"{DescribeRoomEffect(world)}");
    }

    private static bool handEffectSeen;
    private static bool outcomeReported;

    /// <summary>Called when the game starts a visible Praxis hand effect, which is the proof that an effect appeared.</summary>
    public static void NoteHandEffect() => handEffectSeen = true;

    /// <summary>
    /// Writes one line when the watch ends that says whether the game ever showed the effect. Without it the answer is
    /// spread over many tick lines; with it a series of traps reads as a plain list of "effect, room, shown or not".
    /// </summary>
    public static void ReportOutcomeIfDue(GameCode.XWorld world)
    {
        if (outcomeReported || Time.realtimeSinceStartup <= watchUntil)
            return;
        outcomeReported = true;
        Log($"RESULT effect={watchedEffect} room='{(world.currentRoomCMP is { } room ? RoomName(room) : "?")}' " +
            $"visibleHandEffectSeen={handEffectSeen} (only the hand effects report one; meteors and wells show on screen instead)");
        if (watchedEffect is ePraxisEffectType.SlowHands or ePraxisEffectType.LaserHands)
            Log($"AFTER {watchedEffect}: {DescribeEngineHandRefs()}; {DescribeRoomEffect(world)}");
        if (watchedEffect == ePraxisEffectType.SkillDisabler)
            Log($"AFTER {watchedEffect}: {DescribeSkillDisabler(world)}");
    }

    public static bool Watching => Time.realtimeSinceStartup <= watchUntil;
}

[HarmonyPatch(typeof(GameCode.XWorld), nameof(GameCode.XWorld.TickPraxisEffect))]
internal static class PraxisDiagTickPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameCode.XWorld __instance)
    {
        try
        {
            if (PraxisDiagnostics.Enabled)
                PraxisDiagnostics.NoteTick(__instance);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("PraxisDiag.TickPraxisEffect", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.XWorld), nameof(GameCode.XWorld.InitializePraxisEffect))]
internal static class PraxisDiagInitializePatch
{
    [HarmonyPostfix]
    private static void Postfix(GameCode.XWorld __instance)
    {
        try
        {
            if (PraxisDiagnostics.Enabled)
                PraxisDiagnostics.Log(
                    $"InitializePraxisEffect called ({PraxisDiagnostics.Describe(__instance.GameState?.PraxisEffectData)}); " +
                    $"watching={PraxisDiagnostics.Watching}");
        }
        catch (Exception exception)
        {
            PatchSafety.Report("PraxisDiag.InitializePraxisEffect", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.XWorld), nameof(GameCode.XWorld.UpdatePraxisHandEffect))]
internal static class PraxisDiagHandEffectPatch
{
    [HarmonyPostfix]
    private static void Postfix(bool Active, ePraxisEffectType EffectType, ulong Seed)
    {
        try
        {
            if (PraxisDiagnostics.Enabled)
            {
                if (Active && PraxisDiagnostics.Watching)
                    PraxisDiagnostics.NoteHandEffect();
                PraxisDiagnostics.Log(
                    $"UpdatePraxisHandEffect active={Active} effect={EffectType} seed={Seed} watching={PraxisDiagnostics.Watching}");
            }
        }
        catch (Exception exception)
        {
            PatchSafety.Report("PraxisDiag.UpdatePraxisHandEffect", exception);
        }
    }
}
