namespace EmberKnights.Archipelago;

/// <summary>
/// Converts the active reverse-heat value into a deterministic modifier-rank array (length 23)
/// that totals the exact target heat under the game's own orb-cost rules.
/// Supports both sequential (gentle-first) and random-assortment modes.
/// </summary>
public static class HeatSolver
{
    public const int ModRanksLength = 23;
    public const int MaxBaseHeat = 55;
    private const int UnitStepCapacity = 39;

    private readonly record struct UnitStep(int ModId, bool IsBit, int BitIndex);
    private readonly record struct ScheduleStep(int ModId, bool IsBit, int BitIndex, int Cost, bool Backoffable);

    private static readonly UnitStep[] UnitSteps = BuildUnitSteps();

    /// <summary>FNV-1a over the UTF-16 code units: deterministic across processes, unlike string.GetHashCode().</summary>
    public static int StableHash(string text)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var character in text)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return hash;
        }
    }

    public static int EffectiveHeat(int startHeat, int minimumHeat, int reductionPerItem, int reductionsReceived)
    {
        var clampedStart = Math.Clamp(startHeat, 0, MaxBaseHeat);
        var clampedMin = Math.Clamp(minimumHeat, 0, MaxBaseHeat);
        var floor = Math.Min(clampedStart, clampedMin);
        var step = Math.Max(1, reductionPerItem);
        var reduced = clampedStart - Math.Max(0, reductionsReceived) * step;
        return Math.Max(floor, reduced);
    }

    /// <param name="curatedTiers">
    /// When true (and <paramref name="randomAssortment"/> is on), the random schedule is built tier by tier so the
    /// gentler tiers are always used up before a harsher one starts. See <see cref="TierOf"/>.
    /// </param>
    public static int[] SolveRanks(
        int targetHeat,
        bool randomAssortment = false,
        int randomSeed = 0,
        bool curatedTiers = false) =>
        randomAssortment
            ? SolveRandomAssortment(targetHeat, randomSeed, curatedTiers)
            : SolveSequential(targetHeat);

    public const int TierCount = 5;

    /// <summary>
    /// Difficulty tier (1 = gentlest, 5 = harshest) of a single heat step. Levels are the 1-based level or bit
    /// position that the step turns on, so a modifier can sit in two different tiers.
    /// <list type="bullet">
    /// <item>Tier 1: Pantheon.</item>
    /// <item>Tier 2: Malice.</item>
    /// <item>Tier 3: Hulking/Haste/Ruin/Onslaught/Horde levels 1-2, Challenger, Inflation 1, Tyrant.</item>
    /// <item>Tier 4: the same five at levels 3-4, Aficionado, Sealed, Inflation 2.</item>
    /// <item>Tier 5: Doused, Fragile, Blitz.</item>
    /// </list>
    /// </summary>
    public static int TierOf(int modId, int level) => modId switch
    {
        7 => 1,                                   // Pantheon
        6 => 2,                                   // Malice
        1 or 2 or 3 or 4 or 5 => level <= 2 ? 3 : 4,
        8 => 3,                                   // Challenger
        14 => 3,                                  // Tyrant
        12 => level <= 1 ? 3 : 4,                 // Inflation
        15 => 4,                                  // Aficionado
        11 => 4,                                  // Sealed
        9 or 10 or 13 => 5,                       // Doused, Fragile, Blitz
        _ => throw new ArgumentOutOfRangeException(nameof(modId), modId, "Modifier has no heat tier."),
    };

    private static int[] SolveSequential(int targetHeat)
    {
        var ranks = new int[ModRanksLength];
        var clamped = Math.Clamp(targetHeat, 0, MaxBaseHeat);
        if (clamped == 0)
            return ranks;

        int unitStepCount;
        if (clamped <= UnitStepCapacity)
        {
            unitStepCount = clamped;
        }
        else
        {
            var extra = clamped - UnitStepCapacity;
            int bigCost;
            if (extra <= 2)
            {
                ranks[15] = 1; // Aficionado level 1 (+2)
                bigCost = 2;
            }
            else if (extra == 3)
            {
                ranks[15] = 2; // Aficionado level 2 (+3)
                bigCost = 3;
            }
            else if (extra == 4)
            {
                ranks[15] = 3; // Aficionado level 3 (+4)
                bigCost = 4;
            }
            else if (extra <= 7)
            {
                ranks[15] = 3;
                ranks[8] = 1; // Challenger (+3)
                bigCost = 7;
            }
            else if (extra <= 10)
            {
                ranks[15] = 3;
                ranks[8] = 1;
                ranks[9] = 1; // Doused (+3)
                bigCost = 10;
            }
            else if (extra <= 13)
            {
                ranks[15] = 3;
                ranks[8] = 1;
                ranks[9] = 1;
                ranks[11] = 1; // Sealed (+3)
                bigCost = 13;
            }
            else
            {
                ranks[15] = 3;
                ranks[8] = 1;
                ranks[9] = 1;
                ranks[11] = 1;
                ranks[10] = 1; // Fragile (+3)
                bigCost = 16;
            }

            unitStepCount = UnitStepCapacity - (bigCost - extra);
        }

        for (var i = 0; i < unitStepCount; i++)
        {
            var step = UnitSteps[i];
            if (step.IsBit)
                ranks[step.ModId] |= 1 << step.BitIndex;
            else
                ranks[step.ModId]++;
        }

        return ranks;
    }

    private static int[] SolveRandomAssortment(int targetHeat, int randomSeed, bool curatedTiers)
    {
        var ranks = new int[ModRanksLength];
        var clamped = Math.Clamp(targetHeat, 0, MaxBaseHeat);
        if (clamped == 0)
            return ranks;

        var schedule = curatedTiers ? BuildCuratedSchedule(randomSeed) : BuildRandomSchedule(randomSeed);
        var sum = 0;
        var endIndex = 0;
        for (var i = 0; i < schedule.Length; i++)
        {
            sum += schedule[i].Cost;
            endIndex = i;
            if (sum >= clamped)
                break;
        }

        var overshoot = sum - clamped;
        var active = new bool[endIndex + 1];
        Array.Fill(active, true);
        for (var i = endIndex - 1; i >= 0 && overshoot > 0; i--)
        {
            if (schedule[i].Backoffable)
            {
                active[i] = false;
                overshoot--;
            }
        }

        for (var i = 0; i <= endIndex; i++)
        {
            if (!active[i])
                continue;
            var step = schedule[i];
            if (step.IsBit)
                ranks[step.ModId] |= 1 << step.BitIndex;
            else
                ranks[step.ModId]++;
        }

        return ranks;
    }

    /// <summary>
    /// Tier-ordered schedule: every step of tier 1 comes before any step of tier 2, and so on. Inside a tier the
    /// steps are shuffled, except that the steps of one modifier keep their order (level 2 never comes before
    /// level 1, and bit n+1 never before bit n), so a modifier's levels always fill in from the bottom.
    /// </summary>
    private static ScheduleStep[] BuildCuratedSchedule(int randomSeed)
    {
        var rng = new Random(randomSeed == 0 ? 0x5A17C3D9 : randomSeed);
        var schedule = new List<ScheduleStep>(46);

        for (var tier = 1; tier <= TierCount; tier++)
        {
            var steps = CuratedSteps().Where(entry => entry.Tier == tier).Select(entry => entry.Step).ToList();

            // Shuffle which modifier each slot belongs to, then hand that modifier's steps out in their own order.
            var queues = steps
                .GroupBy(step => step.ModId)
                .ToDictionary(group => group.Key, group => new Queue<ScheduleStep>(group));
            var slots = steps.Select(step => step.ModId).ToList();
            for (var i = slots.Count - 1; i > 0; i--)
            {
                var swapIndex = rng.Next(i + 1);
                (slots[i], slots[swapIndex]) = (slots[swapIndex], slots[i]);
            }

            foreach (var modId in slots)
                schedule.Add(queues[modId].Dequeue());
        }

        EnsureBackoffRoom(schedule);
        return schedule.ToArray();
    }

    /// <summary>
    /// A +2 or +3 step can only land on every in-between heat if enough +1 steps come before it to be backed off.
    /// The schedule's first two steps are therefore always +1 steps; tier 1 and 2 are all +1 steps (7 of them), so this
    /// already holds, and the check guards the invariant against future tier changes.
    /// </summary>
    private static void EnsureBackoffRoom(List<ScheduleStep> schedule)
    {
        var ones = 0;
        foreach (var step in schedule)
        {
            if (step.Cost > 1 && ones < step.Cost - 1)
                throw new InvalidOperationException(
                    $"Curated heat schedule has a +{step.Cost} step after only {ones} backoffable +1 steps.");
            if (step.Cost == 1)
                ones++;
        }
    }

    private readonly record struct CuratedStep(int Tier, ScheduleStep Step);

    private static IEnumerable<CuratedStep> CuratedSteps()
    {
        // Enemy stat modifiers: levels 1-4, +1 each.
        foreach (var modId in new[] { 1, 2, 3, 4, 5 })
        {
            for (var level = 1; level <= 4; level++)
                yield return new CuratedStep(TierOf(modId, level), new ScheduleStep(modId, false, 0, 1, true));
        }

        // Bit modifiers, +1 per bit: Malice 3, Pantheon 4, Inflation 2, Tyrant 5.
        foreach (var (modId, bits) in new[] { (6, 3), (7, 4), (12, 2), (14, 5) })
        {
            for (var bit = 0; bit < bits; bit++)
                yield return new CuratedStep(TierOf(modId, bit + 1), new ScheduleStep(modId, true, bit, 1, true));
        }

        // Blitz: levels 1-5, +1 each.
        for (var level = 1; level <= 5; level++)
            yield return new CuratedStep(TierOf(13, level), new ScheduleStep(13, false, 0, 1, true));

        // Toggles, +3 each, never backed off.
        foreach (var modId in new[] { 8, 9, 10, 11 })
            yield return new CuratedStep(TierOf(modId, 1), new ScheduleStep(modId, false, 0, 3, false));

        // Aficionado levels cost 2, 3, 4 in total, so the steps add +2, +1, +1.
        yield return new CuratedStep(TierOf(15, 1), new ScheduleStep(15, false, 0, 2, false));
        yield return new CuratedStep(TierOf(15, 2), new ScheduleStep(15, false, 0, 1, false));
        yield return new CuratedStep(TierOf(15, 3), new ScheduleStep(15, false, 0, 1, false));
    }

    private static ScheduleStep[] BuildRandomSchedule(int randomSeed)
    {
        var rng = new Random(randomSeed == 0 ? 0x5A17C3D9 : randomSeed);
        var mainUnits = UnitSteps.ToArray();
        var trailingBlitzUnits = Array.Empty<UnitStep>();

        for (var i = mainUnits.Length - 1; i > 0; i--)
        {
            var swapIndex = rng.Next(i + 1);
            (mainUnits[i], mainUnits[swapIndex]) = (mainUnits[swapIndex], mainUnits[i]);
        }

        var toggles = new[] { 8, 9, 10, 11 };
        for (var i = toggles.Length - 1; i > 0; i--)
        {
            var swapIndex = rng.Next(i + 1);
            (toggles[i], toggles[swapIndex]) = (toggles[swapIndex], toggles[i]);
        }

        // First two steps are always +1 backoffable unit steps so any +2 or +3 step later in the
        // schedule can be hit at every intermediate integer heat by backing off 1 or 2 unit steps.
        var schedule = new ScheduleStep[46];
        schedule[0] = ToScheduleStep(mainUnits[0]);
        schedule[1] = ToScheduleStep(mainUnits[1]);

        var middleLength = (mainUnits.Length - 2) + 4 + 3; // remaining main unit steps + 4 toggles + 3 Aficionado levels
        var slotIndices = Enumerable.Range(0, middleLength).ToArray();
        for (var i = slotIndices.Length - 1; i > 0; i--)
        {
            var swapIndex = rng.Next(i + 1);
            (slotIndices[i], slotIndices[swapIndex]) = (slotIndices[swapIndex], slotIndices[i]);
        }

        var middle = new ScheduleStep?[middleLength];
        for (var i = 0; i < 4; i++)
            middle[slotIndices[i]] = new ScheduleStep(toggles[i], false, 0, 3, false);

        var aficionadoSlots = new[] { slotIndices[4], slotIndices[5], slotIndices[6] };
        Array.Sort(aficionadoSlots);
        middle[aficionadoSlots[0]] = new ScheduleStep(15, false, 0, 2, false);
        middle[aficionadoSlots[1]] = new ScheduleStep(15, false, 0, 1, false);
        middle[aficionadoSlots[2]] = new ScheduleStep(15, false, 0, 1, false);

        var unitCursor = 2;
        for (var i = 0; i < middleLength; i++)
            schedule[i + 2] = middle[i] ?? ToScheduleStep(mainUnits[unitCursor++]);

        for (var i = 0; i < trailingBlitzUnits.Length; i++)
            schedule[2 + middleLength + i] = ToScheduleStep(trailingBlitzUnits[i]);

        return schedule;
    }

    private static ScheduleStep ToScheduleStep(UnitStep step) =>
        new(step.ModId, step.IsBit, step.BitIndex, 1, true);

    public static int CalculateTotalHeat(IReadOnlyList<int> modRanks, CatalogData catalog)
    {
        var total = 0;
        foreach (var mod in catalog.Heat.Modifiers)
        {
            if (mod.Id < 0 || mod.Id >= modRanks.Count)
                continue;
            var stored = modRanks[mod.Id];
            if (stored <= 0)
                continue;

            if (mod.Kind == "level")
            {
                var levelIndex = Math.Clamp(stored - 1, 0, mod.Costs.Length - 1);
                total += mod.Costs[levelIndex];
            }
            else
            {
                for (var bit = 0; bit < mod.Costs.Length; bit++)
                {
                    if ((stored & (1 << bit)) != 0)
                        total += mod.Costs[bit];
                }
            }
        }
        return total;
    }

    private static UnitStep[] BuildUnitSteps()
    {
        var steps = new List<UnitStep>(UnitStepCapacity);

        // 1..20: round-robin the five 4-rank enemy stat/spawn modifiers (+1 per level).
        var statMods = new[] { 1, 2, 4, 5, 3 }; // Hulking, Haste, Onslaught, Horde, Ruin
        for (var level = 0; level < 4; level++)
        {
            foreach (var modId in statMods)
                steps.Add(new UnitStep(modId, false, 0));
        }

        // 21..24: Pantheon (boss variants, 4 bits at +1 each).
        for (var bit = 0; bit < 4; bit++)
            steps.Add(new UnitStep(7, true, bit));

        // 25..27: Malice (deadlier enemies, 3 bits at +1 each).
        for (var bit = 0; bit < 3; bit++)
            steps.Add(new UnitStep(6, true, bit));

        // 28..29: Inflation (shop modifier, 2 bits at +1 each).
        for (var bit = 0; bit < 2; bit++)
            steps.Add(new UnitStep(12, true, bit));

        // 30..34: Tyrant / PraxisEffects (5 bits at +1 each).
        for (var bit = 0; bit < 5; bit++)
            steps.Add(new UnitStep(14, true, bit));

        // 35..39: Blitz (world timer, 5 levels at +1 each).
        for (var level = 0; level < 5; level++)
            steps.Add(new UnitStep(13, false, 0));

        return steps.ToArray();
    }
}
