using HarmonyLib;

namespace EmberKnights.Archipelago;

/// <summary>
/// Makes the Slow Hands and Laser Hands effects of the Praxis Arena Trap actually run in an ordinary battle room.
///
/// The trap writes the effect into the game's Praxis room-effect data, and the game acknowledges it, but in a normal battle
/// room nothing ever starts the hand effect's own code: the room-effect data stays uninitialised and has no hand objects, so
/// the effect shows nothing. The Praxis boss room supplies its own hand objects as an override. The game also keeps one
/// complete set in <c>GameEngine.PraxisHandRefs</c> (10 hands and 2 laser rays, checked in the log), so this class starts the
/// effect itself and hands it that set.
///
/// It only acts for an effect the trap fired (<see cref="Arm"/>), never for one the game starts on its own, and it stops
/// when the effect reports it is finished or the room ends. Every step is logged with [PraxisHands].
/// </summary>
internal static class PraxisHandDriver
{
    private static ePraxisEffectType armedEffect = ePraxisEffectType.None;
    private static bool initialized;
    private static bool finishedLogged;
    private static int ticks;

    /// <summary>True while a trap-fired hand effect is being driven.</summary>
    public static bool Active => armedEffect != ePraxisEffectType.None;

    /// <summary>The trap calls this right after writing a hand effect. Other effects are ignored.</summary>
    public static void Arm(ePraxisEffectType effect)
    {
        Reset();
        if (effect is ePraxisEffectType.SlowHands or ePraxisEffectType.LaserHands)
        {
            armedEffect = effect;
            Log($"Armed for {effect}.");
        }
    }

    public static void Reset()
    {
        // Every way the effect can end passes through here, so the engine hand object always goes back where it was.
        RestoreHandObject();
        armedEffect = ePraxisEffectType.None;
        initialized = false;
        finishedLogged = false;
        ticks = 0;
        startedInRoom = -1;
        leftSpawningAt = -1f;
    }

    private static UnityEngine.Transform? movedHandObject;
    private static UnityEngine.Vector3 handObjectOriginalPosition;

    /// <summary>
    /// Moves the engine's hand object onto the room. The hands and the box they move in are children of that object, and the
    /// log showed them at x = 712 and 1288 while the room was at x = -12480, so the effect was working in the object's own
    /// place, not the room's. Moving the object by the gap between the two puts the hands in the room. The original
    /// position is remembered and put back by <see cref="RestoreHandObject"/> whenever the effect ends.
    /// </summary>
    private static void MoveHandObjectToRoom(XPraxisHandRoomRefs refs, GameCode.XRoomCMP room)
    {
        RestoreHandObject();
        var transform = refs.transform;
        var before = transform.position;
        var target = room.WorldBounds.center;
        // Only x and y are moved; the object keeps its own depth.
        var moved = new UnityEngine.Vector3(target.x, target.y, before.z);
        movedHandObject = transform;
        handObjectOriginalPosition = before;
        transform.position = moved;
        Log($"Moved the engine hand object from ({before.x:F0},{before.y:F0}) to the room centre ({moved.x:F0},{moved.y:F0}); " +
            $"now at ({transform.position.x:F0},{transform.position.y:F0}).");
    }

    /// <summary>Puts the engine hand object back where the game had it. Safe to call when nothing was moved.</summary>
    private static void RestoreHandObject()
    {
        if (movedHandObject is null)
            return;
        try
        {
            movedHandObject.position = handObjectOriginalPosition;
            Log($"Put the engine hand object back at ({handObjectOriginalPosition.x:F0},{handObjectOriginalPosition.y:F0}).");
        }
        catch (Exception exception)
        {
            Log($"Could not put the engine hand object back: {exception.GetType().Name}: {exception.Message}");
        }
        movedHandObject = null;
    }

    /// <summary>The room instance the running effect started in. -1 until it starts.</summary>
    private static int startedInRoom = -1;

    private static void Log(string message) => ArchipelagoPlugin.LogSource.LogInfo($"[PraxisHands] {message}");

    /// <summary>Called once per frame from the game's Praxis tick, after the game has done its own work.</summary>
    public static void Tick(GameCode.XWorld world, float dt)
    {
        if (!Active)
            return;

        var room = world.currentRoomCMP;
        var data = world.A5RoomEffectData;
        var refs = GameEngine.Instance?.PraxisHandRefs;
        if (room is null || data is null || refs is null)
        {
            Log($"Cannot drive {armedEffect}: room={(room is null ? "null" : "ok")} data={(data is null ? "null" : "ok")} " +
                $"engineRefs={(refs is null ? "null" : "ok")}. Giving up.");
            Reset();
            return;
        }

        // Only start in the battle room the trap fired in. Once running, the checks below stop it when the room ends.
        if (!initialized && (room.roomType != eRoomType.Battle || room.roomState != GameCode.eRoomState.NewBattleRoomSpawning))
        {
            Log($"Not starting {armedEffect}: room is {room.roomType}/{room.roomState}. Giving up.");
            Reset();
            return;
        }

        try
        {
            if (!initialized)
            {
                Log($"Initialising {armedEffect} with the engine hand refs. {PraxisDiagnostics.DescribeRoomEffect(world)}");
                // The hand systems keep static state (hands, finished flags). An earlier hand effect leaves it finished,
                // and the next one then starts already done, so wipe it and the room-effect data before starting.
                ResetLeftovers(data);
                GiveSpawnArea(data, room);
                MoveHandObjectToRoom(refs, room);
                startedInRoom = room.RoomInstId;
                if (armedEffect == ePraxisEffectType.SlowHands)
                    GameCode.XA5RoomEffectPraxisHandsSlowSYS.Initialize(world, data, room, refs);
                else
                    GameCode.XA5RoomEffectPraxisHandsLaserSYS.Initialize(world, data, room, refs);
                initialized = true;
                Log($"Initialized. {PraxisDiagnostics.DescribeRoomEffect(world)}");
                return;
            }

            // The player walked into another room: the effect belongs to the room it started in, so end it there.
            if (room.RoomInstId != startedInRoom)
            {
                Finish(world, data, room, $"the player left room {startedInRoom} for room {room.RoomInstId}");
                return;
            }

            ticks++;
            if (armedEffect == ePraxisEffectType.SlowHands)
                GameCode.XA5RoomEffectPraxisHandsSlowSYS.Tick(world, data, room, dt, refs);
            else
                GameCode.XA5RoomEffectPraxisHandsLaserSYS.Tick(world, data, room, dt, refs);

            if (ticks == 1 || ticks % 120 == 0)
                Log($"Tick {ticks} ({armedEffect}): {PraxisDiagnostics.DescribeRoomEffect(world)}");
            // One long line a second is evidence for a bug report, not something every player's log needs: it follows the
            // same switch as the other [PraxisDiag] lines.
            if (PraxisDiagnostics.Enabled)
                LogHandPositions(room, refs);

            var finished = armedEffect == ePraxisEffectType.SlowHands
                ? GameCode.XA5RoomEffectPraxisHandsSlowSYS.IsFinished(world, data, room)
                : GameCode.XA5RoomEffectPraxisHandsLaserSYS.IsFinished(world, data, room);
            if (finished)
            {
                Finish(world, data, room, "the effect reports it is finished");
            }
            else if (PraxisHandStopPolicy.ShouldStopForRoomState(
                         room.roomState == GameCode.eRoomState.NewBattleRoomSpawning, UnityEngine.Time.realtimeSinceStartup, ref leftSpawningAt))
            {
                Finish(world, data, room, $"the room left the fight and is now {room.roomState}");
            }
        }
        catch (Exception exception)
        {
            Log($"{armedEffect} failed and was stopped: {exception.GetType().Name}: {exception.Message}");
            TryCleanup();
            Reset();
        }
    }

    /// <summary>The time the room was first seen outside the fight, or -1 while it is still in the fight.</summary>
    private static float leftSpawningAt = -1f;

    private static void Finish(GameCode.XWorld world, GameCode.XA5RoomEffectData data, GameCode.XRoomCMP room, string why)
    {
        if (!finishedLogged)
        {
            finishedLogged = true;
            Log($"Stopping {armedEffect}: {why}.");
        }
        try
        {
            if (armedEffect == ePraxisEffectType.SlowHands)
                GameCode.XA5RoomEffectPraxisHandsSlowSYS.OnRoomComplete(world, data, room);
            else
                GameCode.XA5RoomEffectPraxisHandsLaserSYS.OnRoomComplete(world, data, room);
        }
        catch (Exception exception)
        {
            Log($"Cleanup of {armedEffect} failed: {exception.GetType().Name}: {exception.Message}");
        }
        TryCleanup();
        Reset();
    }

    /// <summary>
    /// Clears what an earlier hand effect left behind: the two systems' static state, and the room-effect data's state and
    /// timer. Without this the second effect of a session starts in the Finished state (seen in the log with Laser Hands
    /// right after Slow Hands).
    /// </summary>
    private static void ResetLeftovers(GameCode.XA5RoomEffectData data)
    {
        TryCleanup(GameCode.XA5RoomEffectPraxisHandsSlowSYS.Cleanup);
        TryCleanup(GameCode.XA5RoomEffectPraxisHandsLaserSYS.Cleanup);
        data.State = 0;
        data.StateTimer = 0f;
        data.IsInitialized = false;
    }

    /// <summary>
    /// Gives the effect an area to spawn in. In an ordinary battle room the room-effect data has no spawn points and an empty
    /// spawn box, which the hand code builds its hand positions from, so the hands had nowhere to appear. The room's own world
    /// bounds are the area the player can actually move in.
    /// </summary>
    private static void GiveSpawnArea(GameCode.XA5RoomEffectData data, GameCode.XRoomCMP room)
    {
        var bounds = room.WorldBounds;
        data.SpawnBounds = bounds;
        Log($"Spawn area set from the room's world bounds: center={bounds.center} size={bounds.size}.");
    }

    private static float nextPositionLogAt;

    /// <summary>
    /// Once a second while a hand effect runs, says where each hand and laser ray is: whether it is active in the scene, its
    /// world position and its scale. The effect's state machine can run to the end without any hand appearing, and its own
    /// state does not say whether a hand was really placed, so this is the evidence for "spawned but off-screen" against
    /// "never activated". Reads only; changes nothing.
    /// </summary>
    private static void LogHandPositions(GameCode.XRoomCMP room, XPraxisHandRoomRefs refs)
    {
        var now = UnityEngine.Time.realtimeSinceStartup;
        if (now < nextPositionLogAt)
            return;
        nextPositionLogAt = now + 1f;

        var bounds = room.WorldBounds;
        var parts = new List<string>();
        var hands = refs.Hands;
        for (var i = 0; hands is not null && i < hands.Count; i++)
        {
            var hand = hands[i];
            if (hand is null)
                continue;
            var go = hand.gameObject;
            var t = hand.transform;
            var p = t.position;
            var inRoom = bounds.Contains(new UnityEngine.Vector3(p.x, p.y, bounds.center.z));
            // Only report hands that are doing something, so a list of ten idle hands does not bury the log.
            if (go.activeInHierarchy || i < 2)
                parts.Add($"{hand.name}[active={go.activeInHierarchy} pos=({p.x:F0},{p.y:F0}) inRoom={inRoom} scale=({t.localScale.x:F2},{t.localScale.y:F2}) playing={hand.IsPlaying}]");
        }
        var activeHands = 0;
        for (var i = 0; hands is not null && i < hands.Count; i++)
            if (hands[i] is { } h && h.gameObject.activeInHierarchy)
                activeHands++;

        var rays = refs.HandRays;
        var rayParts = new List<string>();
        for (var i = 0; rays is not null && i < rays.Count; i++)
        {
            var ray = rays[i];
            if (ray is null)
                continue;
            var p = ray.transform.position;
            rayParts.Add($"ray{i}[active={ray.gameObject.activeInHierarchy} pos=({p.x:F0},{p.y:F0})]");
        }

        Log($"Hands: {activeHands} of {hands?.Count ?? 0} active; room center=({bounds.center.x:F0},{bounds.center.y:F0}) " +
            $"size=({bounds.size.x:F0},{bounds.size.y:F0}); {string.Join(" ", parts)} {string.Join(" ", rayParts)}");
    }

    private static void TryCleanup(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            Log($"Cleanup call failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void TryCleanup()
    {
        try
        {
            if (armedEffect == ePraxisEffectType.SlowHands)
                GameCode.XA5RoomEffectPraxisHandsSlowSYS.Cleanup();
            else if (armedEffect == ePraxisEffectType.LaserHands)
                GameCode.XA5RoomEffectPraxisHandsLaserSYS.Cleanup();
        }
        catch (Exception exception)
        {
            Log($"Cleanup call failed: {exception.GetType().Name}: {exception.Message}");
        }
    }
}

/// <summary>Runs the hand driver after the game's own per-frame Praxis tick.</summary>
[HarmonyPatch(typeof(GameCode.XWorld), nameof(GameCode.XWorld.TickPraxisEffect))]
internal static class PraxisHandDriverTickPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameCode.XWorld __instance, float dt)
    {
        try
        {
            PraxisHandDriver.Tick(__instance, dt);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("PraxisHandDriver.Tick", exception);
            PraxisHandDriver.Reset();
        }
    }
}
