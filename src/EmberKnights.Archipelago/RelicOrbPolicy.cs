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
/// Which skills a seed starts with when it uses the starting_skills option. All skills (the 7 standard skills, the 10 tablet
/// skills and, with the DLC, the 10 DLC tablet skills) form one pool; the seed starts with a random few of them and the rest
/// only arrive from the multiworld. Seeds from before the option carry <c>starting_standard_skill_ids</c> instead, which only
/// ever gated the standard skills (the tablet skills were always items), and a seed with neither key leaves the standard
/// skills unlocked as in the base game. Pure logic with no game dependencies.
/// </summary>
internal static class StartingSkillPolicy
{
    /// <summary>
    /// What the seed says about skills. <see cref="Starting"/> is the set of skills the player starts with. When
    /// <see cref="GatesTablets"/> is true the tablet skills are gated too (the new option); otherwise only the standard
    /// skills are, and only when <see cref="Starting"/> is not null (the old option); null means nothing is gated.
    /// </summary>
    public sealed record Mode(HashSet<int>? Starting, bool GatesTablets);

    /// <summary>
    /// Reads the slot data. The new key wins when present. A key that is present, even an empty list, turns gating on.
    /// A value that is not a list is treated as absent. Ids that are not skills of this game are ignored.
    /// </summary>
    public static Mode Read(object? newKey, object? oldKey, Func<int, bool> isSkill, Func<int, bool> isStandard)
    {
        var fromNew = ReadIds(newKey, isSkill);
        if (fromNew is not null)
            return new Mode(fromNew, GatesTablets: true);
        return new Mode(ReadIds(oldKey, isStandard), GatesTablets: false);
    }

    private static HashSet<int>? ReadIds(object? raw, Func<int, bool> isKnown)
    {
        if (raw is null || raw is string || raw is not System.Collections.IEnumerable enumerable)
            return null;
        var result = new HashSet<int>();
        foreach (var element in enumerable)
        {
            try
            {
                var id = Convert.ToInt32(element);
                if (isKnown(id))
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
    /// Whether the plugin decides this skill's unlock state. A tablet skill is always decided by the multiworld; a standard
    /// skill only when the seed gates standard skills at all; a DLC skill only when the DLC is on.
    /// </summary>
    public static bool IsControlled(bool isTablet, bool isStandard, bool isDlcSkill, bool dlcActive, Mode mode) =>
        isTablet || (isDlcSkill && dlcActive) || (isStandard && mode.Starting is not null);

    /// <summary>
    /// Whether the player owns a controlled skill: a starting skill, or one the multiworld has delivered. A starting skill
    /// only counts for the kinds the seed gates, so an old seed's starting list (standard skills only) never unlocks a
    /// tablet skill, which still needs its own item.
    /// </summary>
    public static bool IsUnlocked(int abilityId, bool isTablet, bool isStandard, Mode mode, IReadOnlySet<int> received)
    {
        if (received.Contains(abilityId))
            return true;
        // A seed that does not gate standard skills leaves them free, exactly as the base game does.
        if (isStandard && mode.Starting is null)
            return true;
        if (mode.Starting is null || !mode.Starting.Contains(abilityId))
            return false;
        // A start list only unlocks the kinds the seed gates. The old format lists standard skills only, so it never
        // unlocks a tablet skill; the new format lists any skill.
        return isStandard || mode.GatesTablets;
    }
}

/// <summary>
/// Which relics a seed starts with when it uses the starting_relics option. All relics (the 43 the game unlocks for
/// free, the 53 base orb relics and the 10 DLC orb relics) form one pool; the seed starts with a random few of them and
/// the rest only arrive from the multiworld. Pure logic with no game dependencies.
/// </summary>
internal static class StartingRelicPolicy
{
    /// <summary>
    /// Reads <c>starting_relic_ids</c> from slot data. Returns null when the seed does not carry the key (generated
    /// before the option existed): such seeds leave the 43 default relics unlocked exactly as the base game does, so an
    /// old save is never locked out of relics it already had. A present key, even an empty one, turns gating on.
    /// Ids that are not relics of this game are ignored.
    /// </summary>
    public static HashSet<int>? ReadStartingRelics(object? raw, Func<int, bool> isKnownRelic)
    {
        if (raw is null || raw is string || raw is not System.Collections.IEnumerable enumerable)
            return null;
        var result = new HashSet<int>();
        foreach (var element in enumerable)
        {
            try
            {
                var id = Convert.ToInt32(element);
                if (isKnownRelic(id))
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
    /// Whether the plugin controls a default relic (one of the 43 the game unlocks for free). It does only when the
    /// seed gates them, which is when the slot data carried <c>starting_relic_ids</c> (<paramref name="starting"/> is not null).
    /// </summary>
    public static bool IsGated(bool isDefaultRelic, HashSet<int>? starting) => isDefaultRelic && starting is not null;

    /// <summary>
    /// True when the player owns a relic the seed gates: it is a starting relic or the multiworld has delivered it.
    /// </summary>
    public static bool IsUnlocked(int relicDefId, HashSet<int>? starting, IReadOnlySet<int> received) =>
        (starting is not null && starting.Contains(relicDefId)) || received.Contains(relicDefId);
}

/// <summary>
/// The first shop of a new save is a scripted "FTUE" shop: its slots carry forced items (Apple Pie, Bone Plate and
/// Tainted Conduit) that the game places regardless of what the player owns. With gated relics that would offer relics
/// the seed has not given out, so the forced relic slots are turned into ordinary slots while the plugin is active.
/// The free Apple Pie is a tutorial gift and stays. Pure logic with no game dependencies.
/// </summary>
internal static class FirstShopPolicy
{
    /// <summary>The game's drop type for a relic in a shop slot (<c>eDropType.Relic</c>).</summary>
    public const int RelicDropType = 7;

    /// <summary>
    /// True when a shop slot entry is a scripted relic that has to be turned into a normal one: the entry forces a
    /// specific item (<paramref name="hasFtueOverrides"/>) and that item is a relic. Forced food, potions and gifts
    /// are left alone.
    /// </summary>
    public static bool ShouldClearOverride(bool hasFtueOverrides, int dropType) =>
        hasFtueOverrides && dropType == RelicDropType;
}

/// <summary>
/// Which seed goals a defeated Praxis fight completes. Praxis has two versions: the normal fight, and Arch Praxis, the harder
/// one that only appears once the run's heat is at the Doom threshold (15). A seed with minimum heat 15 or higher therefore
/// never offers the normal fight, so a "Praxis" goal could not be finished at all. Arch Praxis is the same boss at a harder
/// setting, so beating it also completes a Praxis goal; the Arch Praxis goal still needs Arch Praxis itself. Pure logic with
/// no game dependencies.
/// </summary>
internal static class GoalPolicy
{
    public const string Praxis = "praxis";
    public const string ArchPraxis = "arch_praxis";

    /// <summary>
    /// Whether defeating the fight named by <paramref name="defeated"/> completes the seed's <paramref name="goal"/>. An unknown
    /// goal or fight never completes anything, so a bad value cannot report a goal that was not reached.
    /// </summary>
    public static bool Completes(string? defeated, string? goal) => (defeated, goal) switch
    {
        (Praxis, Praxis) => true,
        (ArchPraxis, ArchPraxis) => true,
        // The harder fight also satisfies the easier goal. The reverse never holds.
        (ArchPraxis, Praxis) => true,
        _ => false,
    };
}

/// <summary>
/// The relic selector ("pick one of three") rolls each card's rarity from a table that is about 84% Common, then picks a
/// relic of that rarity the player may be offered. A player who owns few relics, as with a seed that starts with a handful
/// drawn from the whole pool, can run out of Commons and see one or two cards. This tops a short selector back up to three
/// from relics the player owns that every eligibility check has already allowed. Pure logic with no game dependencies.
/// </summary>
internal static class SelectorTopUpPolicy
{
    /// <summary>How many cards a relic selector shows. Observed in play; the screen's own slot count lives in a prefab.</summary>
    public const int TargetCards = 3;

    /// <summary>
    /// Picks which relics to add to a selector that came out short. Candidates are relics the player owns that passed every
    /// check, in the order given; a relic already on offer is never added twice, and the result never exceeds the number of
    /// missing cards. Returns nothing when the selector is already full, so a normal roll is left exactly as the game made it.
    /// </summary>
    public static List<int> ChooseAdditions(
        IReadOnlyCollection<int> alreadyOffered,
        IEnumerable<int> candidates,
        Func<int, int> nextBelow,
        int target = TargetCards)
    {
        var additions = new List<int>();
        var missing = target - alreadyOffered.Count;
        if (missing <= 0)
            return additions;

        var pool = candidates.Where(id => !alreadyOffered.Contains(id)).Distinct().ToList();
        while (additions.Count < missing && pool.Count > 0)
        {
            var index = nextBelow(pool.Count);
            additions.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return additions;
    }

    /// <summary>
    /// Whether a relic may be added to a short selector: the player owns it and the game ran every check on it and none said
    /// no. A relic the game stopped asking about was removed by an earlier check the log does not show, so it is not allowed.
    /// </summary>
    public static bool IsAllowed(bool owned, bool maxOwnedOk, bool unlockedOk, bool weaponOk, bool categoryAskedAndFailed) =>
        owned && maxOwnedOk && unlockedOk && weaponOk && !categoryAskedAndFailed;

    /// <summary>
    /// What the game answered, per check, for one relic during one selector. A check that was never asked is null: the game
    /// stops asking about a relic once an earlier check removed it, so "never asked" for a later check means "already out".
    /// </summary>
    public sealed record Verdicts(bool? MaxOwned, bool? Unlocked, bool? Weapon, bool? Category);

    /// <summary>
    /// True when every check the game ran on the relic passed and it ran all the way to the last one (the weapon check).
    /// A relic the game never finished checking was removed by something earlier, however the checks that did run answered.
    /// </summary>
    public static bool PassedEverything(Verdicts v) =>
        v.MaxOwned == true && v.Unlocked == true && v.Weapon == true && v.Category != false;

    /// <summary>
    /// Every owned relic that passed everything and is not already on offer, in id order so the same inputs give the same list.
    /// </summary>
    public static List<int> Candidates(IReadOnlyDictionary<int, Verdicts> byRelic, Func<int, bool> owned, IReadOnlyCollection<int> alreadyOffered) =>
        byRelic
            .Where(entry => owned(entry.Key) && PassedEverything(entry.Value) && !alreadyOffered.Contains(entry.Key))
            .Select(entry => entry.Key)
            .OrderBy(id => id)
            .ToList();
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
