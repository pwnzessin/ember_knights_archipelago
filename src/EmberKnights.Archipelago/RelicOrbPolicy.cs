namespace EmberKnights.Archipelago;

/// <summary>What Esper's reveal should show for one orb: a real relic from this game, or text for anything else.</summary>
internal readonly record struct OrbReveal(int? RelicDefId, string? Text);

/// <summary>Pure decisions about relic orbs. No game dependencies.</summary>
internal static class RelicOrbPolicy
{
    /// <summary>
    /// Counts the relics waiting for Esper that belong to the 53 randomized orbs. Other relics (DLC orbs, etc.)
    /// share the same pending list but are not Archipelago locations.
    /// </summary>
    public static int CountPendingOrbRelics(IEnumerable<int>? pendingRelicDefIds, Func<int, bool> isOrbRelic)
    {
        if (pendingRelicDefIds is null)
            return 0;
        var count = 0;
        foreach (var defId in pendingRelicDefIds)
        {
            if (isOrbRelic(defId))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Works out what Esper's reveal shows for the orbs being handed in. The check for an orb is sent when it is
    /// picked up, so by the time Esper identifies it the multiworld already knows what that orb's location holds.
    /// Orb <c>n</c> is the n-th orb collected; <paramref name="alreadyRevealed"/> orbs were shown in earlier visits.
    /// An orb whose location was never scouted yields an empty reveal, which keeps the game's own relic.
    /// </summary>
    public static OrbReveal[] ChooseReveals(
        int alreadyRevealed,
        int orbsHandedIn,
        int orbsCollected,
        Func<int, ScoutedOrb?> scoutedForOrder)
    {
        var shown = new OrbReveal[Math.Max(0, orbsHandedIn)];
        for (var slot = 0; slot < shown.Length; slot++)
        {
            var order = Math.Max(0, alreadyRevealed) + slot + 1;
            if (order > orbsCollected || scoutedForOrder(order) is not { } scouted)
                continue;
            shown[slot] = scouted.IsOwnRelic
                ? new OrbReveal(scouted.RelicDefId, null)
                : new OrbReveal(null, scouted.Describe());
        }
        return shown;
    }

    /// <summary>Text for the overlay when the item is not one of this game's relics.</summary>
    public static string FormatEsperMessage(string itemName, string playerName, bool isOwnPlayer) =>
        isOwnPlayer
            ? $"Esper senses: {itemName}"
            : $"Esper senses: {itemName} for {playerName}";
}

/// <summary>
/// Which of the seven base-game standard skills a seed gates behind Archipelago items, and which it starts with.
/// Pure logic with no game dependencies.
/// </summary>
internal static class StandardSkillPolicy
{
    /// <summary>
    /// Reads <c>starting_standard_skill_ids</c> from slot data. Returns null when the seed does not carry the key
    /// (generated before the option existed): such seeds keep every standard skill unlocked, as in the base game,
    /// so an old save is never locked out of skills it already had. A present key, even an empty one, turns gating on.
    /// Ids that are not standard skills are ignored.
    /// </summary>
    public static HashSet<int>? ReadStartingSkills(object? raw, Func<int, bool> isStandardSkill)
    {
        if (raw is null || raw is string || raw is not System.Collections.IEnumerable enumerable)
            return null;
        var result = new HashSet<int>();
        foreach (var element in enumerable)
        {
            try
            {
                var id = Convert.ToInt32(element);
                if (isStandardSkill(id))
                    result.Add(id);
            }
            catch (Exception)
            {
                // Skip anything that is not a number rather than failing the whole connection.
            }
        }
        return result;
    }

    /// <summary>
    /// True when the player may use this standard skill: always when the seed does not gate them
    /// (<paramref name="starting"/> is null), otherwise when it is a starting skill or has been received.
    /// </summary>
    public static bool IsUnlocked(int abilityId, HashSet<int>? starting, IReadOnlySet<int> received) =>
        starting is null || starting.Contains(abilityId) || received.Contains(abilityId);
}

/// <summary>What the multiworld holds at one "Relic Orb N" location, from a silent scout.</summary>
internal sealed class ScoutedOrb
{
    public int Order { get; init; }
    public string ItemName { get; init; } = "";
    public string PlayerName { get; init; } = "";
    public bool IsOwnPlayer { get; init; }

    /// <summary>The Ember Knights relic this item is, when it is one of this slot's own relic items.</summary>
    public int? RelicDefId { get; init; }

    public bool IsOwnRelic => IsOwnPlayer && RelicDefId.HasValue;

    public string Describe() => RelicOrbPolicy.FormatEsperMessage(ItemName, PlayerName, IsOwnPlayer);
}
