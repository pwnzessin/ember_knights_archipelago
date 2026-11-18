namespace EmberKnights.Archipelago;

/// <summary>
/// Pure decisions for the Enemy Ambush Trap: where the trapped enemies stand, and which enemies may be chosen when the
/// seed lets traps use enemies from any world. No game dependencies, so it can be unit tested; the game-facing code in
/// <c>GameActions</c> feeds it plain numbers and applies the result.
/// </summary>
internal static class TrapSpawnPolicy
{
    /// <summary>
    /// Distance between neighbouring enemies in the grid, in world units. A path node is 16 units and the game places
    /// battle enemies in zones of 15 to 45 units, so 28 keeps neighbours apart without pushing them out of a room.
    /// Nothing in the game collides, so the point is that an area attack cannot hit a whole stack at once.
    /// </summary>
    public const float GridSpacing = 28f;

    /// <summary>
    /// Offsets (x, y) from the centre for <paramref name="count"/> enemies, filling a square grid outward from the
    /// middle: the centre cell first, then the cells around it ring by ring, nearest first. The first offset is always
    /// (0, 0), so a single enemy stands exactly where the trap would have put it.
    /// </summary>
    public static (float X, float Y)[] GridOffsets(int count, float spacing = GridSpacing)
    {
        if (count <= 0)
            return Array.Empty<(float, float)>();

        // Enough rings to hold every enemy: a ring of radius r adds 8r cells, so (2r + 1)^2 cells fit within r rings.
        var rings = 0;
        while ((2 * rings + 1) * (2 * rings + 1) < count)
            rings++;

        var cells = new List<(int Col, int Row)>();
        for (var row = -rings; row <= rings; row++)
        {
            for (var col = -rings; col <= rings; col++)
                cells.Add((col, row));
        }

        // Nearest to the centre first. Ties break by row then column so the order is the same every time.
        cells.Sort((a, b) =>
        {
            var byDistance = (a.Col * a.Col + a.Row * a.Row).CompareTo(b.Col * b.Col + b.Row * b.Row);
            if (byDistance != 0)
                return byDistance;
            var byRow = a.Row.CompareTo(b.Row);
            return byRow != 0 ? byRow : a.Col.CompareTo(b.Col);
        });

        var offsets = new (float X, float Y)[count];
        for (var i = 0; i < count; i++)
            offsets[i] = (cells[i].Col * spacing, cells[i].Row * spacing);
        return offsets;
    }

    /// <summary>
    /// True when the world number is one the seed may draw trap enemies from. The five base worlds count always. The
    /// four DLC worlds and the Architect level count only when the seed uses the DLC, so without it no DLC enemy is
    /// ever chosen. The Nexus, credits and the other non-run areas never count.
    /// </summary>
    public static bool IsEligibleWorld(int areaName, bool dlcActive) => DlcPolicy.IsTrackedWorld(areaName, dlcActive);

    /// <summary>
    /// Whether a game enemy definition may be drawn as a trap enemy. Only ordinary, non-stationary, base-rank enemies of
    /// an eligible world: no bosses or minibosses, no elite or champion duplicates (those spawn from the base type with
    /// a rank), no player clones, no hatcheries or other stationary props, and no definition the game has switched off
    /// or caps at zero. A DLC enemy is excluded unless the seed uses the DLC.
    /// </summary>
    public static bool IsEligibleEnemy(
        bool enabled,
        bool isDlcContent,
        int areaName,
        bool isBoss,
        bool isMiniBoss,
        bool isBaseRank,
        bool isStationary,
        bool isPlayerClone,
        int maxSpawnCount,
        bool dlcActive)
    {
        if (!enabled || isBoss || isMiniBoss || !isBaseRank || isStationary || isPlayerClone || maxSpawnCount == 0)
            return false;
        if (isDlcContent && !dlcActive)
            return false;
        return IsEligibleWorld(areaName, dlcActive);
    }

    /// <summary>
    /// Picks a trap enemy from a pool with a caller-supplied random source, so the choice can be tested. Returns -1 for
    /// an empty pool.
    /// </summary>
    public static int PickIndex(int poolSize, Func<int, int> nextBelow) => poolSize <= 0 ? -1 : nextBelow(poolSize);

    /// <summary>
    /// Reads <c>trap_enemies_any_world</c> from slot data. A seed generated before the option existed has no such key and
    /// keeps the old behaviour, where trap enemies come from the world the player is in.
    /// </summary>
    public static bool ReadAnyWorld(object? raw) => raw switch
    {
        bool value => value,
        string text => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1",
        null => false,
        _ => TryNumber(raw),
    };

    private static bool TryNumber(object raw)
    {
        try
        {
            return Convert.ToInt64(raw) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Remembers which enemies the Boss Ambush Trap spawned, so one of them can be cleaned up when it dies.
///
/// The trap spawns the area's real miniboss straight into an ordinary battle room. The game normally runs a miniboss
/// through its own fight object, whose death sequence removes the body; a miniboss that never joined that fight is
/// killed but its sprite stays on screen. Only entities the trap itself created are ever tracked, so no enemy of the
/// game's own is touched. An entity is identified by the address of its native object (an <see cref="IntPtr"/>), which
/// is what the game's wrapper types expose.
/// </summary>
internal sealed class TrapBossTracker
{
    private readonly HashSet<IntPtr> spawned = new();

    /// <summary>How many trap minibosses are currently remembered.</summary>
    public int Count => spawned.Count;

    /// <summary>True when the entity is remembered. Unlike <see cref="TakeIfTracked"/> it does not forget it.</summary>
    public bool Contains(IntPtr entity) => entity != IntPtr.Zero && spawned.Contains(entity);

    /// <summary>Remembers an entity the trap just spawned. A null (zero) address is ignored.</summary>
    public void Track(IntPtr entity)
    {
        if (entity != IntPtr.Zero)
            spawned.Add(entity);
    }

    /// <summary>
    /// True exactly once for a remembered entity, at its death, and the entity is forgotten at the same time. An entity
    /// that was never tracked, or was already reported, returns false, so the game's own enemies are never cleaned up and
    /// a second death event for the same one does nothing.
    /// </summary>
    public bool TakeIfTracked(IntPtr entity) => entity != IntPtr.Zero && spawned.Remove(entity);

    /// <summary>Forgets everything, for a new connection or when the world is torn down.</summary>
    public void Clear() => spawned.Clear();

    /// <summary>A copy of the remembered addresses, so a caller can walk them while the tracker changes underneath.</summary>
    public IReadOnlyList<IntPtr> Snapshot() => spawned.ToList();
}

/// <summary>
/// Decides when a Praxis hand effect started by the trap must be stopped because the fight is over. No game dependency.
///
/// The effect starts while the room is still spawning enemies. Its own code keeps spawning projectiles for as long as it is
/// ticked, even after its hands are gone, so the mod has to stop it when the room moves on (reward, door choice, next room).
/// A short grace period keeps a one-frame flicker of the room state from ending the effect early.
/// </summary>
internal static class PraxisHandStopPolicy
{
    /// <summary>Seconds the room must stay outside the fight before the effect is stopped.</summary>
    public const float GraceSeconds = 0.5f;

    /// <summary>
    /// True when the effect should be stopped now. <paramref name="stillInFight"/> says whether the room is still in the
    /// state it started in. <paramref name="leftFightAt"/> holds when the room was first seen outside it (-1 while it is
    /// inside); this method sets and clears it.
    /// </summary>
    public static bool ShouldStopForRoomState(bool stillInFight, float now, ref float leftFightAt)
    {
        if (stillInFight)
        {
            leftFightAt = -1f;
            return false;
        }

        if (leftFightAt < 0f || now < leftFightAt)
        {
            leftFightAt = now;
            return false;
        }

        return now - leftFightAt >= GraceSeconds;
    }
}

/// <summary>
/// Decides when the Boss Ambush Trap's miniboss should be checked and when it counts as dead. No game dependency.
///
/// The game never runs its death handling for the trap's miniboss (the log shows no dead state and no kill event for it),
/// so it stays at 0 HP, repeating its last attack. The mod therefore watches its health itself. A corpse removed a fraction
/// of a second late is not noticeable, so the watch runs on a timer instead of every frame.
/// </summary>
internal static class TrapBossWatch
{
    /// <summary>Seconds between health checks. Four checks a second is far finer than anyone can see and costs almost nothing.</summary>
    public const float CheckIntervalSeconds = 0.25f;

    /// <summary>True when enough time has passed since the last check, or when there has been no check yet.</summary>
    public static bool IsDue(float now, float lastCheckAt) =>
        lastCheckAt < 0f || now - lastCheckAt >= CheckIntervalSeconds || now < lastCheckAt;

    /// <summary>
    /// True when the miniboss's health says it is dead. A missing or unreadable health reading (NaN) is never dead, so a
    /// read that failed can not remove a living boss.
    /// </summary>
    public static bool IsDead(float currentHealth) => !float.IsNaN(currentHealth) && currentHealth <= 0f;
}
