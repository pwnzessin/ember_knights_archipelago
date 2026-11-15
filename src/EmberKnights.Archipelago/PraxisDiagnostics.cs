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
        Log($"Trap wrote effect {effect} ({Describe(world.GameState?.PraxisEffectData)}); {RoomSummary(world)}");
    }

    public static string RoomSummary(GameCode.XWorld world)
    {
        var room = world.currentRoomCMP;
        return room is null
            ? "room=null"
            : $"area={(int)(world.currentArea?.areaName ?? 0)} roomType={room.roomType} roomState={room.roomState} " +
              $"enemies={world.LiveEnemies?.Count ?? 0} players={world.LivePlayers?.Count ?? 0}";
    }

    /// <summary>Called from the game's per-frame Praxis tick; logs on a coarse cadence while a trap is being watched.</summary>
    public static void NoteTick(GameCode.XWorld world)
    {
        var now = Time.realtimeSinceStartup;
        if (now > watchUntil || now < nextLogAt)
            return;
        nextLogAt = now + LogInterval;
        Log($"tick (trap effect {watchedEffect}): {Describe(world.GameState?.PraxisEffectData)}; {RoomSummary(world)}");
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
                PraxisDiagnostics.Log(
                    $"UpdatePraxisHandEffect active={Active} effect={EffectType} seed={Seed} watching={PraxisDiagnostics.Watching}");
        }
        catch (Exception exception)
        {
            PatchSafety.Report("PraxisDiag.UpdatePraxisHandEffect", exception);
        }
    }
}
