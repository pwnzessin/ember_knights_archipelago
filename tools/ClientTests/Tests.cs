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
    private static bool IsStandard(int id) => Catalog.StandardSkillForAbility(id) is not null;

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

    [Fact]
    public void ASeedWithoutTheKeyDoesNotGateStandardSkills()
    {
        // Seeds generated before the option existed: every standard skill stays unlocked so old saves lose nothing.
        Assert.Null(StandardSkillPolicy.ReadStartingSkills(null, IsStandard));
        Assert.True(StandardSkillPolicy.IsUnlocked(1, null, new HashSet<int>()));
        Assert.True(StandardSkillPolicy.IsUnlocked(36, null, new HashSet<int>()));
    }

    [Fact]
    public void ASeedWithTheKeyGatesStandardSkillsEvenWhenTheListIsEmpty()
    {
        var starting = StandardSkillPolicy.ReadStartingSkills(new List<object> { 1L, 3L, 8L }, IsStandard);
        Assert.NotNull(starting);
        Assert.Equal(new[] { 1, 3, 8 }, starting!.OrderBy(id => id).ToArray());
        Assert.NotNull(StandardSkillPolicy.ReadStartingSkills(new List<object>(), IsStandard));
    }

    [Fact]
    public void StartingSkillsAreUnlockedAndTheRestOnlyOnceReceived()
    {
        var starting = new HashSet<int> { 1, 3, 8 };
        var received = new HashSet<int>();
        Assert.True(StandardSkillPolicy.IsUnlocked(1, starting, received));
        Assert.False(StandardSkillPolicy.IsUnlocked(2, starting, received));
        Assert.False(StandardSkillPolicy.IsUnlocked(36, starting, received));
        received.Add(2);
        Assert.True(StandardSkillPolicy.IsUnlocked(2, starting, received));
        Assert.False(StandardSkillPolicy.IsUnlocked(36, starting, received));
    }

    [Fact]
    public void SlotDataIdsThatAreNotStandardSkillsOrNotNumbersAreIgnored()
    {
        // 30 is Cyclone (a tablet skill), 999 does not exist, "x" and null are junk.
        var starting = StandardSkillPolicy.ReadStartingSkills(new List<object?> { 2L, 30L, 999L, "x", null, 4 }, IsStandard);
        Assert.Equal(new[] { 2, 4 }, starting!.OrderBy(id => id).ToArray());
    }

    [Fact]
    public void AStringOrNonListValueIsTreatedAsMissing()
    {
        Assert.Null(StandardSkillPolicy.ReadStartingSkills("1,2,3", IsStandard));
        Assert.Null(StandardSkillPolicy.ReadStartingSkills(5L, IsStandard));
    }
}