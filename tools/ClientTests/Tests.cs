using Xunit;

namespace EmberKnights.Archipelago.Tests;

public class RoomProgressTrackerTests
{
    private const int Battle = 2;
    private const int Start = 1;
    private const int LevelExit = 16;

    [Fact]
    public void EnteringTheFirstRoomOfAWorldIsDepthOne()
    {
        var tracker = new RoomProgressTracker();
        Assert.Equal(1, tracker.Enter(areaName: 3, roomInstanceId: 100, roomType: Start));
    }

    [Fact]
    public void DepthCountsDistinctRoomsOnly()
    {
        var tracker = new RoomProgressTracker();
        tracker.Enter(1, 10, Start);
        tracker.Enter(1, 11, Battle);
        Assert.Equal(2, tracker.Enter(1, 10, Start)); // backtracking is not progress
        Assert.Equal(3, tracker.Enter(1, 12, Battle));
    }

    [Fact]
    public void ChangingWorldRestartsDepth()
    {
        var tracker = new RoomProgressTracker();
        tracker.Enter(1, 10, Start);
        tracker.Enter(1, 11, Battle);
        Assert.Equal(1, tracker.Enter(2, 10, Start)); // same instance id, different world
    }

    [Fact]
    public void ReturningToTheNexusBetweenRunsRestartsDepthInTheSameWorld()
    {
        var tracker = new RoomProgressTracker();
        tracker.Enter(2, 10, Start);
        tracker.Enter(2, 11, Battle);
        tracker.Enter(RoomProgressTracker.NexusArea, 1, Battle);
        Assert.Equal(1, tracker.Enter(2, 10, Start)); // new run, room ids repeat
    }

    [Theory]
    [InlineData(0)]
    [InlineData(RoomProgressTracker.NexusArea)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(14)]
    public void AreasOutsideTheFiveBaseWorldsNeverCount(int area)
    {
        var tracker = new RoomProgressTracker();
        Assert.Equal(0, tracker.Enter(area, 1, Battle));
        Assert.Equal(0, tracker.Enter(area, 2, Battle));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(LevelExit)]
    public void TransitionRoomsDoNotCount(int roomType)
    {
        var tracker = new RoomProgressTracker();
        Assert.Equal(0, tracker.Enter(1, 1, roomType));
    }

    [Fact]
    public void AnUnexpectedRoomTypeStillCountsSoMilestonesStayReachable()
    {
        var tracker = new RoomProgressTracker();
        Assert.Equal(1, tracker.Enter(5, 1, roomType: 11)); // PraxisHub
        Assert.Equal(2, tracker.Enter(5, 2, roomType: 15)); // PraxisFakeStart
    }

    [Fact]
    public void ResetStartsFromScratch()
    {
        var tracker = new RoomProgressTracker();
        tracker.Enter(3, 1, Start);
        tracker.Enter(3, 2, Battle);
        tracker.Reset();
        Assert.Equal(0, tracker.Depth);
        Assert.Equal(1, tracker.Enter(3, 1, Start));
    }
}

public class CatalogTests
{
    private static readonly CatalogData Catalog = GameCatalog.Data;

    [Fact]
    public void LoadsExpectedCounts()
    {
        Assert.Equal(10, Catalog.Skills.Count);
        Assert.Equal(16, Catalog.Minibosses.Count);
        Assert.Equal(14, Catalog.Bosses.Count);
        Assert.Equal(30, Catalog.EmberTree.Count);
        Assert.Equal(87, Catalog.WeaponMods.Count);
        Assert.Equal(18, Catalog.RoomMilestones.Count);
        Assert.Equal(53, Catalog.RelicOrbs.Count);
    }

    [Fact]
    public void RelicOrbIdsMatchTheSharedScheme()
    {
        Assert.Equal(Catalog.IdBase + 7000, Catalog.RelicOrbLocationId(0));
        Assert.Equal(Catalog.IdBase + 7052, Catalog.RelicOrbLocationId(52));
        Assert.Equal(Catalog.IdBase + 300, Catalog.RelicItemId(0));
        Assert.Equal(Catalog.IdBase + 352, Catalog.RelicItemId(52));
    }

    [Fact]
    public void RelicOrbsAreNumberedInTheGamesUnlockOrder()
    {
        Assert.Equal(Enumerable.Range(1, 53), Catalog.RelicOrbs.Select(orb => orb.Order));
        Assert.Equal(53, Catalog.RelicOrbs.Select(orb => orb.DefId).Distinct().Count());
        Assert.Equal((48, "Spirit Shelter"), (Catalog.RelicOrbs[0].DefId, Catalog.RelicOrbs[0].Name));
        Assert.Equal((183, "Eye of the Storm"), (Catalog.RelicOrbs[52].DefId, Catalog.RelicOrbs[52].Name));
        Assert.Equal("Relic Orb 01", Catalog.RelicOrbs[0].LocationName);
        Assert.Equal("Relic Orb 53", Catalog.RelicOrbs[52].LocationName);
    }

    [Fact]
    public void OnlyTheFiftyThreeBaseOrbRelicsAreRandomized()
    {
        Assert.All(Catalog.RelicOrbs, orb => Assert.True(Catalog.IsOrbRelic(orb.DefId)));
        // DLC orb relics, default-unlocked relics and the Bob jar relics are never controlled.
        foreach (var other in new[] { 1, 2, 144, 145, 161, 211, 214, 220, 9999 })
            Assert.False(Catalog.IsOrbRelic(other), $"relic {other}");
    }

    [Fact]
    public void RelicItemsResolveByIdAndRetiredRangesDoNot()
    {
        for (var i = 0; i < 53; i++)
            Assert.Equal(Catalog.RelicOrbs[i].DefId, Catalog.RelicForItem(Catalog.RelicItemId(i))!.DefId);
        Assert.Null(Catalog.RelicForItem(Catalog.RelicItemId(53)));
        Assert.Null(Catalog.RelicForItem(Catalog.IdBase + 299));
        // A relic item id must never be mistaken for a skill or filler.
        Assert.Null(Catalog.SkillForItem(Catalog.RelicItemId(0)));
        Assert.Null(Catalog.FillerForItem(Catalog.RelicItemId(0)));
        Assert.Equal("Spirit Shelter", Catalog.GetItemName(Catalog.RelicItemId(0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(54)]
    [InlineData(1000)]
    public void OrbsBeyondTheRandomizedRangeHaveNoLocation(int order)
    {
        Assert.Null(Catalog.LocationForRelicOrb(order));
    }

    [Theory]
    [InlineData(1, 7000)]
    [InlineData(2, 7001)]
    [InlineData(53, 7052)]
    public void TheNthOrbMapsToTheNthLocation(int order, int offset)
    {
        Assert.Equal(Catalog.IdBase + offset, Catalog.LocationForRelicOrb(order));
        Assert.Equal($"Relic Orb {order:D2}", Catalog.GetLocationName(Catalog.LocationForRelicOrb(order)!.Value));
    }

    [Fact]
    public void LocationIdsMatchTheSharedScheme()
    {
        var b = Catalog.IdBase;
        Assert.Equal(b + 1000, Catalog.SkillLocationId(0));
        Assert.Equal(b + 2000, Catalog.MinibossLocationId(0));
        Assert.Equal(b + 3000, Catalog.BossLocationId(0));
        Assert.Equal(b + 4000, Catalog.EmberTreeLocationId(0));
        Assert.Equal(b + 5000, Catalog.WeaponModLocationId(0));
        Assert.Equal(b + 6000, Catalog.RoomMilestoneLocationId(0));
    }

    [Fact]
    public void EveryEmberTreeNodeAndModMapsToItsOwnLocation()
    {
        var emberLocations = Catalog.EmberTree.Select(n => Catalog.LocationForEmberTreeNode(n.DefId)).ToList();
        Assert.All(emberLocations, id => Assert.NotNull(id));
        Assert.Equal(emberLocations.Count, emberLocations.Distinct().Count());

        var modLocations = Catalog.WeaponMods.Select(m => Catalog.LocationForWeaponMod(m.DefId)).ToList();
        Assert.All(modLocations, id => Assert.NotNull(id));
        Assert.Equal(modLocations.Count, modLocations.Distinct().Count());
    }

    [Fact]
    public void DefaultAndDisabledAndDlcDefinitionsProduceNoCheck()
    {
        // Default-unlocked mods: Honed, Quickness, Rapid Shot, Skillful, Crush, Charge Engine,
        // Empowered, Critical Slash, Unstable Rift, Dark Spell: Dark Star.
        foreach (var defaultMod in new[] { 1, 10, 13, 19, 28, 31, 44, 52, 81, 91 })
            Assert.Null(Catalog.LocationForWeaponMod(defaultMod));
        // DLC fist and gun mods.
        foreach (var dlcMod in new[] { 98, 107, 108, 117 })
            Assert.Null(Catalog.LocationForWeaponMod(dlcMod));
        // Disabled Ember Tree nodes: Elite Slayer, Rare Hunter, Stat Dice.
        foreach (var disabledNode in new[] { 11, 13, 16 })
            Assert.Null(Catalog.LocationForEmberTreeNode(disabledNode));
        Assert.Null(Catalog.LocationForEmberTreeNode(9999));
    }

    [Fact]
    public void LocationNamesAreUniqueAcrossAllGroups()
    {
        var ids = new List<long>();
        ids.AddRange(Enumerable.Range(0, Catalog.Skills.Count).Select(Catalog.SkillLocationId));
        ids.AddRange(Enumerable.Range(0, Catalog.Minibosses.Count).Select(Catalog.MinibossLocationId));
        ids.AddRange(Enumerable.Range(0, Catalog.Bosses.Count).Select(Catalog.BossLocationId));
        ids.AddRange(Enumerable.Range(0, Catalog.EmberTree.Count).Select(Catalog.EmberTreeLocationId));
        ids.AddRange(Enumerable.Range(0, Catalog.WeaponMods.Count).Select(Catalog.WeaponModLocationId));
        ids.AddRange(Enumerable.Range(0, Catalog.RoomMilestones.Count).Select(Catalog.RoomMilestoneLocationId));
        ids.AddRange(Enumerable.Range(0, Catalog.RelicOrbs.Count).Select(Catalog.RelicOrbLocationId));

        Assert.Equal(ids.Count, ids.Distinct().Count());
        var names = ids.Select(Catalog.GetLocationName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.DoesNotContain(names, name => name.StartsWith("Location "));
    }

    [Fact]
    public void MilestonesReportedForDepthAreCumulativeAndPerWorld()
    {
        Assert.Empty(Catalog.MilestonesFor(areaName: 3, roomsEntered: 0));
        Assert.Equal(new[] { 1 }, Catalog.MilestonesFor(3, 1).Select(m => m.Room));
        Assert.Equal(new[] { 1, 3 }, Catalog.MilestonesFor(3, 4).Select(m => m.Room));
        Assert.Equal(new[] { 1, 3, 5, 7 }, Catalog.MilestonesFor(3, 50).Select(m => m.Room));
        Assert.All(Catalog.MilestonesFor(3, 50), m => Assert.Equal("Wightsbury", m.World));
        // Netherra only has two milestones.
        Assert.Equal(new[] { 1, 3 }, Catalog.MilestonesFor(5, 50).Select(m => m.Room));
        // Areas that are not base worlds have none.
        Assert.Empty(Catalog.MilestonesFor(6, 50));
    }

    [Fact]
    public void WorldOneRoomOneMatchesTheDocumentedLocationName()
    {
        var milestone = Catalog.MilestonesFor(3, 1).Single();
        Assert.Equal("Wightsbury Room 1 Reached", milestone.LocationName);
        Assert.Equal("Wightsbury Room 1 Reached", Catalog.GetLocationName(Catalog.LocationForRoomMilestone(milestone)));
    }

    [Fact]
    public void WeaponItemIdsResolveAndSwordStaysAlwaysUnlocked()
    {
        Assert.Equal(6, Catalog.Weapons.Count);
        Assert.False(Catalog.IsRandomizedWeapon(1)); // Sword is always active
        for (var i = 0; i < Catalog.Weapons.Count; i++)
        {
            var itemId = Catalog.WeaponItemId(i);
            Assert.Equal(Catalog.Weapons[i].WeaponId, Catalog.WeaponForItem(itemId)!.WeaponId);
            Assert.True(Catalog.IsRandomizedWeapon(Catalog.Weapons[i].WeaponId));
            Assert.Null(Catalog.SkillForItem(itemId));
            Assert.Null(Catalog.FillerForItem(itemId));
        }
    }

    [Fact]
    public void HeatReductionItemHasDedicatedIdAndName()
    {
        Assert.Equal(Catalog.IdBase + 400, Catalog.HeatReductionItemId);
        Assert.True(Catalog.IsHeatReductionItem(Catalog.HeatReductionItemId));
        Assert.False(Catalog.IsHeatReductionItem(Catalog.SkillItemId(0)));
        Assert.False(Catalog.IsHeatReductionItem(Catalog.RelicItemId(0)));
        Assert.Equal(CatalogData.HeatReductionItemName, Catalog.GetItemName(Catalog.HeatReductionItemId));
    }

    [Fact]
    public void ArchipelagoCoinBundlesAndEmberTreeRequirementsAreValid()
    {
        var coins = Catalog.Fillers.Where(filler => filler.Kind == "coin").ToList();
        Assert.Equal(3, coins.Count);
        Assert.Equal(new[] { 1, 2, 3 }, coins.Select(coin => coin.Amount));
        Assert.Equal(new[] { "1 Archipelago Coin", "2 Archipelago Coins", "3 Archipelago Coins" }, coins.Select(coin => coin.Name));

        var thickSkin = Catalog.EmberTree.Single(node => node.DefId == 1);
        Assert.Equal(1, thickSkin.RequiredCoins);
        Assert.All(Catalog.EmberTree.Where(node => node.DefId != 1), node =>
        {
            Assert.InRange(node.Stage, 1, 10);
            Assert.InRange(node.RequiredCoins, 3, 37);
        });

        var traps = Catalog.Fillers.Where(filler => filler.Kind.StartsWith("trap_")).ToList();
        Assert.Equal(3, traps.Count);
        Assert.Equal(
            new[] { "Enemy Ambush Trap", "Boss Ambush Trap", "Praxis Arena Trap" },
            traps.Select(trap => trap.Name));
    }
}

public class HeatSolverTests
{
    private static readonly CatalogData Catalog = GameCatalog.Data;

    [Fact]
    public void EveryTargetFromZeroToMaxProducesTheExactHeat()
    {
        for (var target = 0; target <= HeatSolver.MaxBaseHeat; target++)
        {
            var ranks = HeatSolver.SolveRanks(target);
            Assert.Equal(HeatSolver.ModRanksLength, ranks.Length);
            Assert.Equal(0, ranks[0]);
            Assert.Equal(target, HeatSolver.CalculateTotalHeat(ranks, Catalog));
        }
    }

    [Fact]
    public void RandomAssortmentProducesTheExactHeatAcrossSeedsAndDiffersBetweenSeeds()
    {
        for (var seed = 1; seed <= 100; seed++)
        {
            for (var target = 0; target <= HeatSolver.MaxBaseHeat; target++)
            {
                var ranks = HeatSolver.SolveRanks(target, randomAssortment: true, randomSeed: seed * 7919);
                Assert.Equal(target, HeatSolver.CalculateTotalHeat(ranks, Catalog));
                for (var modId = 16; modId < ranks.Length; modId++)
                    Assert.Equal(0, ranks[modId]);
            }
        }

        var assortmentA = HeatSolver.SolveRanks(20, randomAssortment: true, randomSeed: 101);
        var assortmentB = HeatSolver.SolveRanks(20, randomAssortment: true, randomSeed: 202);
        var sequential = HeatSolver.SolveRanks(20, randomAssortment: false, randomSeed: 101);
        Assert.False(assortmentA.SequenceEqual(assortmentB));
        Assert.False(assortmentA.SequenceEqual(sequential));
    }

    [Fact]
    public void FixedSeedOnlyEverRemovesDebuffsAsHeatDrops()
    {
        // "Roll once" mode keeps the seed constant, so lowering the heat must never turn on a modifier that was
        // off at the higher heat, nor raise any modifier's level. Aficionado's three steps (+2, +1, +1) are exempt
        // from the per-modifier comparison only because they are applied as a group; they must still not grow.
        for (var seed = 1; seed <= 100; seed++)
        {
            for (var curated = 0; curated <= 1; curated++)
            {
                for (var heat = 1; heat <= HeatSolver.MaxBaseHeat; heat++)
                {
                    var higher = HeatSolver.SolveRanks(heat, true, seed * 104729, curated == 1);
                    var lower = HeatSolver.SolveRanks(heat - 1, true, seed * 104729, curated == 1);
                    var toggleAdded = new[] { 8, 9, 10, 11 }.Any(id => lower[id] > higher[id]);
                    Assert.False(toggleAdded, $"seed {seed}, heat {heat}: a toggle appeared when heat dropped");
                    Assert.True(
                        lower[15] <= higher[15],
                        $"seed {seed}, heat {heat}: Aficionado grew when heat dropped");
                }
            }
        }
    }

    [Fact]
    public void ChangingTheSeedChangesTheAssortmentButTheSameSeedIsRepeatable()
    {
        var first = HeatSolver.SolveRanks(30, true, 12345, false);
        var again = HeatSolver.SolveRanks(30, true, 12345, false);
        var other = HeatSolver.SolveRanks(30, true, 54321, false);
        Assert.Equal(first, again);
        Assert.False(first.SequenceEqual(other));
    }

    [Fact]
    public void StableHashIsDeterministicAndDistinguishesSeeds()
    {
        // These constants are FNV-1a values; if they ever change, "roll once" seeds would silently re-roll.
        Assert.Equal(HeatSolver.StableHash("abc"), HeatSolver.StableHash("abc"));
        Assert.NotEqual(HeatSolver.StableHash("09096303671302924074"), HeatSolver.StableHash("03537140675882381717"));
        Assert.Equal(unchecked((int)0xE40C292C), HeatSolver.StableHash("a"));
    }

    // Tier of each heat step, for the curated schedule. The solver does not expose its schedule, so the tests rebuild
    // the order from the ranks: a modifier's level N was turned on by the Nth step of that modifier.
    private static readonly (int ModId, string Name)[] TieredMods =
    {
        (7, "Pantheon"), (6, "Malice"), (1, "Hulking"), (2, "Haste"), (3, "Ruin"), (4, "Onslaught"), (5, "Horde"),
        (8, "Challenger"), (14, "Tyrant"), (12, "Inflation"), (15, "Aficionado"), (11, "Sealed"),
        (9, "Doused"), (10, "Fragile"), (13, "Blitz"),
    };

    private static int LevelsOn(int[] ranks, int modId)
    {
        var mod = Catalog.Heat.Modifiers.Single(m => m.Id == modId);
        var stored = ranks[modId];
        if (mod.Kind == "level")
            return stored;
        var bits = 0;
        for (var bit = 0; bit < mod.Costs.Length; bit++)
            if ((stored & (1 << bit)) != 0)
                bits++;
        return bits;
    }

    /// <summary>Highest tier that has any active step at this heat, and the lowest tier that still has a step missing.</summary>
    private static (int HighestUsed, int LowestIncomplete) TierSpan(int[] ranks)
    {
        var highestUsed = 0;
        var lowestIncomplete = HeatSolver.TierCount + 1;
        foreach (var (modId, _) in TieredMods)
        {
            var mod = Catalog.Heat.Modifiers.Single(m => m.Id == modId);
            var maxLevels = mod.Kind == "toggle" ? 1 : mod.Costs.Length;
            var on = LevelsOn(ranks, modId);
            for (var level = 1; level <= maxLevels; level++)
            {
                var tier = HeatSolver.TierOf(modId, level);
                if (level <= on)
                    highestUsed = Math.Max(highestUsed, tier);
                else
                    lowestIncomplete = Math.Min(lowestIncomplete, tier);
            }
        }
        return (highestUsed, lowestIncomplete);
    }

    [Fact]
    public void CuratedTiersMatchTheDesignedAssignment()
    {
        // Tier 1
        for (var level = 1; level <= 4; level++) Assert.Equal(1, HeatSolver.TierOf(7, level));          // Pantheon
        // Tier 2
        for (var level = 1; level <= 3; level++) Assert.Equal(2, HeatSolver.TierOf(6, level));          // Malice
        // Tier 3 and 4: the five stat modifiers split by level
        foreach (var modId in new[] { 1, 2, 3, 4, 5 })
        {
            Assert.Equal(3, HeatSolver.TierOf(modId, 1));
            Assert.Equal(3, HeatSolver.TierOf(modId, 2));
            Assert.Equal(4, HeatSolver.TierOf(modId, 3));
            Assert.Equal(4, HeatSolver.TierOf(modId, 4));
        }
        Assert.Equal(3, HeatSolver.TierOf(8, 1));                                                       // Challenger
        for (var bit = 1; bit <= 5; bit++) Assert.Equal(3, HeatSolver.TierOf(14, bit));                 // Tyrant
        Assert.Equal(3, HeatSolver.TierOf(12, 1));                                                      // Inflation 1
        Assert.Equal(4, HeatSolver.TierOf(12, 2));                                                      // Inflation 2
        for (var level = 1; level <= 3; level++) Assert.Equal(4, HeatSolver.TierOf(15, level));         // Aficionado
        Assert.Equal(4, HeatSolver.TierOf(11, 1));                                                      // Sealed
        // Tier 5
        Assert.Equal(5, HeatSolver.TierOf(9, 1));                                                       // Doused
        Assert.Equal(5, HeatSolver.TierOf(10, 1));                                                      // Fragile
        for (var level = 1; level <= 5; level++) Assert.Equal(5, HeatSolver.TierOf(13, level));         // Blitz
    }

    [Fact]
    public void EveryHeatModifierThatCanBeRolledHasATier()
    {
        foreach (var (modId, name) in TieredMods)
        {
            var mod = Catalog.Heat.Modifiers.Single(m => m.Id == modId);
            Assert.Equal(name, mod.Name);
            var levels = mod.Kind == "toggle" ? 1 : mod.Costs.Length;
            for (var level = 1; level <= levels; level++)
                Assert.InRange(HeatSolver.TierOf(modId, level), 1, HeatSolver.TierCount);
        }
    }

    [Fact]
    public void CuratedTiersHitEveryExactHeatForEverySeed()
    {
        for (var seed = 1; seed <= 300; seed++)
        {
            for (var target = 0; target <= HeatSolver.MaxBaseHeat; target++)
            {
                var ranks = HeatSolver.SolveRanks(target, randomAssortment: true, randomSeed: seed * 3571, curatedTiers: true);
                Assert.Equal(target, HeatSolver.CalculateTotalHeat(ranks, Catalog));
                for (var modId = 16; modId < ranks.Length; modId++)
                    Assert.Equal(0, ranks[modId]);
            }
        }
    }

    [Fact]
    public void ALowerTierIsFilledBeforeAHigherOneStarts()
    {
        // A step of tier N may only be active once every step of tiers below N is active, with one allowance: the
        // solver backs a single big step (+2/+3) off by dropping earlier +1 steps, so a tier can be at most as
        // incomplete as that backoff allows. At the heat values where whole tiers are full, the rule must be exact.
        // Cumulative heat at the end of each tier is 4, 7, 26, 44 and 55.
        foreach (var seed in Enumerable.Range(1, 200))
        {
            foreach (var (heat, tier) in new[] { (4, 1), (7, 2), (26, 3), (44, 4), (55, 5) })
            {
                var ranks = HeatSolver.SolveRanks(heat, true, seed * 3571, true);
                var (highestUsed, lowestIncomplete) = TierSpan(ranks);
                Assert.True(highestUsed <= tier, $"seed {seed}, heat {heat}: tier {highestUsed} is in use before tier {tier} is full");
                if (heat < HeatSolver.MaxBaseHeat)
                    Assert.True(lowestIncomplete > tier, $"seed {seed}, heat {heat}: tier {lowestIncomplete} is still incomplete");
            }
        }
    }

    [Fact]
    public void LowHeatOnlyEverContainsTheMildestTiers()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            for (var heat = 1; heat <= 7; heat++)
            {
                var ranks = HeatSolver.SolveRanks(heat, true, seed * 3571, true);
                Assert.True(TierSpan(ranks).HighestUsed <= 2, $"seed {seed}, heat {heat}: a tier 3+ modifier is active");
            }

            // Blitz, Doused and Fragile are tier 5 and may not appear before heat 45.
            for (var heat = 0; heat <= 44; heat++)
            {
                var ranks = HeatSolver.SolveRanks(heat, true, seed * 3571, true);
                Assert.Equal(0, ranks[13]);
                Assert.Equal(0, ranks[9]);
                Assert.Equal(0, ranks[10]);
            }
        }
    }

    [Fact]
    public void CuratedHeatKeepsTheFirstFourteenHeatPointsOutOfTheStatModifiers()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var ranks = HeatSolver.SolveRanks(7, true, seed * 3571, true);
            Assert.Equal(4, LevelsOn(ranks, 7));   // all of Pantheon
            Assert.Equal(3, LevelsOn(ranks, 6));   // all of Malice
            foreach (var modId in new[] { 1, 2, 3, 4, 5, 14, 12, 8, 15, 11, 9, 10, 13 })
                Assert.Equal(0, LevelsOn(ranks, modId));
        }
    }

    [Fact]
    public void CuratedHeatNeverTurnsOnABigModifierWhileHeatIsLowered()
    {
        // The big steps (the four +3 toggles and Aficionado) are never backed off to hit an exact heat, so once one is
        // on it stays on at every higher heat. +1 steps are different: a +3 step arriving can take a +1 step of a
        // lower tier back out, so a single level may reappear when heat drops. That is accepted, because hitting
        // every exact heat is worth more than strict nesting at those 12 values.
        var big = new[] { 8, 9, 10, 11, 15 };
        for (var seed = 1; seed <= 100; seed++)
        {
            for (var heat = 1; heat <= HeatSolver.MaxBaseHeat; heat++)
            {
                var higher = HeatSolver.SolveRanks(heat, true, seed * 104729, true);
                var lower = HeatSolver.SolveRanks(heat - 1, true, seed * 104729, true);
                foreach (var modId in big)
                    Assert.True(LevelsOn(lower, modId) <= LevelsOn(higher, modId),
                        $"seed {seed}, heat {heat}: modifier {modId} grew when heat dropped");
            }
        }
    }

    [Fact]
    public void CuratedHeatOnlyBreaksTierOrderAtTheHeatsWhereAnExactTotalForcesIt()
    {
        // Documented behavior: at these heats a +2/+3 step needs a lower tier's +1 step given back to land exactly.
        var allowed = new HashSet<int> { 8, 9, 27, 28, 29, 30, 31, 32, 45, 46, 48, 49 };
        for (var seed = 1; seed <= 300; seed++)
        {
            for (var heat = 1; heat <= HeatSolver.MaxBaseHeat; heat++)
            {
                var ranks = HeatSolver.SolveRanks(heat, true, seed * 3571, true);
                var (highestUsed, lowestIncomplete) = TierSpan(ranks);
                if (lowestIncomplete < highestUsed)
                    Assert.Contains(heat, allowed);
            }
        }
    }

    [Fact]
    public void CuratedHeatKeepsEachModifiersLevelsInOrder()
    {
        // Level stored is a count, but bit modifiers store a bitmask: bit n+1 must never be on without bit n.
        foreach (var modId in new[] { 6, 7, 12, 14 })
        {
            for (var seed = 1; seed <= 100; seed++)
            {
                for (var heat = 0; heat <= HeatSolver.MaxBaseHeat; heat++)
                {
                    var stored = HeatSolver.SolveRanks(heat, true, seed * 3571, true)[modId];
                    Assert.True((stored & (stored + 1)) == 0, $"seed {seed}, heat {heat}, mod {modId}: bits are not contiguous ({stored})");
                }
            }
        }
    }

    [Fact]
    public void TheDifferentSeedsGiveDifferentCuratedAssortmentsAndTheSameSeedRepeats()
    {
        var a = HeatSolver.SolveRanks(30, true, 111, true);
        var again = HeatSolver.SolveRanks(30, true, 111, true);
        var b = HeatSolver.SolveRanks(30, true, 222, true);
        Assert.Equal(a, again);
        Assert.False(a.SequenceEqual(b));
    }
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(-1, 0)]
    [InlineData(56, HeatSolver.MaxBaseHeat)]
    [InlineData(100, HeatSolver.MaxBaseHeat)]
    public void TargetsOutsideTheRangeAreClamped(int target, int expected)
    {
        var ranks = HeatSolver.SolveRanks(target);
        Assert.Equal(expected, HeatSolver.CalculateTotalHeat(ranks, Catalog));
    }

    [Fact]
    public void UnsafeAndDlcModifiersAreNeverEnabled()
    {
        for (var target = 0; target <= HeatSolver.MaxBaseHeat; target++)
        {
            var ranks = HeatSolver.SolveRanks(target);
            // Shared Fate (16), Chaos (17), and DLC modifiers (18..22) must always stay 0.
            for (var modId = 16; modId < ranks.Length; modId++)
                Assert.Equal(0, ranks[modId]);
        }
    }

    [Fact]
    public void UnitStepRangeIsStrictlyMonotonic()
    {
        for (var target = 0; target < 39; target++)
        {
            var lower = HeatSolver.SolveRanks(target);
            var higher = HeatSolver.SolveRanks(target + 1);
            for (var modId = 1; modId <= 15; modId++)
                Assert.True((higher[modId] >= lower[modId]), $"mod {modId} decreased from {target} to {target + 1}");
        }
    }

    [Fact]
    public void BigModifiersIncreaseMonotonicallyAtHighHeat()
    {
        var bigMods = new[] { 15, 8, 9, 11, 10 };
        for (var target = 0; target < HeatSolver.MaxBaseHeat; target++)
        {
            var lower = HeatSolver.SolveRanks(target);
            var higher = HeatSolver.SolveRanks(target + 1);
            foreach (var modId in bigMods)
                Assert.True(higher[modId] >= lower[modId], $"big mod {modId} decreased from {target} to {target + 1}");
        }
    }

    [Theory]
    [InlineData(25, 15, 1, 0, 25)]
    [InlineData(25, 15, 1, 3, 22)]
    [InlineData(25, 15, 1, 10, 15)]
    [InlineData(25, 15, 1, 20, 15)]
    [InlineData(30, 15, 4, 2, 22)]
    [InlineData(30, 15, 4, 4, 15)]
    [InlineData(10, 15, 1, 0, 10)]
    [InlineData(10, 15, 1, 5, 10)]
    public void EffectiveHeatHonorsStartFloorAndStep(
        int startHeat, int minimumHeat, int step, int received, int expected)
    {
        Assert.Equal(expected, HeatSolver.EffectiveHeat(startHeat, minimumHeat, step, received));
    }
}

public class RelicOrbPolicyTests
{


    [Fact]
    public void OnlyOrbRelicsInThePendingListAreCounted()
    {
        // Observed in the live log: relic 48 (an orb relic) and relic 211 (not one) were pending together.
        Assert.True(Catalog.IsOrbRelic(48));
        Assert.False(Catalog.IsOrbRelic(211));
        Assert.Equal(1, RelicOrbPolicy.CountPendingOrbRelics(new[] { 48, 211 }, Catalog.IsOrbRelic));
    }

    [Fact]
    public void EveryPendingOrbRelicCountsAndEmptyOrMissingListsCountNothing()
    {
        var orbRelicIds = Catalog.RelicOrbs.Take(3).Select(orb => orb.DefId).ToArray();
        Assert.Equal(3, RelicOrbPolicy.CountPendingOrbRelics(orbRelicIds, Catalog.IsOrbRelic));
        Assert.Equal(0, RelicOrbPolicy.CountPendingOrbRelics(Array.Empty<int>(), Catalog.IsOrbRelic));
        Assert.Equal(0, RelicOrbPolicy.CountPendingOrbRelics(null, Catalog.IsOrbRelic));
    }

    private static readonly CatalogData Catalog = GameCatalog.Data;
}
public class PraxisHandStopPolicyTests
{
    // The hand effect keeps spawning projectiles while it is ticked, even with its hands gone, so the mod stops it when the
    // room leaves the fight. A short grace period keeps a one-frame state flicker from ending it early.

    [Fact]
    public void ItNeverStopsWhileTheRoomIsStillInTheFight()
    {
        var leftAt = -1f;
        for (var t = 0f; t < 60f; t += 0.5f)
            Assert.False(PraxisHandStopPolicy.ShouldStopForRoomState(true, t, ref leftAt));
        Assert.Equal(-1f, leftAt);
    }

    [Fact]
    public void ItStopsOnlyAfterTheRoomHasStayedOutsideTheFightForTheGracePeriod()
    {
        var leftAt = -1f;
        Assert.False(PraxisHandStopPolicy.ShouldStopForRoomState(false, 10.0f, ref leftAt));
        Assert.False(PraxisHandStopPolicy.ShouldStopForRoomState(false, 10.3f, ref leftAt));
        Assert.True(PraxisHandStopPolicy.ShouldStopForRoomState(false, 10.5f, ref leftAt));
    }

    [Fact]
    public void AOneFrameFlickerOutsideTheFightDoesNotStopIt()
    {
        var leftAt = -1f;
        Assert.False(PraxisHandStopPolicy.ShouldStopForRoomState(false, 10.00f, ref leftAt));
        Assert.False(PraxisHandStopPolicy.ShouldStopForRoomState(true, 10.02f, ref leftAt));
        Assert.Equal(-1f, leftAt);
        // The room leaves again much later: the grace period starts over from this moment, not from the first flicker.
        Assert.False(PraxisHandStopPolicy.ShouldStopForRoomState(false, 20.00f, ref leftAt));
        Assert.False(PraxisHandStopPolicy.ShouldStopForRoomState(false, 20.30f, ref leftAt));
        Assert.True(PraxisHandStopPolicy.ShouldStopForRoomState(false, 20.50f, ref leftAt));
    }

    [Fact]
    public void TheClockGoingBackwardsRestartsTheGracePeriodInsteadOfStoppingAtOnce()
    {
        var leftAt = 500f;
        Assert.False(PraxisHandStopPolicy.ShouldStopForRoomState(false, 1f, ref leftAt));
        Assert.Equal(1f, leftAt);
    }

    [Fact]
    public void TheGracePeriodIsShortEnoughToStopTheProjectilesPromptly()
    {
        Assert.InRange(PraxisHandStopPolicy.GraceSeconds, 0.1f, 1.5f);
    }
}

public class TrapBossWatchTests
{
    // The trap's miniboss is never given the game's death handling, so the mod checks its health on a timer.

    [Fact]
    public void TheFirstCheckIsAlwaysDue()
    {
        Assert.True(TrapBossWatch.IsDue(now: 10f, lastCheckAt: -1f));
    }

    [Theory]
    [InlineData(10.00f, 10.00f, false)]
    [InlineData(10.20f, 10.00f, false)]
    [InlineData(10.25f, 10.00f, true)]
    [InlineData(11.00f, 10.00f, true)]
    public void ACheckIsDueOnlyOnceTheIntervalHasPassed(float now, float last, bool expected)
    {
        Assert.Equal(expected, TrapBossWatch.IsDue(now, last));
    }

    [Fact]
    public void TheClockGoingBackwardsStillCountsAsDue()
    {
        // A scene reload can reset the clock; the watch must not stall waiting for it to catch up.
        Assert.True(TrapBossWatch.IsDue(now: 1f, lastCheckAt: 500f));
    }

    [Fact]
    public void TheIntervalIsLongerThanAFrameButShorterThanAnyoneNotices()
    {
        Assert.InRange(TrapBossWatch.CheckIntervalSeconds, 0.1f, 0.5f);
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(-5f, true)]
    [InlineData(0.01f, false)]
    [InlineData(1200f, false)]
    public void OnlyZeroOrLessHealthIsDead(float hp, bool dead)
    {
        Assert.Equal(dead, TrapBossWatch.IsDead(hp));
    }

    [Fact]
    public void AFailedHealthReadNeverRemovesALivingBoss()
    {
        Assert.False(TrapBossWatch.IsDead(float.NaN));
    }

    [Fact]
    public void SnapshotLetsACallerRemoveWhileWalking()
    {
        var tracker = new TrapBossTracker();
        tracker.Track(new IntPtr(0x1000));
        tracker.Track(new IntPtr(0x2000));
        foreach (var entity in tracker.Snapshot())
            Assert.True(tracker.TakeIfTracked(entity));
        Assert.Equal(0, tracker.Count);
    }
}

public class TrapBossTrackerTests
{
    // The Boss Ambush Trap's miniboss skips the game's miniboss fight, so nothing removes its body when it dies. The
    // tracker is how the mod knows an enemy that just died is one of the trap's own, and never anything else.

    [Fact]
    public void ATrackedEnemyIsReportedOnceAtItsDeath()
    {
        var tracker = new TrapBossTracker();
        var miniboss = new IntPtr(0x1000);
        tracker.Track(miniboss);
        Assert.True(tracker.TakeIfTracked(miniboss));
        Assert.False(tracker.TakeIfTracked(miniboss));
    }

    [Fact]
    public void AnEnemyTheTrapDidNotSpawnIsNeverReported()
    {
        var tracker = new TrapBossTracker();
        tracker.Track(new IntPtr(0x1000));
        Assert.False(tracker.TakeIfTracked(new IntPtr(0x2000)));
        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public void ADeathBeforeAnythingWasTrackedIsIgnored()
    {
        var tracker = new TrapBossTracker();
        Assert.False(tracker.TakeIfTracked(new IntPtr(0x1000)));
    }

    [Fact]
    public void SeveralTrapMinibossesAreTrackedIndependently()
    {
        var tracker = new TrapBossTracker();
        var a = new IntPtr(0x1000);
        var b = new IntPtr(0x2000);
        tracker.Track(a);
        tracker.Track(b);
        Assert.True(tracker.TakeIfTracked(b));
        Assert.Equal(1, tracker.Count);
        Assert.True(tracker.TakeIfTracked(a));
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void ANullEntityIsNeverTrackedOrReported()
    {
        // SpawnEnemy can return null when the spawn fails; that must not make every null death look like ours.
        var tracker = new TrapBossTracker();
        tracker.Track(IntPtr.Zero);
        Assert.Equal(0, tracker.Count);
        Assert.False(tracker.TakeIfTracked(IntPtr.Zero));
    }

    [Fact]
    public void ClearForgetsEveryEntity()
    {
        var tracker = new TrapBossTracker();
        tracker.Track(new IntPtr(0x1000));
        tracker.Clear();
        Assert.False(tracker.TakeIfTracked(new IntPtr(0x1000)));
    }

    [Fact]
    public void ContainsLooksWithoutForgetting()
    {
        // The diagnostic reads the tracker at the dead state, before the kill event removes the entity. Looking must not
        // use up the one removal the kill event is owed.
        var tracker = new TrapBossTracker();
        var miniboss = new IntPtr(0x1000);
        tracker.Track(miniboss);
        Assert.True(tracker.Contains(miniboss));
        Assert.True(tracker.Contains(miniboss));
        Assert.False(tracker.Contains(new IntPtr(0x2000)));
        Assert.False(tracker.Contains(IntPtr.Zero));
        Assert.True(tracker.TakeIfTracked(miniboss));
        Assert.False(tracker.Contains(miniboss));
    }
}

public class OrbCollectionTrackerTests
{
    // Observed in the live log, for both a manual pickup and an orb left on the ground: the game calls
    // AddUnknownRelicCollected once at spawn (returns true) and once when the player takes the orb (returns false).

    [Fact]
    public void SpawnIsNotAPickupButTheSecondCallIs()
    {
        var tracker = new OrbCollectionTracker();
        Assert.False(tracker.IsPickup(48, gameAddedIt: true));
        Assert.True(tracker.IsPickup(48, gameAddedIt: false));
    }

    [Fact]
    public void AnOrbLeftOnTheGroundIsCountedTheSameAsOnePickedUpByHand()
    {
        // Relic 212 in the live log: spawned (true), pulled in by the room-clear sequence (false), no manual pickup.
        var tracker = new OrbCollectionTracker();
        Assert.False(tracker.IsPickup(212, true));
        Assert.True(tracker.IsPickup(212, false));
    }

    [Fact]
    public void AnOrbNeverSendsASecondCheck()
    {
        var tracker = new OrbCollectionTracker();
        tracker.IsPickup(48, true);
        Assert.True(tracker.IsPickup(48, false));
        Assert.False(tracker.IsPickup(48, false));
        Assert.False(tracker.IsPickup(48, false));
    }

    [Fact]
    public void AnOrbSeenOnlyAtPickupStillCountsOnce()
    {
        // The spawn call was missed (hook installed mid-room, reconnect, orb already on the floor in a loaded save).
        var tracker = new OrbCollectionTracker();
        Assert.True(tracker.IsPickup(40, gameAddedIt: false));
        Assert.False(tracker.IsPickup(40, gameAddedIt: false));
    }

    [Fact]
    public void SeveralOrbsInOneRoomAreTrackedIndependently()
    {
        // Relics 48 (base) and 211 (DLC) spawn together and are taken in either order.
        var tracker = new OrbCollectionTracker();
        Assert.False(tracker.IsPickup(48, true));
        Assert.False(tracker.IsPickup(211, true));
        Assert.True(tracker.IsPickup(211, false));
        Assert.True(tracker.IsPickup(48, false));
        Assert.False(tracker.IsPickup(211, false));
    }

    [Fact]
    public void ARelicThatSpawnsAgainAfterBeingCollectedCanBeCollectedAgain()
    {
        // A new run reuses the relic ids; the game adds the id to its fresh list again (true), then it is taken (false).
        var tracker = new OrbCollectionTracker();
        tracker.IsPickup(48, true);
        Assert.True(tracker.IsPickup(48, false));
        Assert.False(tracker.IsPickup(48, true));
        Assert.True(tracker.IsPickup(48, false));
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        var tracker = new OrbCollectionTracker();
        tracker.IsPickup(48, true);
        tracker.IsPickup(48, false);
        tracker.Reset();
        Assert.True(tracker.IsPickup(48, false));
    }
}

public class RelicRevealTests
{
    private static readonly CatalogData Catalog = GameCatalog.Data;

    private static ScoutedOrb Own(int order, int relicDefId) => new()
    {
        Order = order, ItemName = "Some Relic", PlayerName = "Me", IsOwnPlayer = true, RelicDefId = relicDefId,
    };

    private static ScoutedOrb Foreign(int order, string item, string player) => new()
    {
        Order = order, ItemName = item, PlayerName = player, IsOwnPlayer = false,
    };

    [Fact]
    public void AnOrbHoldingMyRelicRevealsThatRelic()
    {
        var scouted = new Dictionary<int, ScoutedOrb> { [1] = Own(1, 183), [2] = Own(2, 48) };
        var shown = RelicOrbPolicy.ChooseReveals(0, 2, 2, order => scouted.GetValueOrDefault(order));
        Assert.Equal(new int?[] { 183, 48 }, shown.Select(reveal => reveal.RelicDefId).ToArray());
        Assert.All(shown, reveal => Assert.Null(reveal.Text));
    }

    [Fact]
    public void AnOrbHoldingAnotherPlayersItemRevealsTextNotARelic()
    {
        var scouted = new Dictionary<int, ScoutedOrb> { [1] = Foreign(1, "Hookshot", "Link") };
        var shown = RelicOrbPolicy.ChooseReveals(0, 1, 1, order => scouted.GetValueOrDefault(order));
        Assert.Null(shown[0].RelicDefId);
        Assert.Equal("Esper senses: Hookshot for Link", shown[0].Text);
    }

    [Fact]
    public void AnOrbHoldingNonRelicItemOfMyOwnNamesTheItemWithoutAPlayer()
    {
        var gold = new ScoutedOrb { Order = 1, ItemName = "50 Gold", PlayerName = "Me", IsOwnPlayer = true, RelicDefId = null };
        var shown = RelicOrbPolicy.ChooseReveals(0, 1, 1, _ => gold);
        Assert.Null(shown[0].RelicDefId);
        Assert.Equal("Esper senses: 50 Gold", shown[0].Text);
    }

    [Fact]
    public void RevealContinuesWithTheOrbsAfterTheOnesAlreadyShown()
    {
        var scouted = new Dictionary<int, ScoutedOrb> { [1] = Own(1, 183), [2] = Own(2, 48), [3] = Own(3, 100) };
        var shown = RelicOrbPolicy.ChooseReveals(alreadyRevealed: 2, orbsHandedIn: 1, orbsCollected: 3,
            order => scouted.GetValueOrDefault(order));
        Assert.Equal(100, shown[0].RelicDefId);
    }

    [Fact]
    public void OrbsThatWereNeverScoutedKeepTheGamesOwnRelic()
    {
        var shown = RelicOrbPolicy.ChooseReveals(0, 2, 2, _ => null);
        Assert.All(shown, reveal => Assert.Equal(default, reveal));
    }

    [Fact]
    public void AnOrbNumberBeyondWhatWasCollectedIsNeverRevealed()
    {
        // Two orbs handed in but only one was ever counted as collected: the second has no location to describe.
        var scouted = new Dictionary<int, ScoutedOrb> { [1] = Own(1, 183), [2] = Own(2, 48) };
        var shown = RelicOrbPolicy.ChooseReveals(0, 2, orbsCollected: 1, order => scouted.GetValueOrDefault(order));
        Assert.Equal(183, shown[0].RelicDefId);
        Assert.Equal(default, shown[1]);
    }

    [Fact]
    public void NoOrbsOrNegativeCountsShowNothing()
    {
        Assert.Empty(RelicOrbPolicy.ChooseReveals(0, 0, 5, _ => Own(1, 183)));
        Assert.Empty(RelicOrbPolicy.ChooseReveals(0, -3, 5, _ => Own(1, 183)));
        Assert.Equal(183, RelicOrbPolicy.ChooseReveals(-5, 1, 5, _ => Own(1, 183))[0].RelicDefId);
    }

    [Fact]
    public void EveryRelicItemMapsToAnOrbRelicTheRevealCanShow()
    {
        for (var i = 0; i < Catalog.RelicOrbs.Count; i++)
        {
            var relic = Catalog.RelicForItem(Catalog.RelicItemId(i));
            Assert.NotNull(relic);
            Assert.True(Catalog.IsOrbRelic(relic!.DefId));
        }
    }

    [Fact]
    public void EveryOrbLocationIdIsDistinctSoTheScoutMapIsUnambiguous()
    {
        var ids = Catalog.RelicOrbs.Select(orb => Catalog.RelicOrbLocationId(orb.Order - 1)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(53, ids.Count);
    }
}
public class AshFillerTests
{
    private static readonly CatalogData Catalog = GameCatalog.Data;

    [Fact]
    public void AshIsTheLastFillerSoNoExistingItemIdMoved()
    {
        var names = Catalog.Fillers.Select(filler => filler.Name).ToList();
        Assert.Equal(
            new[]
            {
                "1 Archipelago Coin", "2 Archipelago Coins", "3 Archipelago Coins", "50 Gold",
                "Enemy Ambush Trap", "Boss Ambush Trap", "Praxis Arena Trap", "Ash",
            },
            names);
        Assert.Equal("ash", Catalog.Fillers[^1].Kind);
    }

    [Fact]
    public void AshResolvesFromItsItemIdAndStaysBelowTheRelicItemBlock()
    {
        var ashIndex = Catalog.Fillers.Count - 1;
        var ashItemId = Catalog.IdBase + 200 + ashIndex;
        Assert.Equal(113523207, ashItemId);
        var filler = Catalog.FillerForItem(ashItemId);
        Assert.NotNull(filler);
        Assert.Equal("Ash", filler!.Name);
        Assert.Equal("Ash", Catalog.GetItemName(ashItemId));
        Assert.True(ashItemId < Catalog.RelicItemId(0), "filler IDs must stay below the relic item block");
    }

    [Fact]
    public void OnlyOneFillerGrantsAsh()
    {
        Assert.Single(Catalog.Fillers, filler => filler.Kind == "ash");
    }
}
public class RetiredCoinBundleTests
{
    private static readonly CatalogData Catalog = GameCatalog.Data;

    [Fact]
    public void TheThreeCoinBundleStaysInTheCatalogSoItsItemIdAndLaterIdsDoNotMove()
    {
        // The generator no longer places it, but item IDs are positions in the filler list. Removing it would shift
        // 50 Gold, the traps and Ash, and a seed that was already generated could still send it.
        var threeCoins = Catalog.Fillers.Single(filler => filler.Name == "3 Archipelago Coins");
        Assert.Equal("coin", threeCoins.Kind);
        Assert.Equal(3, threeCoins.Amount);
        Assert.Equal(2, Catalog.Fillers.IndexOf(threeCoins));
        Assert.Equal("50 Gold", Catalog.FillerForItem(Catalog.IdBase + 203)!.Name);
        Assert.Equal("Ash", Catalog.FillerForItem(Catalog.IdBase + 207)!.Name);
    }

    [Fact]
    public void AnExistingSeedThatSendsThreeCoinsStillResolvesToThreeCoins()
    {
        var filler = Catalog.FillerForItem(Catalog.IdBase + 202);
        Assert.NotNull(filler);
        Assert.Equal(("coin", 3), (filler!.Kind, filler.Amount));
    }
}
public class UpgradeTooltipTests
{
    private static readonly CatalogData Catalog = GameCatalog.Data;

    private static ScoutedItem Mine(string item) => new() { ItemName = item, PlayerName = "Me", IsOwnPlayer = true };
    private static ScoutedItem Theirs(string item, string player) => new() { ItemName = item, PlayerName = player, IsOwnPlayer = false };

    [Fact]
    public void ShowsOnlyTheItemNotTheCheckName()
    {
        var line = UpgradeTooltip.Format("Ember Tree: Regeneration", Mine("5 Archipelago Coins"), alreadyChecked: false);
        Assert.Equal($"{UpgradeTooltip.Marker} 5 Archipelago Coins", line);
        Assert.DoesNotContain("Ember Tree", line);
        Assert.DoesNotContain(">", line.Replace("<color=#8fd3ff>", "").Replace("</color>", ""));
    }

    [Fact]
    public void NamesTheOtherPlayerForTheirItem()
    {
        var line = UpgradeTooltip.Format("Sword Mod: Spark", Theirs("Hookshot", "Link"), false);
        Assert.Equal($"{UpgradeTooltip.Marker} Hookshot for Link", line);
        Assert.DoesNotContain("Sword Mod", line);
    }

    [Fact]
    public void FallsBackToTheCheckNameOnlyWhenTheItemWasNotScouted()
    {
        var line = UpgradeTooltip.Format("Ember Tree: Thick Skin", null, false);
        Assert.Equal($"{UpgradeTooltip.Marker} Ember Tree: Thick Skin", line);
    }

    [Fact]
    public void MarksChecksThatWereAlreadySent()
    {
        var sent = UpgradeTooltip.Format("Ember Tree: Thick Skin", Mine("Cyclone"), alreadyChecked: true);
        Assert.Contains("(sent)", sent);
        Assert.StartsWith($"{UpgradeTooltip.Marker} Cyclone", sent);
        Assert.DoesNotContain("(sent)", UpgradeTooltip.Format("Ember Tree: Thick Skin", Mine("Cyclone"), false));
    }

    [Fact]
    public void AppendKeepsTheGamesDescriptionAndAddsTheLineOnce()
    {
        var line = UpgradeTooltip.Format("Ember Tree: Regeneration", null, false);
        var once = UpgradeTooltip.Append("Heal over time.", line);
        Assert.StartsWith("Heal over time.\n", once);
        Assert.EndsWith(line, once);
        Assert.Equal(once, UpgradeTooltip.Append(once, line));
    }

    [Fact]
    public void AppendHandlesAnEmptyOrMissingDescription()
    {
        var line = UpgradeTooltip.Format("Ember Tree: Regeneration", null, false);
        Assert.Equal(line, UpgradeTooltip.Append(null, line));
        Assert.Equal(line, UpgradeTooltip.Append("", line));
    }

    [Fact]
    public void EveryEmberTreeNodeAndWeaponModHasALocationTheTooltipCanName()
    {
        foreach (var node in Catalog.EmberTree)
        {
            var id = Catalog.LocationForEmberTreeNode(node.DefId);
            Assert.NotNull(id);
            Assert.StartsWith("Ember Tree: ", Catalog.GetLocationName(id!.Value));
        }
        foreach (var mod in Catalog.WeaponMods)
        {
            var id = Catalog.LocationForWeaponMod(mod.DefId);
            Assert.NotNull(id);
            Assert.Equal(mod.Name, Catalog.GetLocationName(id!.Value));
        }
    }

    // The game's own EmberTreeLevels table: upgrades required to open each stage, stage 1 first.
    private static readonly int[] Thresholds = { 0, 1, 4, 7, 10, 13, 16, 19, 30, 35, 104 };

    [Fact]
    public void AFreshTreeOnlyOpensStageOne()
    {
        Assert.True(UpgradeTooltip.CanUnlockEmberTreeNode(false, 1, 0, Thresholds));
        Assert.False(UpgradeTooltip.CanUnlockEmberTreeNode(false, 2, 0, Thresholds));
        Assert.False(UpgradeTooltip.CanUnlockEmberTreeNode(false, 10, 0, Thresholds));
    }

    [Theory]
    [InlineData(2, 0, false)]
    [InlineData(2, 1, true)]
    [InlineData(3, 3, false)]
    [InlineData(3, 4, true)]
    [InlineData(8, 18, false)]
    [InlineData(8, 19, true)]
    [InlineData(9, 29, false)]
    [InlineData(9, 30, true)]
    [InlineData(10, 34, false)]
    [InlineData(10, 35, true)]
    [InlineData(10, 200, true)]
    public void AStageOpensExactlyAtItsUpgradeThreshold(int stage, int invested, bool expected)
    {
        Assert.Equal(expected, UpgradeTooltip.CanUnlockEmberTreeNode(false, stage, invested, Thresholds));
    }

    [Fact]
    public void AnAlreadyUnlockedNodeIsNeverShownEvenInAnOpenStage()
    {
        Assert.False(UpgradeTooltip.CanUnlockEmberTreeNode(true, 1, 50, Thresholds));
        Assert.False(UpgradeTooltip.CanUnlockEmberTreeNode(true, 10, 200, Thresholds));
    }

    [Theory]
    [InlineData(false, false, false)] // locked and never sent: stays hidden
    [InlineData(false, true, true)]   // can be bought now: show what it holds
    [InlineData(true, false, true)]   // bought and sent: still shows the item with "(sent)"
    [InlineData(true, true, true)]
    public void TheEmberTreeLineShowsWhileBuyableAndAfterTheCheckWasSent(bool sent, bool canUnlock, bool expected)
    {
        Assert.Equal(expected, UpgradeTooltip.ShouldShowEmberTreeLine(sent, canUnlock));
    }

    [Fact]
    public void ABoughtNodeWhoseCheckWasSentKeepsItsSentLine()
    {
        var shown = UpgradeTooltip.ShouldShowEmberTreeLine(checkSent: true, canUnlockNow: false);
        Assert.True(shown);
        Assert.Contains("(sent)", UpgradeTooltip.Format("Ember Tree: Thick Skin", Mine("Cyclone"), alreadyChecked: true));
    }

    [Fact]
    public void BadInputHidesTheLineInsteadOfGuessing()
    {
        Assert.False(UpgradeTooltip.CanUnlockEmberTreeNode(false, 0, 10, Thresholds));
        Assert.False(UpgradeTooltip.CanUnlockEmberTreeNode(false, -1, 10, Thresholds));
        Assert.False(UpgradeTooltip.CanUnlockEmberTreeNode(false, 1, 10, null));
        Assert.False(UpgradeTooltip.CanUnlockEmberTreeNode(false, 1, 10, Array.Empty<int>()));
        // A stage past the end of the table is treated as closed.
        Assert.False(UpgradeTooltip.CanUnlockEmberTreeNode(false, 12, 1000, Thresholds));
    }

    [Fact]
    public void EveryCatalogNodeStageHasAThresholdInTheGamesTable()
    {
        foreach (var node in Catalog.EmberTree)
        {
            Assert.InRange(node.Stage, 1, Thresholds.Length);
            Assert.True(UpgradeTooltip.CanUnlockEmberTreeNode(false, node.Stage, 1000, Thresholds), node.Name);
        }
    }

    [Fact]
    public void UnknownUpgradesGetNoLocationSoNoTooltipIsAdded()
    {
        Assert.Null(Catalog.LocationForEmberTreeNode(-1));
        Assert.Null(Catalog.LocationForWeaponMod(-1));
    }
}

public class StandardSkillTests
{
    private static readonly CatalogData Catalog = GameCatalog.Data;

    [Fact]
    public void TheSevenBaseGameDefaultSkillsAreTheStandardSkills()
    {
        Assert.Equal(7, Catalog.StandardSkills.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 6, 8, 36 }, Catalog.StandardSkills.Select(skill => skill.AbilityId).ToArray());
    }

    [Fact]
    public void StandardSkillItemIdsAreClearOfTabletSkillsAndWeaponsAndMatchTheApworld()
    {
        var ids = Catalog.StandardSkills.Select((_, index) => Catalog.StandardSkillItemId(index)).ToList();
        Assert.Equal(Enumerable.Range(0, 7).Select(i => (long)Catalog.IdBase + 20 + i).ToList(), ids);
        Assert.DoesNotContain(ids, id => Enumerable.Range(0, Catalog.Skills.Count).Select(Catalog.SkillItemId).Contains(id));
        Assert.True(ids.Max() < Catalog.WeaponItemId(0));
    }

    [Fact]
    public void EveryStandardSkillItemResolvesBackToItsSkillAndNameAndNothingElseDoes()
    {
        for (var i = 0; i < Catalog.StandardSkills.Count; i++)
        {
            var id = Catalog.StandardSkillItemId(i);
            Assert.Same(Catalog.StandardSkills[i], Catalog.StandardSkillForItem(id));
            Assert.Equal(Catalog.StandardSkills[i].Name, Catalog.GetItemName(id));
            Assert.Null(Catalog.SkillForItem(id));
        }
        Assert.Null(Catalog.StandardSkillForItem(Catalog.SkillItemId(0)));
        Assert.Null(Catalog.StandardSkillForItem(Catalog.WeaponItemId(0)));
        Assert.Null(Catalog.StandardSkillForItem(Catalog.IdBase + 19));
        Assert.Null(Catalog.StandardSkillForItem(Catalog.IdBase + 27));
    }

    [Fact]
    public void ATabletSkillIsNeverAlsoAStandardSkill()
    {
        foreach (var tablet in Catalog.Skills)
            Assert.Null(Catalog.StandardSkillForAbility(tablet.AbilityId));
    }
}

public class LogBufferTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 14, 23, 0);

    private static string[] Texts(IReadOnlyList<LogBuffer.Row> rows) => rows.Select(row => row.Text).ToArray();

    [Fact]
    public void AddsMessagesAndIgnoresBlankOnes()
    {
        var buffer = new LogBuffer(10);
        Assert.True(buffer.Add("Connected.", T0));
        Assert.False(buffer.Add(null, T0));
        Assert.False(buffer.Add("", T0));
        Assert.False(buffer.Add("   ", T0));
        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void DropsAMessageIdenticalToTheNewestButKeepsAnEarlierRepeat()
    {
        var buffer = new LogBuffer(10);
        buffer.Add("Received 5 Ash.", T0);
        Assert.False(buffer.Add("Received 5 Ash.", T0.AddMinutes(1)));
        buffer.Add("Check: Ember Tree: Thick Skin", T0.AddMinutes(1));
        Assert.True(buffer.Add("Received 5 Ash.", T0.AddMinutes(2)));
        Assert.Equal(3, buffer.Count);
    }

    [Fact]
    public void DiscardsTheOldestMessageOnceFull()
    {
        var buffer = new LogBuffer(3);
        foreach (var line in new[] { "one", "two", "three", "four" })
            buffer.Add(line, T0);
        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { "two", "three", "four" }, Texts(buffer.VisibleRows(10, 40)));
    }

    [Fact]
    public void VersionChangesOnlyWhenTheContentChanges()
    {
        var buffer = new LogBuffer(5);
        var start = buffer.Version;
        buffer.Add("a", T0);
        Assert.NotEqual(start, buffer.Version);
        var afterAdd = buffer.Version;
        buffer.Add("a", T0);
        buffer.Add("", T0);
        Assert.Equal(afterAdd, buffer.Version);
        buffer.Clear();
        Assert.NotEqual(afterAdd, buffer.Version);
        var afterClear = buffer.Version;
        buffer.Clear();
        Assert.Equal(afterClear, buffer.Version);
    }

    [Fact]
    public void ShowsTheNewestMessagesWhenMoreThanFit()
    {
        var buffer = new LogBuffer(20);
        for (var i = 1; i <= 8; i++)
            buffer.Add($"line {i}", T0);
        Assert.Equal(new[] { "line 6", "line 7", "line 8" }, Texts(buffer.VisibleRows(3, 40)));
    }

    [Fact]
    public void WrapsLongMessagesAtWordBoundaries()
    {
        var rows = LogBuffer.Wrap("Hookshot for Link was found at Scoggfirth Room 3 Reached", 20);
        Assert.All(rows, row => Assert.True(row.Length <= 20, row));
        Assert.Equal("Hookshot for Link was found at Scoggfirth Room 3 Reached", string.Join(" ", rows));
    }

    [Fact]
    public void SplitsAWordLongerThanARow()
    {
        var rows = LogBuffer.Wrap("Supercalifragilisticexpialidocious", 10);
        Assert.All(rows, row => Assert.True(row.Length <= 10, row));
        Assert.Equal("Supercalifragilisticexpialidocious", string.Concat(rows));
    }

    [Fact]
    public void OnlyTheFirstRowOfAMessageCarriesItsTime()
    {
        var buffer = new LogBuffer(10);
        buffer.Add("a rather long message that needs three rows", T0);
        var rows = buffer.VisibleRows(10, 16);
        Assert.True(rows.Count >= 3);
        Assert.Equal("14:23", rows[0].Time);
        Assert.True(rows[0].StartsMessage);
        Assert.All(rows.Skip(1), row => { Assert.Null(row.Time); Assert.False(row.StartsMessage); });
        Assert.Equal("a rather long message that needs three rows", string.Join(" ", Texts(rows)));
    }

    [Fact]
    public void EachMessageGetsItsOwnTimeAndAContinuationNeverBecomesANewMessage()
    {
        var buffer = new LogBuffer(10);
        buffer.Add("first message that is long enough to wrap onto a second row", T0);
        buffer.Add("second", T0.AddMinutes(1));
        var rows = buffer.VisibleRows(10, 24);
        var starts = rows.Where(row => row.StartsMessage).ToList();
        Assert.Equal(2, starts.Count);
        Assert.Equal(new[] { "14:23", "14:24" }, starts.Select(row => row.Time).ToArray());
        Assert.Equal("second", rows[^1].Text);
    }

    [Fact]
    public void ANewestMessageThatWrapsStillAppearsInFull()
    {
        var buffer = new LogBuffer(10);
        buffer.Add("old line", T0);
        buffer.Add("a rather long message that needs three rows", T0.AddMinutes(1));
        var rows = buffer.VisibleRows(4, 20);
        Assert.True(rows.Count <= 4);
        Assert.Equal("a rather long message that needs three rows", string.Join(" ", Texts(rows).Skip(rows.Count - 3)));
        Assert.Equal("old line", rows[0].Text);
    }

    [Fact]
    public void WhenTheWindowIsTooSmallForTheNewestMessageItsStartIsKept()
    {
        var buffer = new LogBuffer(10);
        buffer.Add("alpha beta gamma delta epsilon zeta eta theta iota kappa", T0);
        var rows = buffer.VisibleRows(2, 12);
        Assert.Equal(2, rows.Count);
        Assert.StartsWith("alpha beta", rows[0].Text);
        Assert.True(rows[0].StartsMessage);
    }

    [Fact]
    public void AGapBetweenMessagesIsCountedAgainstTheRowLimitAndNeverPlacedAtTheTop()
    {
        var buffer = new LogBuffer(20);
        for (var i = 1; i <= 6; i++)
            buffer.Add($"message {i}", T0);
        // 4 rows asked for, with a gap row between messages: 3 messages (3 rows) + 2 gaps would be 5, so only 2 fit.
        var rows = buffer.VisibleRows(4, 40, gapBetweenMessages: true);
        var gaps = rows.Count(row => row.StartsMessage) - 1;
        Assert.True(rows.Count + gaps <= 4, $"rows={rows.Count} gaps={gaps}");
        Assert.Equal("message 6", rows[^1].Text);
        Assert.Equal(new[] { "message 5", "message 6" }, Texts(rows));

        var withoutGaps = buffer.VisibleRows(4, 40);
        Assert.Equal(4, withoutGaps.Count);
    }

    [Fact]
    public void ALongNewestMessageIsStillShownWhenGapsAreOn()
    {
        var buffer = new LogBuffer(10);
        buffer.Add("older", T0);
        buffer.Add("one two three four five six seven eight nine ten", T0.AddMinutes(1));
        var rows = buffer.VisibleRows(3, 16, gapBetweenMessages: true);
        Assert.Equal(3, rows.Count);
        Assert.StartsWith("one two", rows[0].Text);
        Assert.DoesNotContain(rows, row => row.Text == "older");
    }

    [Fact]
    public void NeverReturnsMoreRowsThanAskedForAndAnEmptyBufferGivesNone()
    {
        Assert.Empty(new LogBuffer(5).VisibleRows(5, 30));
        var buffer = new LogBuffer(50);
        for (var i = 0; i < 40; i++)
            buffer.Add($"message number {i} with some padding text to make it wrap around", T0);
        Assert.True(buffer.VisibleRows(6, 24).Count <= 6);
        var spaced = buffer.VisibleRows(6, 24, gapBetweenMessages: true);
        Assert.True(spaced.Count + spaced.Count(row => row.StartsMessage) - 1 <= 6);
    }

    [Fact]
    public void TinyOrNonsenseDimensionsAreClamped()
    {
        var buffer = new LogBuffer(5);
        buffer.Add("hello world", T0);
        Assert.NotEmpty(buffer.VisibleRows(0, 0));
        Assert.NotEmpty(buffer.VisibleRows(-3, -3, gapBetweenMessages: true));
        Assert.Equal(1, new LogBuffer(0).Count + 1);
    }

    [Fact]
    public void TimeIsFormattedAsTwentyFourHourHoursAndMinutesRegardlessOfCulture()
    {
        Assert.Equal("09:05", LogBuffer.FormatTime(new DateTime(2026, 1, 2, 9, 5, 59)));
        Assert.Equal("00:00", LogBuffer.FormatTime(new DateTime(2026, 1, 2, 0, 0, 0)));
        Assert.Equal("23:59", LogBuffer.FormatTime(new DateTime(2026, 1, 2, 23, 59, 0)));
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.Equal("14:23", LogBuffer.FormatTime(T0));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("Received skill: Cyclone", "Received")]
    [InlineData("Received 5 Ash (0/4 coins).", "Received")]
    [InlineData("Check: Ember Tree: Thick Skin", "Sent")]
    [InlineData("Goal achieved: Praxis.", "Sent")]
    [InlineData("DeathLink sent.", "Sent")]
    [InlineData("Warning: AP session state could not be saved.", "Warning")]
    [InlineData("Set slot_name in the BepInEx configuration before connecting.", "Warning")]
    [InlineData("The Archipelago connection closed.", "Warning")]
    [InlineData("Could not scout upgrade locations", "Warning")]
    [InlineData("Samus: hello everyone", "Chat")]
    [InlineData("Connected.", "Info")]
    [InlineData("Heat is set by Archipelago: 35 (Heat Reduction items lower it).", "Info")]
    [InlineData("Link sent Hookshot to Samus (Scoggfirth Room 3)", "Info")]
    public void MessagesAreTintedByTheirKind(string text, string expectedKind)
    {
        Assert.Equal(Enum.Parse<LogKind>(expectedKind), LogBuffer.Classify(text));
    }

    [Fact]
    public void AChatLineNeedsAShortSingleWordNameBeforeTheColon()
    {
        Assert.Equal(LogKind.Chat, LogBuffer.Classify("Link: nice find"));
        Assert.Equal(LogKind.Info, LogBuffer.Classify("Heat is set by Archipelago: 35"));
        Assert.Equal(LogKind.Info, LogBuffer.Classify("averyveryverylongplayernamethatgoesonandon: hi"));
        Assert.Equal(LogKind.Info, LogBuffer.Classify(": nothing before the colon"));
    }
}

public class LogFormatterTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 14, 23, 0);

    private static IReadOnlyList<LogBuffer.Row> Rows(params (string text, DateTime time)[] messages)
    {
        var buffer = new LogBuffer(20);
        foreach (var (text, time) in messages)
            buffer.Add(text, time);
        return buffer.VisibleRows(20, 30);
    }

    [Fact]
    public void EveryRowJumpsToTheSameTextColumnSoWrappedRowsHangUnderTheText()
    {
        var rows = Rows(("a rather long message that needs a few rows to display", T0));
        Assert.True(rows.Count >= 2);
        var lines = LogFormatter.Build(rows, textStart: 3.4f).Split('\n');
        Assert.Equal(rows.Count, lines.Length);
        Assert.All(lines, line => Assert.Contains("<pos=3.4em>", line));
        Assert.Contains("14:23", lines[0]);
        Assert.All(lines.Skip(1), line => Assert.DoesNotContain("14:23", line));
    }

    [Fact]
    public void TheTimeIsDimAndTheTextUsesTheColourOfItsKind()
    {
        var text = LogFormatter.Build(Rows(("Received skill: Cyclone", T0)));
        Assert.Contains($"<color={LogFormatter.TimeColor}>14:23</color>", text);
        Assert.Contains($"<color={LogFormatter.ReceivedColor}>Received skill: Cyclone</color>", text);
        var warning = LogFormatter.Build(Rows(("Warning: AP session state could not be saved.", T0)));
        Assert.Contains(LogFormatter.WarningColor, warning);
    }

    [Fact]
    public void ABlankLineSeparatesMessagesOnlyWhenAsked()
    {
        var rows = Rows(("first", T0), ("second", T0.AddMinutes(1)));
        Assert.DoesNotContain("\n\n", LogFormatter.Build(rows, gapBetweenMessages: false));
        var spaced = LogFormatter.Build(rows, gapBetweenMessages: true);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(spaced, "\n\n"));
    }

    [Fact]
    public void NoGapIsAddedInsideAMessageOrBeforeTheFirstOne()
    {
        var rows = Rows(("one long message that wraps over several rows for sure yes", T0));
        var text = LogFormatter.Build(rows, gapBetweenMessages: true);
        Assert.DoesNotContain("\n\n", text);
        Assert.False(text.StartsWith("\n"));
    }

    [Theory]
    [InlineData(80)]
    [InlineData(30)]
    [InlineData(10)]
    public void UserTextCannotInjectMarkupIntoTheWindowAtAnyWrapWidth(int rowWidth)
    {
        var buffer = new LogBuffer(5);
        buffer.Add("Mallory: <color=red>pwned</color><size=300>", T0);
        // Narrow widths split the message across rows, even in the middle of a tag, so check every width.
        var text = LogFormatter.Build(buffer.VisibleRows(20, rowWidth));
        // Ordinal comparison on purpose: xUnit's Assert.DoesNotContain is culture-sensitive, and a culture-sensitive
        // match ignores the zero-width space, so it would report a tag that TextMeshPro cannot actually see.
        Assert.False(text.Contains("<color=red>", StringComparison.Ordinal));
        Assert.False(text.Contains("<size=300>", StringComparison.Ordinal));
        // After removing the formatter's own tags, no '<' may be left that is not followed by a zero-width space.
        var withoutOwnTags = System.Text.RegularExpressions.Regex.Replace(
            text, @"</?color(=#[0-9a-f]{6})?>|<pos=[0-9.]+em>", "");
        for (var i = 0; i < withoutOwnTags.Length; i++)
        {
            if (withoutOwnTags[i] == '<')
                Assert.True(i + 1 < withoutOwnTags.Length && withoutOwnTags[i + 1] == '\u200B', $"bare '<' at {i}: {withoutOwnTags}");
        }
    }

    [Fact]
    public void AnInjectedTagIsShownAsLiteralTextAtWideWidth()
    {
        var buffer = new LogBuffer(5);
        buffer.Add("Mallory: <color=red>pwned</color><size=300>", T0);
        var text = LogFormatter.Build(buffer.VisibleRows(20, 80));
        Assert.Contains("<\u200Bcolor=red>", text);
        Assert.Contains("<\u200Bsize=300>", text);
    }

    [Fact]
    public void EscapeLeavesPlainTextAloneAndNeverDoublesEscapes()
    {
        Assert.Equal("plain text", LogFormatter.Escape("plain text"));
        Assert.Equal("", LogFormatter.Escape(""));
        var once = LogFormatter.Escape("a < b");
        Assert.Equal("a <\u200B b", once);
        // Escaping text that was already escaped only adds one more zero-width space after each '<'.
        Assert.Equal(1, once.Count(ch => ch == '<'));
    }

    [Fact]
    public void EveryKindHasADistinctColourThatIsNotTheTimeColour()
    {
        var kinds = Enum.GetValues<LogKind>();
        var colours = kinds.Select(LogFormatter.ColorFor).ToList();
        Assert.Equal(colours.Count, colours.Distinct().Count());
        Assert.DoesNotContain(LogFormatter.TimeColor, colours);
    }
}
public class FocusFadeTests
{
    [Fact]
    public void StartsHiddenAndStaysHiddenWithoutMessages()
    {
        var fade = new FocusFade(holdSeconds: 5f, fadeInSeconds: 0.2f, fadeOutSeconds: 1f);
        Assert.False(fade.IsVisible);
        for (var t = 0f; t <= 30f; t += 0.5f)
            Assert.Equal(0f, fade.Tick(t));
    }

    [Fact]
    public void ANewMessageBringsTheWindowToFullOpacityAndHoldsIt()
    {
        var fade = new FocusFade(5f, 0.2f, 1f);
        fade.Tick(0f);
        fade.Poke(0f);
        fade.Tick(0.1f);
        Assert.InRange(fade.Alpha, 0.01f, 0.99f);
        fade.Tick(0.3f);
        Assert.Equal(1f, fade.Alpha);
        fade.Tick(4.9f);
        Assert.Equal(1f, fade.Alpha);
    }

    [Fact]
    public void FadesOutAfterTheHoldAndThenIsHidden()
    {
        var fade = new FocusFade(5f, 0f, 1f);
        fade.Poke(0f);
        fade.Tick(0f);
        fade.Tick(5f);
        Assert.Equal(1f, fade.Alpha);
        fade.Tick(5.5f);
        Assert.InRange(fade.Alpha, 0.01f, 0.99f);
        fade.Tick(7f);
        Assert.Equal(0f, fade.Alpha);
        Assert.False(fade.IsVisible);
    }

    [Fact]
    public void AMessageDuringTheFadeOutBringsItBackWithoutWaitingForItToFinish()
    {
        var fade = new FocusFade(2f, 0.2f, 2f);
        fade.Poke(0f);
        // Step through time in frame-sized steps, as the game does, so the fade is caught part way through.
        for (var t = 0f; t <= 3f; t += 0.1f)
            fade.Tick(t);
        var midFade = fade.Alpha;
        Assert.InRange(midFade, 0.01f, 0.99f);
        fade.Poke(3.05f);
        fade.Tick(3.15f);
        Assert.True(fade.Alpha > midFade);
        for (var t = 3.25f; t <= 3.6f; t += 0.1f)
            fade.Tick(t);
        Assert.Equal(1f, fade.Alpha);
    }

    [Fact]
    public void EachMessageRestartsTheHold()
    {
        var fade = new FocusFade(3f, 0f, 0f);
        fade.Poke(0f);
        fade.Tick(2f);
        fade.Poke(2f);
        Assert.Equal(1f, fade.Tick(4.9f));
        Assert.Equal(0f, fade.Tick(5.1f));
    }

    [Fact]
    public void ARestingOpacityKeepsTheWindowFaintlyVisible()
    {
        var fade = new FocusFade(1f, 0f, 0f, restingAlpha: 0.25f);
        Assert.Equal(0.25f, fade.Alpha);
        Assert.True(fade.IsVisible);
        fade.Poke(0f);
        Assert.Equal(1f, fade.Tick(0.5f));
        Assert.Equal(0.25f, fade.Tick(2f));
    }

    [Fact]
    public void ZeroLengthFadesSwitchInstantly()
    {
        var fade = new FocusFade(1f, 0f, 0f);
        fade.Poke(0f);
        Assert.Equal(1f, fade.Tick(0f));
        Assert.Equal(0f, fade.Tick(1.5f));
    }

    [Fact]
    public void PinShowsAtOnceAndNegativeOrNonsenseInputsAreClamped()
    {
        var fade = new FocusFade(-4f, -1f, -1f, restingAlpha: 9f);
        Assert.Equal(1f, fade.Alpha);
        var other = new FocusFade(2f, 0.5f, 0.5f);
        other.Pin(10f);
        Assert.Equal(1f, other.Alpha);
        Assert.Equal(1f, other.Tick(11f));
        Assert.True(other.Tick(14f) < 1f);
    }

    [Fact]
    public void AClockThatGoesBackwardsDoesNotMoveTheFade()
    {
        var fade = new FocusFade(5f, 1f, 1f);
        fade.Poke(10f);
        fade.Tick(10f);
        fade.Tick(10.5f);
        var before = fade.Alpha;
        fade.Tick(9f);
        Assert.Equal(before, fade.Alpha);
    }
}

public class UnlockBaselineTests
{
    // The five nodes the game unlocks itself on a fresh save, as seen in a real log.
    private static readonly int[] FreshSaveNodes = { 1, 2, 3, 4, 10 };

    [Fact]
    public void NothingTheGameGaveAtTheStartIsReported()
    {
        var baseline = UnlockBaseline.TakeBaseline(FreshSaveNodes);
        foreach (var id in FreshSaveNodes)
            Assert.False(UnlockBaseline.ShouldReport(id, unlockedNow: true, baseline), $"node {id}");
    }

    [Fact]
    public void ANodeBoughtAfterTheBaselineIsReported()
    {
        var baseline = UnlockBaseline.TakeBaseline(FreshSaveNodes);
        Assert.True(UnlockBaseline.ShouldReport(5, unlockedNow: true, baseline));
        Assert.True(UnlockBaseline.ShouldReport(33, unlockedNow: true, baseline));
    }

    [Fact]
    public void ANodeThatIsNotUnlockedIsNeverReported()
    {
        var baseline = UnlockBaseline.TakeBaseline(FreshSaveNodes);
        Assert.False(UnlockBaseline.ShouldReport(5, unlockedNow: false, baseline));
        Assert.False(UnlockBaseline.ShouldReport(1, unlockedNow: false, baseline));
    }

    [Fact]
    public void AnEmptyBaselineReportsEveryUnlockedNode()
    {
        var baseline = UnlockBaseline.TakeBaseline(Array.Empty<int>());
        Assert.True(UnlockBaseline.ShouldReport(1, unlockedNow: true, baseline));
        Assert.False(UnlockBaseline.ShouldReport(1, unlockedNow: false, baseline));
    }

    [Fact]
    public void TheBaselineIsTakenOnlyUntilItHasBeenRecorded()
    {
        Assert.True(UnlockBaseline.NeedsBaseline(baselineTaken: false));
        Assert.False(UnlockBaseline.NeedsBaseline(baselineTaken: true));
    }

    [Fact]
    public void TheBaselineDoesNotChangeWhenMoreNodesAreUnlockedLater()
    {
        var baseline = UnlockBaseline.TakeBaseline(FreshSaveNodes);
        var unlockedLater = FreshSaveNodes.Concat(new[] { 5, 6 }).ToList();
        var reported = unlockedLater.Where(id => UnlockBaseline.ShouldReport(id, true, baseline)).ToList();
        Assert.Equal(new[] { 5, 6 }, reported);
        Assert.Equal(FreshSaveNodes.OrderBy(id => id), baseline.OrderBy(id => id));
    }

    // Thick Skin (1) and Slayer (10) come unlocked at level 0 on a fresh save; the rest start locked.
    private static readonly Dictionary<int, (bool Unlocked, int Levels)> FreshTree = new()
    {
        [1] = (true, 0),
        [2] = (false, 0),
        [10] = (true, 0),
        [12] = (false, 0),
    };

    [Fact]
    public void AFreeLevelZeroNodeIsNotConsideredBought()
    {
        Assert.False(UnlockBaseline.IsEmberTreeNodePurchased(unlocked: true, upgradeLevels: 0));
        Assert.False(UnlockBaseline.IsEmberTreeNodePurchased(unlocked: false, upgradeLevels: 0));
        Assert.True(UnlockBaseline.IsEmberTreeNodePurchased(unlocked: true, upgradeLevels: 1));
    }

    [Fact]
    public void TheFreshTreeBaselineLeavesThickSkinAndSlayerOut()
    {
        var baseline = UnlockBaseline.TakeEmberTreeBaseline(FreshTree);
        Assert.Empty(baseline);
    }

    [Fact]
    public void BuyingTheFirstLevelOfThickSkinAndSlayerIsReported()
    {
        var baseline = UnlockBaseline.TakeEmberTreeBaseline(FreshTree);
        Assert.False(UnlockBaseline.ShouldReportEmberTreeNode(1, true, 0, baseline));
        Assert.False(UnlockBaseline.ShouldReportEmberTreeNode(10, true, 0, baseline));
        Assert.True(UnlockBaseline.ShouldReportEmberTreeNode(1, true, 1, baseline));
        Assert.True(UnlockBaseline.ShouldReportEmberTreeNode(10, true, 1, baseline));
    }

    [Fact]
    public void ANodeThatStartsLockedIsReportedWhenBoughtLikeBefore()
    {
        var baseline = UnlockBaseline.TakeEmberTreeBaseline(FreshTree);
        Assert.False(UnlockBaseline.ShouldReportEmberTreeNode(2, false, 0, baseline));
        Assert.True(UnlockBaseline.ShouldReportEmberTreeNode(2, true, 1, baseline));
        Assert.True(UnlockBaseline.ShouldReportEmberTreeNode(12, true, 1, baseline));
    }

    [Fact]
    public void ALevelAlreadyBoughtWhenTheSeedStartedIsNeverReported()
    {
        var reusedSave = new Dictionary<int, (bool Unlocked, int Levels)>
        {
            [1] = (true, 2),
            [2] = (true, 1),
            [10] = (true, 0),
        };
        var baseline = UnlockBaseline.TakeEmberTreeBaseline(reusedSave);
        Assert.False(UnlockBaseline.ShouldReportEmberTreeNode(1, true, 2, baseline));
        Assert.False(UnlockBaseline.ShouldReportEmberTreeNode(2, true, 1, baseline));
        Assert.True(UnlockBaseline.ShouldReportEmberTreeNode(10, true, 1, baseline));
    }

    [Fact]
    public void ABoughtNodeIsStillReportedOnceItsBaselineWasTaken()
    {
        var baseline = UnlockBaseline.TakeEmberTreeBaseline(FreshTree);
        var reported = new[] { 1, 2, 10, 12 }
            .Where(id => UnlockBaseline.ShouldReportEmberTreeNode(id, true, 1, baseline))
            .ToList();
        Assert.Equal(new[] { 1, 2, 10, 12 }, reported);
    }

    [Fact]
    public void MigratingTheOldBaselineFreesThickSkinAndSlayerWhileTheyAreUnbought()
    {
        // The old baseline listed every node that was merely unlocked, so it held 1 and 10 and the node bought since (2).
        var current = new Dictionary<int, (bool Unlocked, int Levels)>
        {
            [1] = (true, 0),
            [2] = (true, 1),
            [10] = (true, 0),
        };
        var migrated = UnlockBaseline.MigrateEmberTreeBaseline(new[] { 1, 2, 10 }, current);
        Assert.Equal(new[] { 2 }, migrated.Keys.OrderBy(id => id));
        Assert.False(UnlockBaseline.ShouldReportEmberTreeNode(2, true, 1, migrated));
        Assert.True(UnlockBaseline.ShouldReportEmberTreeNode(1, true, 1, migrated));
        Assert.True(UnlockBaseline.ShouldReportEmberTreeNode(10, true, 1, migrated));
    }

    // The game's purchase call (SavedPlayerSlot.UnlockEmberTreeUpgade) is the purchase, so no baseline is involved.
    // Observed in the live log: a node is locked ("-") before the call and unlocked ("U") right after it, at level 1
    // both times, so the level cannot tell a bought node from a free one and the unlocked flag change is the signal.

    [Fact]
    public void ACallThatUnlocksALockedNodeIsAPurchase()
    {
        Assert.True(UnlockBaseline.IsEmberTreePurchase(unlockedAfterCall: true, alreadyUnlockedBefore: false));
    }

    [Fact]
    public void ACallThatLeavesTheNodeLockedIsNotAPurchase()
    {
        // A refused purchase (not enough ember, stage not open) must not send a check.
        Assert.False(UnlockBaseline.IsEmberTreePurchase(unlockedAfterCall: false, alreadyUnlockedBefore: false));
    }

    [Fact]
    public void ACallForANodeThatWasAlreadyUnlockedIsNotAPurchase()
    {
        Assert.False(UnlockBaseline.IsEmberTreePurchase(unlockedAfterCall: true, alreadyUnlockedBefore: true));
    }

    [Fact]
    public void ABoughtNodeIsReportedEvenWhenAnEarlierSnapshotAlreadyHeldIt()
    {
        // This is the bug from the oreo3 seed. The first scan ran after Thick Skin (level 6) and four others were
        // already unlocked, so the old snapshot rule excluded them for good and their purchases never sent a check.
        // The purchase call does not look at any snapshot: locked before, unlocked after is a purchase.
        var snapshotTakenLate = new Dictionary<int, int> { [1] = 6, [4] = 1, [9] = 1, [10] = 1, [12] = 1, [17] = 1 };
        foreach (var id in snapshotTakenLate.Keys)
        {
            // The old rule would have refused all of these.
            Assert.False(UnlockBaseline.ShouldReportEmberTreeNode(id, true, snapshotTakenLate[id], snapshotTakenLate));
            // The purchase rule does not consult the snapshot at all.
            Assert.True(UnlockBaseline.IsEmberTreePurchase(unlockedAfterCall: true, alreadyUnlockedBefore: false));
        }
    }

    [Fact]
    public void BuyingTheSameNodeTwiceReportsOnlyTheFirstPurchase()
    {
        var unlocked = false;
        var reports = 0;
        for (var call = 0; call < 3; call++)
        {
            var before = unlocked;
            unlocked = true; // the game unlocks the node on the first call and leaves it unlocked
            if (UnlockBaseline.IsEmberTreePurchase(unlocked, before))
                reports++;
        }
        Assert.Equal(1, reports);
    }

    [Fact]
    public void MigratingKeepsAnAlreadyBoughtFreeNodeExcludedSoItIsNotReportedTwice()
    {
        var current = new Dictionary<int, (bool Unlocked, int Levels)> { [1] = (true, 3) };
        var migrated = UnlockBaseline.MigrateEmberTreeBaseline(new[] { 1 }, current);
        Assert.False(UnlockBaseline.ShouldReportEmberTreeNode(1, true, 3, migrated));
    }

    [Fact]
    public void ABrandNewSeedIsNotConsideredAsAlreadyRun()
    {
        Assert.False(UnlockBaseline.SeedHasAlreadyRun(nextItemIndex: 0, pendingCheckCount: 0));
    }

    [Theory]
    [InlineData(12, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 3)]
    [InlineData(12, 3)]
    public void ASeedThatReceivedItemsOrHoldsPendingChecksHasAlreadyRun(int nextItemIndex, int pending)
    {
        Assert.True(UnlockBaseline.SeedHasAlreadyRun(nextItemIndex, pending));
    }

    [Fact]
    public void SessionStateStartsWithoutABaselineAndWithEmptyLists()
    {
        var state = new SessionState();
        Assert.False(state.BaselineTaken);
        Assert.False(state.BaselineSkipped);
        Assert.Empty(state.BaselineEmberTree);
        Assert.Empty(state.BaselineWeaponMods);
    }

    [Fact]
    public void AnOldStateFileWithoutTheBaselineFieldsStillLoadsAndIsNotGivenABaselineByTheParser()
    {
        // A state file written by 1.2.0 or earlier has none of the baseline fields.
        const string json = "{\"Version\":1,\"Seed\":\"123\",\"SlotName\":\"p\",\"NextItemIndex\":12,\"OrbsCollected\":0,\"RelicsRevealed\":0,\"PendingChecks\":[],\"PendingFillers\":[],\"GoalPending\":false}";
        var state = System.Text.Json.JsonSerializer.Deserialize<SessionState>(json)!;
        Assert.False(state.BaselineTaken);
        Assert.Empty(state.BaselineEmberTree);
        // The service then sees a seed that already ran, so it marks it skipped instead of giving it a baseline.
        Assert.True(UnlockBaseline.SeedHasAlreadyRun(state.NextItemIndex, state.PendingChecks.Count));
    }

    [Fact]
    public void ABaselineSurvivesASaveAndLoadRoundTrip()
    {
        var state = new SessionState { Seed = "abc", BaselineTaken = true };
        state.BaselineEmberTree.AddRange(FreshSaveNodes);
        state.BaselineWeaponMods.AddRange(new[] { 3, 9 });
        var json = System.Text.Json.JsonSerializer.Serialize(state);
        var back = System.Text.Json.JsonSerializer.Deserialize<SessionState>(json)!;
        Assert.True(back.BaselineTaken);
        Assert.Equal(FreshSaveNodes, back.BaselineEmberTree.ToArray());
        Assert.Equal(new[] { 3, 9 }, back.BaselineWeaponMods.ToArray());
    }

    [Fact]
    public void TheEmberTreeLevelBaselineSurvivesASaveAndLoadRoundTrip()
    {
        var state = new SessionState { Seed = "abc", BaselineTaken = true, EmberTreeLevelBaselineTaken = true };
        state.BaselineEmberTreeLevels[2] = 1;
        state.BaselineEmberTreeLevels[7] = 3;
        var json = System.Text.Json.JsonSerializer.Serialize(state);
        var back = System.Text.Json.JsonSerializer.Deserialize<SessionState>(json)!;
        Assert.True(back.EmberTreeLevelBaselineTaken);
        Assert.Equal(1, back.BaselineEmberTreeLevels[2]);
        Assert.Equal(3, back.BaselineEmberTreeLevels[7]);
    }

    [Fact]
    public void AnOldStateFileWithOnlyTheIdListNeedsTheLevelMigration()
    {
        const string json = "{\"Version\":1,\"Seed\":\"1\",\"SlotName\":\"p\",\"BaselineTaken\":true,\"BaselineEmberTree\":[1,10],\"BaselineWeaponMods\":[]}";
        var state = System.Text.Json.JsonSerializer.Deserialize<SessionState>(json)!;
        Assert.True(state.BaselineTaken);
        Assert.False(state.EmberTreeLevelBaselineTaken);
        Assert.Empty(state.BaselineEmberTreeLevels);
        Assert.Equal(new[] { 1, 10 }, state.BaselineEmberTree.ToArray());
    }

    [Fact]
    public void ASeedWithoutABaselineReportsEveryBoughtNode()
    {
        var empty = new Dictionary<int, int>();
        Assert.True(UnlockBaseline.ShouldReportEmberTreeNode(1, true, 1, empty));
        Assert.False(UnlockBaseline.ShouldReportEmberTreeNode(1, true, 0, empty));
    }

    [Fact]
    public void EveryFreshSaveNodeIsARealCatalogNodeSoTheBaselineHidesRealChecks()
    {
        var known = GameCatalog.Data.EmberTree.Select(node => node.DefId).ToHashSet();
        Assert.All(FreshSaveNodes, id => Assert.Contains(id, known));
    }
}

public class ConnectionSetupTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Player1")]
    [InlineData("  Player1  ")]
    public void AnEmptyOrPlaceholderSlotCountsAsNotSetUp(string? slot)
    {
        Assert.True(ConnectionSetup.IsSlotMissing(slot));
    }

    [Theory]
    [InlineData("Samus")]
    [InlineData("player1")]
    [InlineData("Player12")]
    [InlineData("My Slot")]
    public void AnyOtherSlotNameIsAcceptedIncludingNamesThatOnlyLookSimilar(string slot)
    {
        Assert.False(ConnectionSetup.IsSlotMissing(slot));
    }

    [Fact]
    public void TheMessageNamesTheConfigFileAndTheInstaller()
    {
        var message = ConnectionSetup.MissingSlotMessage(@"C:\Games\EmberKnights\BepInEx\config\com.emberknights.archipelago.cfg");
        Assert.Contains(@"C:\Games\EmberKnights\BepInEx\config\com.emberknights.archipelago.cfg", message);
        Assert.Contains("Install.bat", message);
        Assert.Contains("F6", message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WithoutAKnownPathItFallsBackToTheUsualRelativePath(string? path)
    {
        Assert.Contains("BepInEx/config/com.emberknights.archipelago.cfg", ConnectionSetup.MissingSlotMessage(path));
    }

    [Fact]
    public void TheInstallerWritesAnAutoConnectConfigOnlyWhenDetailsWereGivenSoThePlaceholderRuleMatters()
    {
        // The placeholder the plugin writes into a fresh config must be exactly the one the check looks for.
        Assert.Equal("Player1", ConnectionSetup.PlaceholderSlot);
    }
}

public class DlcPolicyTests
{
    [Fact]
    public void ASeedWithoutTheKeyOrWithFalseIsNotADlcSeed()
    {
        Assert.False(DlcPolicy.ReadDlcActive(null));
        Assert.False(DlcPolicy.ReadDlcActive(false));
        Assert.False(DlcPolicy.ReadDlcActive("false"));
        Assert.False(DlcPolicy.ReadDlcActive(0L));
        Assert.False(DlcPolicy.ReadDlcActive("nonsense"));
        Assert.False(DlcPolicy.ReadDlcActive(new object()));
    }

    [Fact]
    public void OnlyAnExplicitTrueMakesItADlcSeedWhateverTypeJsonGaveUs()
    {
        Assert.True(DlcPolicy.ReadDlcActive(true));
        Assert.True(DlcPolicy.ReadDlcActive("true"));
        Assert.True(DlcPolicy.ReadDlcActive("True"));
        Assert.True(DlcPolicy.ReadDlcActive(1L));
        Assert.True(DlcPolicy.ReadDlcActive(1));
        Assert.True(DlcPolicy.ReadDlcActive("1"));
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, false)]
    public void ADlcWeaponIsManagedOnlyWithTheDlcOnWhileABaseWeaponAlwaysIs(bool isBase, bool isDlc, bool dlcActive, bool expected)
    {
        // The Hyper Gloves and Ember Buster must never be added to or removed from a save by a seed without the DLC.
        Assert.Equal(expected, DlcPolicy.IsManagedWeapon(isBase, isDlc, dlcActive));
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, true, false)]
    public void ADlcSkillIsManagedOnlyWithTheDlcOn(bool isTablet, bool isDlc, bool dlcActive, bool expected)
    {
        Assert.Equal(expected, DlcPolicy.IsManagedAbility(isTablet, isDlc, dlcActive));
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, true, false)]
    public void ADlcOrbRelicIsManagedOnlyWithTheDlcOn(bool isBaseOrb, bool isDlcOrb, bool dlcActive, bool expected)
    {
        Assert.Equal(expected, DlcPolicy.IsManagedRelic(isBaseOrb, isDlcOrb, dlcActive));
    }

    [Fact]
    public void TheOrbCheckCountGrowsByTheDlcOrbsOnlyWithTheDlcOn()
    {
        Assert.Equal(53, DlcPolicy.OrbCheckCount(53, 10, dlcActive: false));
        Assert.Equal(63, DlcPolicy.OrbCheckCount(53, 10, dlcActive: true));
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(5, false, true)]
    [InlineData(6, false, false)]
    [InlineData(8, false, false)]
    [InlineData(11, false, false)]
    [InlineData(14, false, false)]
    [InlineData(8, true, true)]
    [InlineData(9, true, true)]
    [InlineData(10, true, true)]
    [InlineData(11, true, true)]
    [InlineData(14, true, true)]
    [InlineData(6, true, false)]
    [InlineData(7, true, false)]
    [InlineData(12, true, false)]
    [InlineData(13, true, false)]
    [InlineData(0, true, false)]
    [InlineData(15, true, false)]
    public void OnlyTheRightWorldsCountTowardRoomMilestones(int area, bool dlcActive, bool expected)
    {
        // 6 is the Nexus, 7 the credits, 12 the PAX competition and 13 Boss Rush: none of them is a milestone world.
        Assert.Equal(expected, DlcPolicy.IsTrackedWorld(area, dlcActive));
    }
}

public class DlcRoomTrackerTests
{
    private const int Battle = 1;

    [Fact]
    public void WithoutTheDlcADlcWorldIsNeverCounted()
    {
        var tracker = new RoomProgressTracker();
        Assert.Equal(0, tracker.Enter(8, 100, Battle));
        Assert.Equal(0, tracker.Enter(8, 101, Battle, dlcActive: false));
        Assert.Equal(0, tracker.Enter(14, 102, Battle, dlcActive: false));
    }

    [Fact]
    public void WithTheDlcTheDlcWorldsCountRoomsLikeBaseWorlds()
    {
        var tracker = new RoomProgressTracker();
        Assert.Equal(1, tracker.Enter(8, 100, Battle, dlcActive: true));
        Assert.Equal(2, tracker.Enter(8, 101, Battle, dlcActive: true));
        Assert.Equal(2, tracker.Enter(8, 101, Battle, dlcActive: true));
        Assert.Equal(3, tracker.Enter(8, 102, Battle, dlcActive: true));
    }

    [Fact]
    public void MovingToAnotherWorldRestartsTheDepthIncludingFromABaseWorldToADlcWorld()
    {
        var tracker = new RoomProgressTracker();
        tracker.Enter(1, 1, Battle, dlcActive: true);
        tracker.Enter(1, 2, Battle, dlcActive: true);
        Assert.Equal(1, tracker.Enter(8, 50, Battle, dlcActive: true));
    }

    [Fact]
    public void TheBaseBehaviourIsUnchangedByTheDlcOverload()
    {
        var withFlag = new RoomProgressTracker();
        var without = new RoomProgressTracker();
        foreach (var (area, room) in new[] { (1, 1), (1, 2), (1, 2), (2, 3), (2, 4), (6, 9), (3, 10) })
            Assert.Equal(without.Enter(area, room, Battle), withFlag.Enter(area, room, Battle, dlcActive: false));
    }

    [Fact]
    public void TheNexusBetweenRunsStillResetsTheDepth()
    {
        var tracker = new RoomProgressTracker();
        tracker.Enter(9, 1, Battle, dlcActive: true);
        tracker.Enter(9, 2, Battle, dlcActive: true);
        tracker.Enter(RoomProgressTracker.NexusArea, 3, Battle, dlcActive: true);
        Assert.Equal(1, tracker.Enter(9, 4, Battle, dlcActive: true));
    }
}

public class DlcCatalogTests
{
    private static readonly CatalogData Catalog = GameCatalog.Data;

    [Fact]
    public void TheDlcSectionHasExactlyTheContentTheGameReported()
    {
        Assert.Equal(10, Catalog.Dlc.Skills.Count);
        Assert.Equal(2, Catalog.Dlc.Weapons.Count);
        Assert.Equal(4, Catalog.Dlc.Minibosses.Count);
        Assert.Equal(6, Catalog.Dlc.Bosses.Count);
        Assert.Equal(19, Catalog.Dlc.WeaponMods.Count);
        Assert.Equal(18, Catalog.Dlc.RoomMilestones.Count);
        Assert.Equal(10, Catalog.Dlc.RelicOrbs.Count);
        Assert.Equal(5, Catalog.Dlc.Regions.Count);
    }

    [Fact]
    public void TheBaseSectionsAreUntouchedByTheDlc()
    {
        Assert.Equal(10, Catalog.Skills.Count);
        Assert.Equal(7, Catalog.StandardSkills.Count);
        Assert.Equal(6, Catalog.Weapons.Count);
        Assert.Equal(16, Catalog.Minibosses.Count);
        Assert.Equal(14, Catalog.Bosses.Count);
        Assert.Equal(30, Catalog.EmberTree.Count);
        Assert.Equal(87, Catalog.WeaponMods.Count);
        Assert.Equal(18, Catalog.RoomMilestones.Count);
        Assert.Equal(53, Catalog.RelicOrbs.Count);
    }

    [Fact]
    public void EveryDlcIdIsClearOfEveryBaseIdAndOfEachOther()
    {
        var baseItems = new List<long>();
        baseItems.AddRange(Enumerable.Range(0, Catalog.Skills.Count).Select(Catalog.SkillItemId));
        baseItems.AddRange(Enumerable.Range(0, Catalog.StandardSkills.Count).Select(Catalog.StandardSkillItemId));
        baseItems.AddRange(Enumerable.Range(0, Catalog.Weapons.Count).Select(Catalog.WeaponItemId));
        baseItems.AddRange(Enumerable.Range(0, Catalog.Fillers.Count).Select(Catalog.FillerItemId));
        baseItems.AddRange(Enumerable.Range(0, Catalog.RelicOrbs.Count).Select(Catalog.RelicItemId));
        baseItems.Add(Catalog.HeatReductionItemId);
        var dlcItems = new List<long>();
        dlcItems.AddRange(Enumerable.Range(0, 10).Select(Catalog.DlcSkillItemId));
        dlcItems.AddRange(Enumerable.Range(0, 2).Select(Catalog.DlcWeaponItemId));
        dlcItems.AddRange(Enumerable.Range(0, 10).Select(Catalog.DlcRelicItemId));
        Assert.Empty(baseItems.Intersect(dlcItems));
        Assert.Equal(dlcItems.Count, dlcItems.Distinct().Count());

        var baseLocations = new List<long>();
        baseLocations.AddRange(Enumerable.Range(0, Catalog.Skills.Count).Select(Catalog.SkillLocationId));
        baseLocations.AddRange(Enumerable.Range(0, Catalog.Minibosses.Count).Select(Catalog.MinibossLocationId));
        baseLocations.AddRange(Enumerable.Range(0, Catalog.Bosses.Count).Select(Catalog.BossLocationId));
        baseLocations.AddRange(Enumerable.Range(0, Catalog.EmberTree.Count).Select(Catalog.EmberTreeLocationId));
        baseLocations.AddRange(Enumerable.Range(0, Catalog.WeaponMods.Count).Select(Catalog.WeaponModLocationId));
        baseLocations.AddRange(Enumerable.Range(0, Catalog.RoomMilestones.Count).Select(Catalog.RoomMilestoneLocationId));
        baseLocations.AddRange(Enumerable.Range(0, Catalog.RelicOrbs.Count).Select(Catalog.RelicOrbLocationId));
        var dlcLocations = new List<long>();
        dlcLocations.AddRange(Enumerable.Range(0, 10).Select(Catalog.DlcSkillLocationId));
        dlcLocations.AddRange(Enumerable.Range(0, 4).Select(Catalog.DlcMinibossLocationId));
        dlcLocations.AddRange(Enumerable.Range(0, 6).Select(Catalog.DlcBossLocationId));
        dlcLocations.AddRange(Enumerable.Range(0, 19).Select(Catalog.DlcWeaponModLocationId));
        dlcLocations.AddRange(Enumerable.Range(0, 18).Select(Catalog.DlcRoomMilestoneLocationId));
        dlcLocations.AddRange(Enumerable.Range(0, 10).Select(Catalog.DlcRelicOrbLocationId));
        Assert.Empty(baseLocations.Intersect(dlcLocations));
        Assert.Equal(dlcLocations.Count, dlcLocations.Distinct().Count());
    }

    [Fact]
    public void TheIdOffsetsAreExactlyTheOnesTheWorldUses()
    {
        // These numbers are written into every generated seed. They must never drift from catalog.py.
        Assert.Equal(Catalog.IdBase + 500L, Catalog.DlcSkillItemId(0));
        Assert.Equal(Catalog.IdBase + 520L, Catalog.DlcWeaponItemId(0));
        Assert.Equal(Catalog.IdBase + 540L, Catalog.DlcRelicItemId(0));
        Assert.Equal(Catalog.IdBase + 8000L, Catalog.DlcSkillLocationId(0));
        Assert.Equal(Catalog.IdBase + 8100L, Catalog.DlcMinibossLocationId(0));
        Assert.Equal(Catalog.IdBase + 8200L, Catalog.DlcBossLocationId(0));
        Assert.Equal(Catalog.IdBase + 8300L, Catalog.DlcWeaponModLocationId(0));
        Assert.Equal(Catalog.IdBase + 8400L, Catalog.DlcRoomMilestoneLocationId(0));
        Assert.Equal(Catalog.IdBase + 8500L, Catalog.DlcRelicOrbLocationId(0));
    }

    [Fact]
    public void DlcOrbsAreNumberedAfterTheBaseOrbs()
    {
        var names = Catalog.Dlc.RelicOrbs.Select(orb => orb.LocationName).ToList();
        Assert.Equal(Enumerable.Range(54, 10).Select(n => $"Relic Orb {n}").ToList(), names);
        Assert.Equal("Relic Orb 01", Catalog.RelicOrbs[0].LocationName);
        Assert.Equal("Relic Orb 53", Catalog.RelicOrbs[^1].LocationName);
    }

    [Fact]
    public void EveryDlcNameResolvesSoNothingIsEverShownAsAnItemNumber()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(Catalog.Dlc.Skills[i].Name, Catalog.GetItemName(Catalog.DlcSkillItemId(i)));
            Assert.Equal(Catalog.Dlc.Skills[i].LocationName, Catalog.GetLocationName(Catalog.DlcSkillLocationId(i)));
            Assert.Equal(Catalog.Dlc.RelicOrbs[i].Name, Catalog.GetItemName(Catalog.DlcRelicItemId(i)));
            Assert.Equal(Catalog.Dlc.RelicOrbs[i].LocationName, Catalog.GetLocationName(Catalog.DlcRelicOrbLocationId(i)));
        }
        Assert.Equal("Hyper Gloves", Catalog.GetItemName(Catalog.DlcWeaponItemId(0)));
        Assert.Equal("Ember Buster", Catalog.GetItemName(Catalog.DlcWeaponItemId(1)));
        Assert.Equal("Oukranos Defeated", Catalog.GetLocationName(Catalog.DlcBossLocationId(0)));
    }

    [Fact]
    public void ADlcLookupNeverReturnsABaseEntryAndABaseLookupNeverReturnsADlcOne()
    {
        foreach (var skill in Catalog.Dlc.Skills)
        {
            Assert.Null(Catalog.SkillForAbility(skill.AbilityId));
            Assert.NotNull(Catalog.DlcSkillForAbility(skill.AbilityId));
        }
        foreach (var skill in Catalog.Skills)
            Assert.Null(Catalog.DlcSkillForAbility(skill.AbilityId));
        foreach (var weapon in Catalog.Dlc.Weapons)
        {
            Assert.False(Catalog.IsRandomizedWeapon(weapon.WeaponId));
            Assert.True(Catalog.IsDlcWeapon(weapon.WeaponId));
        }
        foreach (var weapon in Catalog.Weapons)
            Assert.False(Catalog.IsDlcWeapon(weapon.WeaponId));
        foreach (var orb in Catalog.Dlc.RelicOrbs)
        {
            Assert.False(Catalog.IsOrbRelic(orb.DefId));
            Assert.True(Catalog.IsDlcOrbRelic(orb.DefId));
        }
        foreach (var orb in Catalog.RelicOrbs)
            Assert.False(Catalog.IsDlcOrbRelic(orb.DefId));
        foreach (var encounter in Catalog.Dlc.Bosses.Concat(Catalog.Dlc.Minibosses))
        {
            foreach (var enemy in encounter.EnemyIds)
            {
                Assert.Null(Catalog.BossForEnemy(enemy));
                Assert.Null(Catalog.MinibossForEnemy(enemy));
            }
        }
    }

    [Fact]
    public void ADlcItemIdIsNeverMistakenForABaseItem()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.Null(Catalog.SkillForItem(Catalog.DlcSkillItemId(i)));
            Assert.Null(Catalog.StandardSkillForItem(Catalog.DlcSkillItemId(i)));
            Assert.Null(Catalog.RelicForItem(Catalog.DlcRelicItemId(i)));
            Assert.NotNull(Catalog.DlcSkillForItem(Catalog.DlcSkillItemId(i)));
            Assert.NotNull(Catalog.DlcRelicForItem(Catalog.DlcRelicItemId(i)));
        }
        for (var i = 0; i < 2; i++)
        {
            Assert.Null(Catalog.WeaponForItem(Catalog.DlcWeaponItemId(i)));
            Assert.NotNull(Catalog.DlcWeaponForItem(Catalog.DlcWeaponItemId(i)));
        }
        Assert.Null(Catalog.DlcSkillForItem(Catalog.SkillItemId(0)));
        Assert.Null(Catalog.DlcWeaponForItem(Catalog.WeaponItemId(0)));
        Assert.Null(Catalog.DlcRelicForItem(Catalog.RelicItemId(0)));
    }

    [Fact]
    public void DlcRelicIdsAreTheTenTheGameListedAndTheArchitectHasAReborn()
    {
        Assert.Equal(Enumerable.Range(211, 10).ToArray(), Catalog.Dlc.RelicOrbs.Select(orb => orb.DefId).ToArray());
        Assert.Equal(new[] { 8, 9 }, Catalog.Dlc.Weapons.Select(weapon => weapon.WeaponId).ToArray());
        var architect = Catalog.Dlc.Bosses.Single(boss => boss.Name == "The Architect");
        var reborn = Catalog.Dlc.Bosses.Single(boss => boss.Name == "The Architect Reborn");
        Assert.False(architect.Variant);
        Assert.True(reborn.Variant);
        Assert.Equal(new[] { 149 }, architect.EnemyIds);
        Assert.Equal(new[] { 161 }, reborn.EnemyIds);
    }

    [Fact]
    public void ADlcMilestoneOnlyMatchesItsOwnWorldsAreaNumber()
    {
        Assert.Contains(Catalog.DlcMilestonesFor(8, 3), m => m.World == "Mystic Forest" && m.Room == 3);
        Assert.Empty(Catalog.DlcMilestonesFor(1, 7));
        Assert.Empty(Catalog.MilestonesFor(8, 7));
        Assert.Equal(new[] { 8, 9, 10, 11, 14 }, Catalog.Dlc.RoomMilestones.Select(m => m.AreaName).Distinct().ToArray());
    }

    [Fact]
    public void TheDlcOrbCheckIsOnlyReachableForOrbNumbersOneToTen()
    {
        Assert.Null(Catalog.LocationForDlcRelicOrb(0));
        Assert.NotNull(Catalog.LocationForDlcRelicOrb(1));
        Assert.NotNull(Catalog.LocationForDlcRelicOrb(10));
        Assert.Null(Catalog.LocationForDlcRelicOrb(11));
        Assert.Equal(Catalog.DlcRelicOrbLocationId(0), Catalog.LocationForDlcRelicOrb(1));
    }
}

public class DlcSessionStateTests
{
    [Fact]
    public void AStateFileFromBeforeTheDlcLoadsWithSafeDefaults()
    {
        const string json = "{\"Version\":1,\"Seed\":\"123\",\"SlotName\":\"p\",\"NextItemIndex\":12,\"OrbsCollected\":7,\"RelicsRevealed\":3,\"PendingChecks\":[],\"PendingFillers\":[],\"GoalPending\":false}";
        var state = System.Text.Json.JsonSerializer.Deserialize<SessionState>(json)!;
        Assert.Equal(0, state.DlcOrbsCollected);
        Assert.Equal(0, state.DlcRelicsRevealed);
        Assert.False(state.DlcBaselineTaken);
        Assert.Empty(state.BaselineDlcWeaponMods);
        // The base counters are untouched.
        Assert.Equal(7, state.OrbsCollected);
        Assert.Equal(3, state.RelicsRevealed);
    }

    [Fact]
    public void TheDlcCountersSurviveASaveAndLoad()
    {
        var state = new SessionState { Seed = "s", DlcOrbsCollected = 4, DlcRelicsRevealed = 2, DlcBaselineTaken = true };
        state.BaselineDlcWeaponMods.AddRange(new[] { 99, 100, 108 });
        var back = System.Text.Json.JsonSerializer.Deserialize<SessionState>(System.Text.Json.JsonSerializer.Serialize(state))!;
        Assert.Equal(4, back.DlcOrbsCollected);
        Assert.Equal(2, back.DlcRelicsRevealed);
        Assert.True(back.DlcBaselineTaken);
        Assert.Equal(new[] { 99, 100, 108 }, back.BaselineDlcWeaponMods.ToArray());
    }
}
public class StartingRelicTests
{
    private static readonly CatalogData Catalog = GameCatalog.Data;

    private static bool IsKnown(int id) =>
        Catalog.IsDefaultRelic(id) || Catalog.IsOrbRelic(id) || Catalog.IsDlcOrbRelic(id);

    [Fact]
    public void TheCatalogHoldsTheFortyThreeDefaultRelicsAndNoOrbRelicIsOneOfThem()
    {
        Assert.Equal(43, Catalog.DefaultRelics.Count);
        Assert.Equal(43, Catalog.DefaultRelics.Select(relic => relic.DefId).Distinct().Count());
        Assert.All(Catalog.DefaultRelics, relic =>
        {
            Assert.True(Catalog.IsDefaultRelic(relic.DefId), relic.Name);
            Assert.False(Catalog.IsOrbRelic(relic.DefId), relic.Name);
            Assert.False(Catalog.IsDlcOrbRelic(relic.DefId), relic.Name);
        });
        // Chronograph is an orb relic, not a default relic.
        Assert.False(Catalog.IsDefaultRelic(40));
        Assert.True(Catalog.IsDefaultRelic(17));
    }

    [Fact]
    public void DefaultRelicItemIdsFollowTheSharedSchemeAndMoveNothing()
    {
        Assert.Equal(Catalog.IdBase + 600, Catalog.DefaultRelicItemId(0));
        Assert.Equal(Catalog.IdBase + 642, Catalog.DefaultRelicItemId(42));
        Assert.Equal("Volatile Grimlock", Catalog.DefaultRelicForItem(Catalog.IdBase + 600)!.Name);
        Assert.Equal(43, Enumerable.Range(0, 43).Count(i => Catalog.DefaultRelicForItem(Catalog.DefaultRelicItemId(i)) is not null));
        Assert.Null(Catalog.DefaultRelicForItem(Catalog.IdBase + 599));
        Assert.Null(Catalog.DefaultRelicForItem(Catalog.IdBase + 643));
        // The orb relic and DLC relic item ranges still resolve where they always did.
        Assert.Equal(48, Catalog.RelicForItem(Catalog.IdBase + 300)!.DefId);
        Assert.Equal(211, Catalog.DlcRelicForItem(Catalog.IdBase + 540)!.DefId);
        Assert.Null(Catalog.RelicForItem(Catalog.IdBase + 600));
        Assert.Equal("Bone Plate", Catalog.GetItemName(Catalog.DefaultRelicItemId(1)));
    }

    [Fact]
    public void ASeedWithoutTheKeyLeavesEveryDefaultRelicUnlockedAsInTheBaseGame()
    {
        Assert.Null(StartingRelicPolicy.ReadStartingRelics(null, IsKnown));
        Assert.Null(StartingRelicPolicy.ReadStartingRelics("17", IsKnown));
        Assert.False(StartingRelicPolicy.IsGated(isDefaultRelic: true, starting: null));
    }

    [Fact]
    public void APresentKeyEvenAnEmptyOneTurnsGatingOn()
    {
        var empty = StartingRelicPolicy.ReadStartingRelics(new List<object>(), IsKnown);
        Assert.NotNull(empty);
        Assert.True(StartingRelicPolicy.IsGated(isDefaultRelic: true, starting: empty));
        // Orb relics are managed by their own rule, not by this one.
        Assert.False(StartingRelicPolicy.IsGated(isDefaultRelic: false, starting: empty));
    }

    [Fact]
    public void UnknownAndNonNumericIdsAreIgnored()
    {
        var starting = StartingRelicPolicy.ReadStartingRelics(new List<object> { 17L, 40L, 9999L, "oops", 211L }, IsKnown)!;
        Assert.Equal(new[] { 17, 40, 211 }, starting.OrderBy(id => id).ToArray());
    }

    [Fact]
    public void AStartingRelicIsUnlockedAndTheOthersWaitForTheMultiworld()
    {
        var starting = new HashSet<int> { 17, 40 };
        var received = new HashSet<int> { 58 };
        Assert.True(StartingRelicPolicy.IsUnlocked(17, starting, received));
        Assert.True(StartingRelicPolicy.IsUnlocked(40, starting, received));
        Assert.True(StartingRelicPolicy.IsUnlocked(58, starting, received));
        Assert.False(StartingRelicPolicy.IsUnlocked(6, starting, received));
        Assert.False(StartingRelicPolicy.IsUnlocked(48, starting, received));
    }

    [Fact]
    public void AnUngatedSeedReportsNothingAsStartingAndNothingIsOwnedUntilReceived()
    {
        Assert.False(StartingRelicPolicy.IsUnlocked(17, null, new HashSet<int>()));
        Assert.True(StartingRelicPolicy.IsUnlocked(17, null, new HashSet<int> { 17 }));
    }
}
public class FirstShopPolicyTests
{
    private const int Apple = 1;
    private const int Relic = 7;
    private const int Potion = 16;
    private const int ApplePie = 26;

    [Fact]
    public void AForcedRelicSlotIsCleared()
    {
        // Bone Plate (17) and Tainted Conduit (83) in the scripted first shop.
        Assert.True(FirstShopPolicy.ShouldClearOverride(hasFtueOverrides: true, dropType: Relic));
    }

    [Fact]
    public void TheForcedFreeApplePieIsKept()
    {
        // Slot 0 of the scripted shop is a free Apple Pie, a tutorial gift.
        Assert.False(FirstShopPolicy.ShouldClearOverride(hasFtueOverrides: true, dropType: ApplePie));
        Assert.False(FirstShopPolicy.ShouldClearOverride(hasFtueOverrides: true, dropType: Apple));
        Assert.False(FirstShopPolicy.ShouldClearOverride(hasFtueOverrides: true, dropType: Potion));
    }

    [Fact]
    public void OrdinaryEntriesAreNeverTouched()
    {
        // The normal shops have no forced items, so a relic entry without overrides is left exactly as it is.
        Assert.False(FirstShopPolicy.ShouldClearOverride(hasFtueOverrides: false, dropType: Relic));
        Assert.False(FirstShopPolicy.ShouldClearOverride(hasFtueOverrides: false, dropType: ApplePie));
    }

    [Fact]
    public void TheRelicDropTypeMatchesTheGamesEnum()
    {
        // eDropType.Relic = 7 in the decompiled game code.
        Assert.Equal(7, FirstShopPolicy.RelicDropType);
    }
}
public class TrapSpawnPolicyTests
{
    [Fact]
    public void ASingleEnemyStandsExactlyWhereTheTrapWouldHavePutIt()
    {
        var offsets = TrapSpawnPolicy.GridOffsets(1);
        Assert.Single(offsets);
        Assert.Equal((0f, 0f), offsets[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(26)]
    public void EveryEnemyGetsItsOwnCell(int count)
    {
        var offsets = TrapSpawnPolicy.GridOffsets(count);
        Assert.Equal(count, offsets.Length);
        Assert.Equal(count, offsets.Distinct().Count());
    }

    [Fact]
    public void NoTwoEnemiesAreCloserThanOneGridStep()
    {
        var offsets = TrapSpawnPolicy.GridOffsets(20);
        for (var a = 0; a < offsets.Length; a++)
        {
            for (var b = a + 1; b < offsets.Length; b++)
            {
                var dx = offsets[a].X - offsets[b].X;
                var dy = offsets[a].Y - offsets[b].Y;
                Assert.True(Math.Sqrt(dx * dx + dy * dy) >= TrapSpawnPolicy.GridSpacing - 0.001f, $"{a} and {b} overlap");
            }
        }
    }

    [Fact]
    public void TheGridFillsOutwardFromTheMiddleNearestFirst()
    {
        var offsets = TrapSpawnPolicy.GridOffsets(9);
        var distances = offsets.Select(o => o.X * o.X + o.Y * o.Y).ToArray();
        Assert.Equal(distances.OrderBy(d => d).ToArray(), distances);
        // Nine enemies make a full 3 by 3 block, so nothing is further than one step on either axis.
        Assert.All(offsets, o =>
        {
            Assert.True(Math.Abs(o.X) <= TrapSpawnPolicy.GridSpacing + 0.001f);
            Assert.True(Math.Abs(o.Y) <= TrapSpawnPolicy.GridSpacing + 0.001f);
        });
    }

    [Fact]
    public void TheLayoutIsTheSameEveryTime()
    {
        Assert.Equal(TrapSpawnPolicy.GridOffsets(12), TrapSpawnPolicy.GridOffsets(12));
    }

    [Fact]
    public void ACustomSpacingScalesTheGrid()
    {
        var offsets = TrapSpawnPolicy.GridOffsets(2, spacing: 10f);
        Assert.Equal(10f, Math.Max(Math.Abs(offsets[1].X), Math.Abs(offsets[1].Y)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NothingToPlaceGivesAnEmptyLayout(int count)
    {
        Assert.Empty(TrapSpawnPolicy.GridOffsets(count));
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(2, false, true)]
    [InlineData(3, false, true)]
    [InlineData(4, false, true)]
    [InlineData(5, false, true)]
    [InlineData(8, false, false)]
    [InlineData(9, false, false)]
    [InlineData(10, false, false)]
    [InlineData(11, false, false)]
    [InlineData(14, false, false)]
    [InlineData(8, true, true)]
    [InlineData(9, true, true)]
    [InlineData(10, true, true)]
    [InlineData(11, true, true)]
    [InlineData(14, true, true)]
    [InlineData(6, true, false)]
    [InlineData(7, true, false)]
    [InlineData(12, true, false)]
    [InlineData(13, true, false)]
    [InlineData(0, true, false)]
    public void OnlyRunWorldsAreEligibleAndTheDlcWorldsNeedTheDlc(int area, bool dlc, bool expected)
    {
        Assert.Equal(expected, TrapSpawnPolicy.IsEligibleWorld(area, dlc));
    }

    private static bool Eligible(
        bool enabled = true, bool dlcContent = false, int area = 1, bool boss = false, bool mini = false,
        bool baseRank = true, bool stationary = false, bool clone = false, int max = -1, bool dlc = false) =>
        TrapSpawnPolicy.IsEligibleEnemy(enabled, dlcContent, area, boss, mini, baseRank, stationary, clone, max, dlc);

    [Fact]
    public void AnOrdinaryBaseEnemyIsEligible()
    {
        Assert.True(Eligible());
        Assert.True(Eligible(area: 5));
    }

    [Fact]
    public void BossesMinibossesVariantsHatcheriesAndClonesAreNeverDrawn()
    {
        Assert.False(Eligible(boss: true));
        Assert.False(Eligible(mini: true));
        Assert.False(Eligible(baseRank: false));
        Assert.False(Eligible(stationary: true));
        Assert.False(Eligible(clone: true));
        Assert.False(Eligible(enabled: false));
        Assert.False(Eligible(max: 0));
    }

    [Fact]
    public void ADlcEnemyIsOnlyDrawnWhenTheSeedUsesTheDlc()
    {
        Assert.False(Eligible(dlcContent: true, area: 9, dlc: false));
        Assert.True(Eligible(dlcContent: true, area: 9, dlc: true));
        // A DLC flag on an enemy whose area is a base world is still held back without the DLC.
        Assert.False(Eligible(dlcContent: true, area: 1, dlc: false));
    }

    [Fact]
    public void ADlcWorldEnemyIsRejectedWithoutTheDlcEvenIfItIsNotFlagged()
    {
        Assert.False(Eligible(dlcContent: false, area: 10, dlc: false));
        Assert.True(Eligible(dlcContent: false, area: 10, dlc: true));
    }

    [Fact]
    public void EnemiesOutsideTheRunWorldsAreNeverDrawn()
    {
        Assert.False(Eligible(area: 0, dlc: true));
        Assert.False(Eligible(area: 6, dlc: true));
        Assert.False(Eligible(area: 12, dlc: true));
        Assert.False(Eligible(area: 13, dlc: true));
    }

    [Fact]
    public void PickingFromAnEmptyPoolGivesNothing()
    {
        Assert.Equal(-1, TrapSpawnPolicy.PickIndex(0, _ => 0));
        Assert.Equal(-1, TrapSpawnPolicy.PickIndex(-2, _ => 0));
        Assert.Equal(3, TrapSpawnPolicy.PickIndex(10, n => { Assert.Equal(10, n); return 3; }));
    }

    [Fact]
    public void ASeedWithoutTheKeyKeepsTrapEnemiesInTheCurrentWorld()
    {
        Assert.False(TrapSpawnPolicy.ReadAnyWorld(null));
        Assert.False(TrapSpawnPolicy.ReadAnyWorld(false));
        Assert.False(TrapSpawnPolicy.ReadAnyWorld(0L));
        Assert.False(TrapSpawnPolicy.ReadAnyWorld("false"));
        Assert.False(TrapSpawnPolicy.ReadAnyWorld(new object()));
    }

    [Fact]
    public void TheOptionIsReadFromBoolNumberAndTextForms()
    {
        Assert.True(TrapSpawnPolicy.ReadAnyWorld(true));
        Assert.True(TrapSpawnPolicy.ReadAnyWorld(1L));
        Assert.True(TrapSpawnPolicy.ReadAnyWorld(1));
        Assert.True(TrapSpawnPolicy.ReadAnyWorld("true"));
        Assert.True(TrapSpawnPolicy.ReadAnyWorld("True"));
        Assert.True(TrapSpawnPolicy.ReadAnyWorld("1"));
    }
}
public class TitleStatusTextTests
{
    [Fact]
    public void ConnectedShowsTheModVersionAndTheSlot()
    {
        var line = TitleStatusText.Build("1.5.0", isConnected: true, isActive: true, slotName: "Oreo");
        Assert.StartsWith("Archipelago Mod 1.5.0", line);
        Assert.Contains("Connected", line);
        Assert.Contains("as Oreo", line);
    }

    [Fact]
    public void NotConnectedTellsThePlayerHowToConnect()
    {
        var line = TitleStatusText.Build("1.5.0", isConnected: false, isActive: false, slotName: "Oreo");
        Assert.Contains("Not connected", line);
        Assert.Contains("F6", line);
        Assert.DoesNotContain("Connected</color>", line);
    }

    [Fact]
    public void ADroppedLinkReadsAsReconnectingNotOffline()
    {
        var line = TitleStatusText.Build("1.5.0", isConnected: false, isActive: true, slotName: "Oreo");
        Assert.Contains("Reconnecting", line);
        Assert.DoesNotContain("Not connected", line);
    }

    [Fact]
    public void TheServerAddressNeverAppears()
    {
        // The line is built from state and the slot only, so there is no way to pass an address in; this guards the
        // wording against ever growing one.
        foreach (var (connected, active) in new[] { (true, true), (false, true), (false, false) })
        {
            var line = TitleStatusText.Build("1.5.0", connected, active, "Oreo");
            Assert.DoesNotContain("archipelago.gg", line);
            Assert.DoesNotContain("38281", line);
            Assert.DoesNotContain("@", line);
        }
    }

    [Fact]
    public void AMissingSlotNameStillMakesAReadableLine()
    {
        foreach (var slot in new string?[] { null, "", "   " })
        {
            var connected = TitleStatusText.Build("1.5.0", true, true, slot);
            Assert.EndsWith("Connected</color>", connected);
            var offline = TitleStatusText.Build("1.5.0", false, false, slot);
            Assert.EndsWith("(F6 to connect)", offline);
        }
    }

    [Fact]
    public void ASlotNameCannotInjectRichTextTags()
    {
        var line = TitleStatusText.Build("1.5.0", true, true, "<size=500>Oreo</size><color=red>");
        // Only the tags the mod wrote itself remain: one colour tag pair around the status word.
        Assert.Equal(1, CountOccurrences(line, "<color="));
        Assert.Equal(1, CountOccurrences(line, "</color>"));
        Assert.DoesNotContain("<size", line);
    }

    [Fact]
    public void ALongSlotNameIsShortenedToFitOneLine()
    {
        var cleaned = TitleStatusText.CleanSlot(new string('x', 80));
        Assert.True(cleaned.Length <= 24);
        Assert.EndsWith("…", cleaned);
        Assert.Equal("Oreo", TitleStatusText.CleanSlot("  Oreo  "));
    }

    [Fact]
    public void TheTextTagsAreAlwaysBalanced()
    {
        foreach (var (connected, active) in new[] { (true, true), (false, true), (false, false) })
        {
            var line = TitleStatusText.Build("1.5.0", connected, active, "Oreo");
            Assert.Equal(CountOccurrences(line, "<color="), CountOccurrences(line, "</color>"));
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}

public class SelectorTopUpPolicyTests
{
    private static int First(int count) => 0;

    [Fact]
    public void AFullSelectorIsLeftExactlyAsTheGameMadeIt()
    {
        Assert.Empty(SelectorTopUpPolicy.ChooseAdditions(new[] { 1, 2, 3 }, new[] { 4, 5, 6 }, First));
        Assert.Empty(SelectorTopUpPolicy.ChooseAdditions(new[] { 1, 2, 3, 4 }, new[] { 5 }, First));
    }

    [Fact]
    public void AShortSelectorIsFilledUpToThree()
    {
        var added = SelectorTopUpPolicy.ChooseAdditions(new[] { 10 }, new[] { 20, 30, 40, 50 }, First);
        Assert.Equal(2, added.Count);
        Assert.Equal(new[] { 20, 30 }, added);
    }

    [Fact]
    public void ARelicAlreadyOnOfferIsNeverAddedTwice()
    {
        var added = SelectorTopUpPolicy.ChooseAdditions(new[] { 10, 20 }, new[] { 10, 20, 30 }, First);
        Assert.Equal(new[] { 30 }, added);
    }

    [Fact]
    public void ADuplicateCandidateIsOnlyUsedOnce()
    {
        var added = SelectorTopUpPolicy.ChooseAdditions(new[] { 10 }, new[] { 20, 20, 20, 30 }, First);
        Assert.Equal(new[] { 20, 30 }, added);
    }

    [Fact]
    public void WithTooFewCandidatesItAddsWhatItCanAndNeverInventsOne()
    {
        var added = SelectorTopUpPolicy.ChooseAdditions(new[] { 10 }, new[] { 20 }, First);
        Assert.Equal(new[] { 20 }, added);
        Assert.Empty(SelectorTopUpPolicy.ChooseAdditions(new[] { 10 }, Array.Empty<int>(), First));
    }

    [Fact]
    public void AnEmptySelectorCanBeFilledCompletely()
    {
        var added = SelectorTopUpPolicy.ChooseAdditions(Array.Empty<int>(), new[] { 5, 6, 7, 8 }, First);
        Assert.Equal(3, added.Count);
    }

    [Fact]
    public void TheChoiceUsesTheSuppliedRandomSourceAndStaysInRange()
    {
        var seen = new List<int>();
        var added = SelectorTopUpPolicy.ChooseAdditions(new[] { 1 }, new[] { 10, 20, 30, 40 }, count =>
        {
            seen.Add(count);
            return count - 1; // always the last one left
        });
        Assert.Equal(new[] { 40, 30 }, added);
        Assert.Equal(new[] { 4, 3 }, seen);
    }

    [Fact]
    public void ARelicTheGameNeverFinishedCheckingIsNotAllowed()
    {
        // allowed only when owned, and the max-owned, unlock and weapon checks all said yes, and category did not say no
        Assert.True(SelectorTopUpPolicy.IsAllowed(true, true, true, true, categoryAskedAndFailed: false));
        Assert.False(SelectorTopUpPolicy.IsAllowed(false, true, true, true, false));
        Assert.False(SelectorTopUpPolicy.IsAllowed(true, false, true, true, false));
        Assert.False(SelectorTopUpPolicy.IsAllowed(true, true, false, true, false));
        Assert.False(SelectorTopUpPolicy.IsAllowed(true, true, true, false, false));
        Assert.False(SelectorTopUpPolicy.IsAllowed(true, true, true, true, categoryAskedAndFailed: true));
    }

    [Fact]
    public void TheTargetIsThree()
    {
        Assert.Equal(3, SelectorTopUpPolicy.TargetCards);
    }
}
public class SelectorTopUpCandidateTests
{
    private static SelectorTopUpPolicy.Verdicts Full => new(true, true, true, null);
    private static SelectorTopUpPolicy.Verdicts WithCategory(bool ok) => new(true, true, true, ok);

    [Fact]
    public void ARelicThatPassedEveryCheckIsACandidate()
    {
        Assert.True(SelectorTopUpPolicy.PassedEverything(Full));
        // Category is only asked about relics that have a requirement; passing it is fine.
        Assert.True(SelectorTopUpPolicy.PassedEverything(WithCategory(true)));
    }

    [Fact]
    public void ARelicTheGameRejectedIsNeverACandidate()
    {
        Assert.False(SelectorTopUpPolicy.PassedEverything(WithCategory(false)));
        Assert.False(SelectorTopUpPolicy.PassedEverything(new(false, true, true, null)));
        Assert.False(SelectorTopUpPolicy.PassedEverything(new(true, false, true, null)));
        Assert.False(SelectorTopUpPolicy.PassedEverything(new(true, true, false, null)));
    }

    [Fact]
    public void ARelicTheGameStoppedAskingAboutIsNeverACandidate()
    {
        // Seen in play: relics whose required category was not met were asked max-owned and unlock, never the weapon check.
        Assert.False(SelectorTopUpPolicy.PassedEverything(new(true, true, null, null)));
        Assert.False(SelectorTopUpPolicy.PassedEverything(new(true, true, null, false)));
        Assert.False(SelectorTopUpPolicy.PassedEverything(new(true, null, null, null)));
        Assert.False(SelectorTopUpPolicy.PassedEverything(new(null, null, null, null)));
    }

    [Fact]
    public void CandidatesAreOwnedPassedAndNotAlreadyOfferedInIdOrder()
    {
        // Mirrors the run that showed 3 cards from 9 passing relics: 75, 103 and 179 offered.
        var byRelic = new Dictionary<int, SelectorTopUpPolicy.Verdicts>
        {
            [179] = Full, [62] = Full, [75] = Full, [103] = Full, [114] = WithCategory(true), [132] = Full,
            [102] = new(true, true, null, false),      // rejected on category
            [110] = new(true, true, null, null),       // stopped early
            [154] = new(true, null, null, null),       // stopped very early
        };
        var owned = new HashSet<int> { 62, 75, 102, 103, 110, 114, 132, 154, 179 };
        var candidates = SelectorTopUpPolicy.Candidates(byRelic, owned.Contains, new[] { 75, 103, 179 });
        Assert.Equal(new[] { 62, 114, 132 }, candidates);
    }

    [Fact]
    public void ARelicThePlayerDoesNotOwnIsNeverACandidateWhateverTheChecksSaid()
    {
        var byRelic = new Dictionary<int, SelectorTopUpPolicy.Verdicts> { [5] = Full, [6] = Full };
        var candidates = SelectorTopUpPolicy.Candidates(byRelic, id => id == 6, Array.Empty<int>());
        Assert.Equal(new[] { 6 }, candidates);
    }

    [Fact]
    public void NoCandidatesMeansNothingIsAdded()
    {
        var byRelic = new Dictionary<int, SelectorTopUpPolicy.Verdicts> { [1] = new(true, true, null, false) };
        var candidates = SelectorTopUpPolicy.Candidates(byRelic, _ => true, new[] { 7 });
        Assert.Empty(candidates);
        Assert.Empty(SelectorTopUpPolicy.ChooseAdditions(new[] { 7 }, candidates, _ => 0));
    }

    [Fact]
    public void ShortRunCIsFilledFromWhatPassed()
    {
        // The two-card selector from the logs: 122 and 125 offered, 123 was the other relic that passed everything.
        var byRelic = new Dictionary<int, SelectorTopUpPolicy.Verdicts>
        {
            [122] = Full, [123] = Full, [125] = Full,
            [59] = new(true, true, null, null), [127] = new(true, true, null, null), [158] = new(true, true, null, null),
        };
        var offered = new[] { 122, 125 };
        var candidates = SelectorTopUpPolicy.Candidates(byRelic, _ => true, offered);
        var added = SelectorTopUpPolicy.ChooseAdditions(offered, candidates, _ => 0);
        Assert.Equal(new[] { 123 }, added);
    }

    [Fact]
    public void AFullSelectorGetsNothingEvenWithPlentyOfCandidates()
    {
        var byRelic = Enumerable.Range(1, 20).ToDictionary(i => i, _ => Full);
        var offered = new[] { 1, 2, 3 };
        var candidates = SelectorTopUpPolicy.Candidates(byRelic, _ => true, offered);
        Assert.NotEmpty(candidates);
        Assert.Empty(SelectorTopUpPolicy.ChooseAdditions(offered, candidates, _ => 0));
    }
}
public class StartingSkillPolicyTests
{
    private static readonly CatalogData Catalog = GameCatalog.Data;
    private static bool IsSkill(int id) =>
        Catalog.SkillForAbility(id) is not null || Catalog.StandardSkillForAbility(id) is not null ||
        Catalog.DlcSkillForAbility(id) is not null;
    private static bool IsStandard(int id) => Catalog.StandardSkillForAbility(id) is not null;

    private static StartingSkillPolicy.Mode Read(object? newKey, object? oldKey = null) =>
        StartingSkillPolicy.Read(newKey, oldKey, IsSkill, IsStandard);

    private static readonly IReadOnlySet<int> None = new HashSet<int>();

    // 1 Inferno and 36 Cloak and Dagger are standard; 30 Cyclone and 11 Rupture are tablets; 42 Summon Geode is a DLC tablet.

    [Fact]
    public void TheCatalogMatchesTheSkillKindsTheseTestsAssume()
    {
        Assert.NotNull(Catalog.StandardSkillForAbility(1));
        Assert.NotNull(Catalog.StandardSkillForAbility(36));
        Assert.NotNull(Catalog.SkillForAbility(30));
        Assert.NotNull(Catalog.SkillForAbility(11));
        Assert.NotNull(Catalog.DlcSkillForAbility(42));
        Assert.Equal(7, Catalog.StandardSkills.Count);
        Assert.Equal(10, Catalog.Skills.Count);
        Assert.Equal(10, Catalog.Dlc.Skills.Count);
    }

    [Fact]
    public void ASeedWithNeitherKeyGatesNothingAndLeavesStandardSkillsFree()
    {
        var mode = Read(null, null);
        Assert.Null(mode.Starting);
        Assert.False(mode.GatesTablets);
        Assert.True(StartingSkillPolicy.IsUnlocked(1, isTablet: false, isStandard: true, mode, None));
        Assert.False(StartingSkillPolicy.IsControlled(false, true, false, false, mode));
    }

    [Fact]
    public void TheNewKeyGatesEverySkillIncludingTablets()
    {
        var mode = Read(new List<object> { 1L, 30L });
        Assert.True(mode.GatesTablets);
        Assert.Equal(new[] { 1, 30 }, mode.Starting!.OrderBy(i => i).ToArray());
        // A starting standard skill and a starting tablet skill are both unlocked.
        Assert.True(StartingSkillPolicy.IsUnlocked(1, false, true, mode, None));
        Assert.True(StartingSkillPolicy.IsUnlocked(30, true, false, mode, None));
        // Everything else waits for the multiworld, standard skills included.
        Assert.False(StartingSkillPolicy.IsUnlocked(36, false, true, mode, None));
        Assert.False(StartingSkillPolicy.IsUnlocked(11, true, false, mode, None));
        Assert.True(StartingSkillPolicy.IsControlled(true, false, false, false, mode));
        Assert.True(StartingSkillPolicy.IsControlled(false, true, false, false, mode));
    }

    [Fact]
    public void AReceivedSkillIsUnlockedWhateverTheStartList()
    {
        var mode = Read(new List<object> { 1L });
        var received = new HashSet<int> { 11, 36 };
        Assert.True(StartingSkillPolicy.IsUnlocked(11, true, false, mode, received));
        Assert.True(StartingSkillPolicy.IsUnlocked(36, false, true, mode, received));
        Assert.False(StartingSkillPolicy.IsUnlocked(30, true, false, mode, received));
    }

    [Fact]
    public void AnEmptyNewKeyStillTurnsGatingOn()
    {
        var mode = Read(new List<object>());
        Assert.NotNull(mode.Starting);
        Assert.True(mode.GatesTablets);
        Assert.False(StartingSkillPolicy.IsUnlocked(1, false, true, mode, None));
    }

    [Fact]
    public void AnOldSeedGatesOnlyTheStandardSkillsAndTabletsStayItemsOnly()
    {
        // Seeds from before the option carry only starting_standard_skill_ids.
        var mode = Read(null, new List<object> { 1L, 2L, 3L });
        Assert.False(mode.GatesTablets);
        Assert.Equal(new[] { 1, 2, 3 }, mode.Starting!.OrderBy(i => i).ToArray());
        Assert.True(StartingSkillPolicy.IsUnlocked(1, false, true, mode, None));
        Assert.False(StartingSkillPolicy.IsUnlocked(36, false, true, mode, None));
        // A tablet skill is controlled by the multiworld in an old seed too, and the start list never unlocks it.
        Assert.True(StartingSkillPolicy.IsControlled(true, false, false, false, mode));
        Assert.False(StartingSkillPolicy.IsUnlocked(30, true, false, mode, None));
        Assert.True(StartingSkillPolicy.IsUnlocked(30, true, false, mode, new HashSet<int> { 30 }));
    }

    [Fact]
    public void AnOldSeedListingATabletIdIgnoresIt()
    {
        // The old key only ever held standard skill ids; a tablet id in it is not a standard skill and is dropped.
        var mode = Read(null, new List<object> { 1L, 30L });
        Assert.Equal(new[] { 1 }, mode.Starting!.ToArray());
    }

    [Fact]
    public void TheNewKeyWinsWhenBothArePresent()
    {
        var mode = Read(new List<object> { 11L }, new List<object> { 1L, 2L });
        Assert.True(mode.GatesTablets);
        Assert.Equal(new[] { 11 }, mode.Starting!.ToArray());
    }

    [Fact]
    public void UnknownAndNonNumericIdsAreIgnored()
    {
        var mode = Read(new List<object?> { 1L, 30L, 42L, 999L, "x", null, 11 });
        Assert.Equal(new[] { 1, 11, 30, 42 }, mode.Starting!.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void AValueThatIsNotAListIsTreatedAsAbsent()
    {
        Assert.Null(Read("1,2,3").Starting);
        Assert.Null(Read(5L).Starting);
        Assert.Null(Read(null, "1,2").Starting);
    }

    [Fact]
    public void ADlcSkillIsOnlyControlledWithTheDlcOn()
    {
        var mode = Read(new List<object> { 1L });
        Assert.False(StartingSkillPolicy.IsControlled(false, false, true, dlcActive: false, mode));
        Assert.True(StartingSkillPolicy.IsControlled(false, false, true, dlcActive: true, mode));
    }

    [Fact]
    public void ADlcSkillThePlayerStartsWithIsUnlockedWhenControlled()
    {
        var mode = Read(new List<object> { 42L });
        Assert.True(StartingSkillPolicy.IsUnlocked(42, isTablet: true, isStandard: false, mode, None));
        Assert.False(StartingSkillPolicy.IsUnlocked(43, isTablet: true, isStandard: false, mode, None));
    }

    [Fact]
    public void ASkillThePluginDoesNotKnowIsNeverControlled()
    {
        // Warcry (5) and the other disabled or non-pool abilities stay with the game.
        var mode = Read(new List<object> { 1L });
        Assert.False(StartingSkillPolicy.IsControlled(false, false, false, true, mode));
        Assert.False(StartingSkillPolicy.IsControlled(false, false, false, false, mode));
    }
}
public class GoalPolicyTests
{
    [Fact]
    public void ThePraxisFightCompletesAPraxisGoal()
    {
        Assert.True(GoalPolicy.Completes("praxis", "praxis"));
    }

    [Fact]
    public void ArchPraxisCompletesAnArchPraxisGoal()
    {
        Assert.True(GoalPolicy.Completes("arch_praxis", "arch_praxis"));
    }

    [Fact]
    public void ArchPraxisAlsoCompletesAPraxisGoalBecauseHeat15OnlyOffersThatFight()
    {
        // The reported case: goal praxis with minimum heat 15, where the normal Praxis fight never spawns.
        Assert.True(GoalPolicy.Completes("arch_praxis", "praxis"));
    }

    [Fact]
    public void TheNormalPraxisFightNeverCompletesAnArchPraxisGoal()
    {
        // The easier fight must not finish the harder goal.
        Assert.False(GoalPolicy.Completes("praxis", "arch_praxis"));
    }

    [Theory]
    [InlineData(null, "praxis")]
    [InlineData("praxis", null)]
    [InlineData(null, null)]
    [InlineData("", "praxis")]
    [InlineData("praxis", "")]
    [InlineData("architect", "praxis")]
    [InlineData("arch_praxis", "architect")]
    [InlineData("Praxis", "praxis")]
    [InlineData("PRAXIS", "praxis")]
    [InlineData("arch_praxis ", "praxis")]
    public void AnythingUnknownCompletesNothing(string? defeated, string? goal)
    {
        Assert.False(GoalPolicy.Completes(defeated, goal));
    }

    [Fact]
    public void TheConstantsMatchTheSlotDataValuesAndTheCatalogGoals()
    {
        // The apworld writes slot_data["goal"] as one of these two strings, and the catalog names its final bosses with them.
        Assert.Equal("praxis", GoalPolicy.Praxis);
        Assert.Equal("arch_praxis", GoalPolicy.ArchPraxis);
        Assert.NotNull(GameCatalog.Data.FinalBoss(GoalPolicy.Praxis));
        Assert.NotNull(GameCatalog.Data.FinalBoss(GoalPolicy.ArchPraxis));
    }

    [Fact]
    public void EveryCombinationOfTheTwoRealGoalsIsDecidedAsDocumented()
    {
        var expected = new Dictionary<(string, string), bool>
        {
            [("praxis", "praxis")] = true,
            [("praxis", "arch_praxis")] = false,
            [("arch_praxis", "praxis")] = true,
            [("arch_praxis", "arch_praxis")] = true,
        };
        foreach (var ((defeated, goal), completes) in expected)
            Assert.Equal(completes, GoalPolicy.Completes(defeated, goal));
    }
}

public class SessionStateStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ek_state_test_" + Guid.NewGuid().ToString("N"));
    private SessionStateStore Store => new(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private static SessionState Progress(string seed, string slot, int items, int team = 0) => new()
    {
        Seed = seed,
        SlotName = slot,
        Team = team,
        NextItemIndex = items,
        BaselineTaken = true,
        EmberTreeLevelBaselineTaken = true,
    };

    [Fact]
    public void ANewSessionStartsEmptyAndRemembersItsIdentity()
    {
        var state = Store.Load("seedA", 0, "oreo");
        Assert.Equal("seedA", state.Seed);
        Assert.Equal("oreo", state.SlotName);
        Assert.Equal(0, state.Team);
        Assert.Equal(0, state.NextItemIndex);
        Assert.False(state.BaselineTaken);
    }

    [Fact]
    public void ProgressSurvivesASaveAndLoad()
    {
        var store = Store;
        var state = Progress("seedA", "oreo", 12);
        state.PendingChecks.Add(77);
        store.Save(state);

        var back = Store.Load("seedA", 0, "oreo");
        Assert.Equal(12, back.NextItemIndex);
        Assert.True(back.BaselineTaken);
        Assert.Equal(new long[] { 77 }, back.PendingChecks.ToArray());
    }

    [Fact]
    public void TheSameSlotNameInAnotherRoomDoesNotTouchTheFirstRoom()
    {
        var store = Store;
        store.Save(Progress("seedA", "oreo", 20));

        var other = store.Load("seedB", 0, "oreo");
        Assert.Equal(0, other.NextItemIndex);
        Assert.False(other.BaselineTaken);
        other.NextItemIndex = 3;
        store.Save(other);

        Assert.Equal(20, store.Load("seedA", 0, "oreo").NextItemIndex);
        Assert.Equal(3, store.Load("seedB", 0, "oreo").NextItemIndex);
    }

    [Fact]
    public void ReturningToAnEarlierRoomRestoresItsProgress()
    {
        var store = Store;
        var first = Progress("seedA", "oreo", 20);
        first.BaselineEmberTreeLevels[2] = 1;
        store.Save(first);
        store.Save(Progress("seedB", "oreo", 5));

        var back = store.Load("seedA", 0, "oreo");
        Assert.Equal(20, back.NextItemIndex);
        Assert.True(back.BaselineTaken);
        Assert.Equal(1, back.BaselineEmberTreeLevels[2]);
    }

    [Fact]
    public void TheTeamIsPartOfTheIdentity()
    {
        var store = Store;
        store.Save(Progress("seedA", "oreo", 9, team: 0));
        Assert.Equal(0, store.Load("seedA", 1, "oreo").NextItemIndex);
        Assert.Equal(9, store.Load("seedA", 0, "oreo").NextItemIndex);
    }

    [Fact]
    public void TheSlotNameIsNotCaseSensitiveButTheSeedIs()
    {
        var store = Store;
        store.Save(Progress("seedA", "Oreo", 4));
        Assert.Equal(4, store.Load("seedA", 0, "oreo").NextItemIndex);
        Assert.Equal(0, store.Load("SEEDA", 0, "oreo").NextItemIndex);
    }

    [Fact]
    public void TheFileNameDoesNotDependOnAnyHostOrPort()
    {
        var store = Store;
        Assert.Equal(store.GetPath("seedA", 0, "oreo"), store.GetPath("seedA", 0, "oreo"));
        Assert.NotEqual(store.GetPath("seedA", 0, "oreo"), store.GetPath("seedB", 0, "oreo"));
        Assert.NotEqual(store.GetPath("seedA", 0, "oreo"), store.GetPath("seedA", 0, "oreo2"));
    }

    [Fact]
    public void ALegacyFileForTheSameSeedIsAdoptedAndKept()
    {
        var store = Store;
        Directory.CreateDirectory(Path.Combine(directory, "state"));
        var legacyPath = store.GetLegacyPath("oreo");
        var legacy = Progress("seedA", "oreo", 17);
        legacy.PendingChecks.Add(5);
        File.WriteAllText(legacyPath, System.Text.Json.JsonSerializer.Serialize(legacy));

        var adopted = store.Load("seedA", 0, "oreo");
        Assert.Equal(17, adopted.NextItemIndex);
        Assert.Equal(new long[] { 5 }, adopted.PendingChecks.ToArray());
        Assert.True(File.Exists(store.GetPath("seedA", 0, "oreo")));
        Assert.True(File.Exists(legacyPath));
    }

    [Fact]
    public void ALegacyFileForAnotherSeedIsIgnoredAndLeftAlone()
    {
        var store = Store;
        Directory.CreateDirectory(Path.Combine(directory, "state"));
        var legacyPath = store.GetLegacyPath("oreo");
        File.WriteAllText(legacyPath, System.Text.Json.JsonSerializer.Serialize(Progress("seedOld", "oreo", 17)));
        var before = File.ReadAllText(legacyPath);

        var fresh = store.Load("seedNew", 0, "oreo");
        Assert.Equal(0, fresh.NextItemIndex);
        Assert.False(fresh.BaselineTaken);
        Assert.Equal(before, File.ReadAllText(legacyPath));
        Assert.False(File.Exists(store.GetPath("seedNew", 0, "oreo")));
    }

    [Fact]
    public void ALegacyFileThatCannotBeReadIsIgnoredInsteadOfBlockingTheConnection()
    {
        var store = Store;
        Directory.CreateDirectory(Path.Combine(directory, "state"));
        File.WriteAllText(store.GetLegacyPath("oreo"), "{ not json");
        var fresh = store.Load("seedA", 0, "oreo");
        Assert.Equal(0, fresh.NextItemIndex);
    }

    [Fact]
    public void ACorruptFileForTheSessionStillBlocksInsteadOfSilentlyResetting()
    {
        var store = Store;
        Directory.CreateDirectory(Path.Combine(directory, "state"));
        File.WriteAllText(store.GetPath("seedA", 0, "oreo"), "{ not json");
        Assert.Throws<InvalidDataException>(() => store.Load("seedA", 0, "oreo"));
    }

    [Fact]
    public void AMigratedFileIsNotMigratedAgainOverNewerProgress()
    {
        var store = Store;
        Directory.CreateDirectory(Path.Combine(directory, "state"));
        File.WriteAllText(store.GetLegacyPath("oreo"), System.Text.Json.JsonSerializer.Serialize(Progress("seedA", "oreo", 10)));
        var adopted = store.Load("seedA", 0, "oreo");
        adopted.NextItemIndex = 25;
        store.Save(adopted);

        Assert.Equal(25, store.Load("seedA", 0, "oreo").NextItemIndex);
    }
}
