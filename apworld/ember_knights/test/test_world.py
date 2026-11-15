import unittest

from BaseClasses import CollectionState

from ..catalog import CATALOG
from .bases import EmberKnightsTestBase


def regular_locations(test: EmberKnightsTestBase) -> list:
    return [location for location in test.multiworld.get_locations(test.player) if location.address is not None]


def expected_location_count(boss_variants: bool) -> int:
    encounters = [*CATALOG.minibosses, *CATALOG.bosses]
    if not boss_variants:
        encounters = [encounter for encounter in encounters if not encounter.variant]
    return (
        len(CATALOG.skills)
        + len(encounters)
        + len(CATALOG.ember_tree)
        + len(CATALOG.weapon_mods)
        + len(CATALOG.room_milestones)
        + len(CATALOG.relic_orbs)
    )


class TestDefaultWorld(EmberKnightsTestBase):
    options = {"goal": "praxis"}

    def test_default_counts(self) -> None:
        locations = regular_locations(self)
        self.assertEqual(expected_location_count(True), len(locations))
        self.assertEqual(228, len(locations))
        self.assertEqual(len(locations), len(self.multiworld.itempool))
        self.assertEqual("praxis", self.world.fill_slot_data()["goal"])
        # Relic orb checks are sent when the orbs are handed to Esper unless the player opts out.
        self.assertEqual("on_esper_turn_in", self.world.fill_slot_data()["relic_orb_checks"])

    def test_skills_relics_weapons_and_heat_reductions_match_options(self) -> None:
        item_names = [item.name for item in self.multiworld.itempool]
        single_items = {skill.name for skill in CATALOG.skills} | {orb.name for orb in CATALOG.relic_orbs}
        self.assertTrue(single_items.issubset(item_names))
        for name in single_items:
            self.assertEqual(1, item_names.count(name), name)
        # Default extra_starting_weapons=1 -> Sword (1) + 1 random extra starting weapon, 5 weapons in the pool.
        starting_ids = self.world.starting_weapon_ids
        self.assertEqual(2, len(starting_ids))
        self.assertIn(1, starting_ids)
        pool_weapons = [name for name in item_names if name in self.world.item_name_groups["Weapons"]]
        self.assertEqual(5, len(pool_weapons))
        # Default options are start_heat=25, minimum_heat=15, heat_reduction_per_item=1 -> 10 reductions.
        self.assertEqual(10, item_names.count(CATALOG.heat_reduction_item_name))
        # 35 two-coin and 45 one-coin bundles = 115 coins. Three-coin bundles are retired from the pool.
        self.assertEqual(45, item_names.count("1 Archipelago Coin"))
        self.assertEqual(35, item_names.count("2 Archipelago Coins"))
        self.assertEqual(0, item_names.count("3 Archipelago Coins"))
        filler = {filler.name for filler in CATALOG.fillers}
        non_filler = single_items | set(pool_weapons) | {CATALOG.heat_reduction_item_name}
        self.assertEqual(set(item_names) - non_filler, set(item_names) & filler)

    def test_new_location_groups_are_present(self) -> None:
        names = {location.name for location in regular_locations(self)}
        self.assertTrue({node.location_name for node in CATALOG.ember_tree}.issubset(names))
        self.assertTrue({mod.location_name for mod in CATALOG.weapon_mods}.issubset(names))
        self.assertTrue({milestone.location_name for milestone in CATALOG.room_milestones}.issubset(names))
        self.assertTrue({orb.location_name for orb in CATALOG.relic_orbs}.issubset(names))

    def test_relic_orbs_are_reachable_from_the_start(self) -> None:
        for orb in CATALOG.relic_orbs:
            self.assertTrue(self.can_reach_location(orb.location_name), orb.location_name)

    def test_relic_items_are_useful_not_progression(self) -> None:
        relics = [item for item in self.multiworld.itempool if item.name in self.world.item_name_groups["Relics"]]
        self.assertEqual(53, len(relics))
        self.assertTrue(all(item.useful and not item.advancement for item in relics))

    def test_room_milestones_follow_world_order(self) -> None:
        """Each world's milestones require entering that world, which requires clearing the previous one."""
        # Only the first world is open from the start.
        self.assertTrue(self.can_reach_location("Scoggfirth Room 1 Reached"))
        for later_world in ("Steadfast Citadel", "Wightsbury", "Anville", "Netherra"):
            self.assertFalse(self.can_reach_location(f"{later_world} Room 1 Reached"), later_world)
            self.assertFalse(self.can_reach_region(later_world), later_world)

    def test_world_entrances_require_the_previous_clear_event(self) -> None:
        """The entrance rules themselves are the invariant: world N needs world N-1's clear event."""
        order = ["Scoggfirth", "Steadfast Citadel", "Wightsbury", "Anville", "Netherra", "Architect's Realm"]
        for previous, current in zip(order, order[1:]):
            entrance = self.multiworld.get_entrance(f"{previous} to {current}", self.player)
            empty_state = CollectionState(self.multiworld)
            self.assertFalse(entrance.access_rule(empty_state), f"{previous} to {current} must be locked")
            granted = CollectionState(self.multiworld)
            granted.prog_items[self.player][f"{previous} Clear"] = 1
            self.assertTrue(entrance.access_rule(granted), f"{previous} to {current} must open on its clear event")

    def test_meta_progression_checks_and_coin_gating(self) -> None:
        # Starting coins default to 1, which unlocks Thick Skin (1 coin) immediately.
        self.assertTrue(self.can_reach_location("Ember Tree: Thick Skin"))
        self.assertTrue(self.can_reach_location("Sword Mod: Momentum"))
        # Additional Ember Tree nodes require more Archipelago Coins according to their stage.
        self.assertFalse(self.can_reach_location("Ember Tree: Regeneration"))
        self.assertFalse(self.can_reach_location("Ember Tree: Ultembera"))
        regeneration = self.multiworld.get_location("Ember Tree: Regeneration", self.player)
        ultembera = self.multiworld.get_location("Ember Tree: Ultembera", self.player)
        state = CollectionState(self.multiworld)
        self.assertFalse(regeneration.access_rule(state))
        state.prog_items[self.player]["2 Archipelago Coins"] = 1
        self.assertTrue(regeneration.access_rule(state))
        self.assertFalse(ultembera.access_rule(state))
        # Ultembera needs 37 coins: 1 starting coin + 2 from the bundle above + 17 more 2-coin bundles = 37.
        state.prog_items[self.player]["2 Archipelago Coins"] = 17
        self.assertFalse(ultembera.access_rule(state))
        state.prog_items[self.player]["2 Archipelago Coins"] = 18
        self.assertTrue(ultembera.access_rule(state))


class TestReducedWorld(EmberKnightsTestBase):
    options = {
        "goal": "arch_praxis",
        "boss_variants": False,
        "start_heat": 30,
        "minimum_heat": 15,
        "heat_reduction_per_item": 4,
        "traps": True,
        "death_link": True,
        "relic_orb_checks": "on_pickup",
    }

    def test_reduced_counts_and_slot_data(self) -> None:
        locations = regular_locations(self)
        self.assertEqual(expected_location_count(False), len(locations))
        self.assertEqual(len(locations), len(self.multiworld.itempool))
        self.assertFalse(any("Arachna Za" in location.name for location in locations))
        # Meta-progression and world milestones are not affected by the boss variant option.
        self.assertTrue(any(location.name.startswith("Ember Tree:") for location in locations))
        # ceil((30 - 15) / 4) = 4 Heat Reduction items.
        reductions = [item for item in self.multiworld.itempool if item.name == CATALOG.heat_reduction_item_name]
        self.assertEqual(4, len(reductions))
        self.assertTrue(all(item.useful and not item.advancement for item in reductions))
        traps = [item for item in self.multiworld.itempool if item.name in self.world.item_name_groups["Traps"]]
        gold = [item for item in self.multiworld.itempool if item.name == "50 Gold"]
        ash = [item for item in self.multiworld.itempool if item.name == CATALOG.ash_item_name]
        self.assertGreater(len(traps), 0)
        self.assertTrue(all(item.trap and not item.advancement for item in traps))
        # Gold, Ash and traps share the filler slots three ways; gold takes the remainder (0 to 2 extra).
        self.assertEqual(len(ash), len(traps))
        self.assertIn(len(gold) - len(ash), (0, 1, 2))
        slot_data = self.world.fill_slot_data()
        self.assertEqual("arch_praxis", slot_data["goal"])
        self.assertEqual(30, slot_data["start_heat"])
        self.assertEqual(15, slot_data["minimum_heat"])
        self.assertEqual(4, slot_data["heat_reduction_per_item"])
        self.assertTrue(slot_data["traps"])
        self.assertTrue(slot_data["death_link"])
        self.assertEqual("on_pickup", slot_data["relic_orb_checks"])


class TestZeroHeatSpanWorld(EmberKnightsTestBase):
    options = {"start_heat": 10, "minimum_heat": 15, "heat_reduction_per_item": 2}

    def test_no_heat_reduction_items_when_start_is_at_or_below_minimum(self) -> None:
        item_names = [item.name for item in self.multiworld.itempool]
        self.assertNotIn(CATALOG.heat_reduction_item_name, item_names)


class TestCatalogContract(unittest.TestCase):
    def test_ids_and_names_are_unique(self) -> None:
        self.assertEqual(
            len(CATALOG.skills) + len(CATALOG.weapons) + len(CATALOG.relic_orbs) + len(CATALOG.fillers) + 1,
            len(CATALOG.item_name_to_id),
        )
        self.assertEqual(
            len(CATALOG.skills)
            + len(CATALOG.minibosses)
            + len(CATALOG.bosses)
            + len(CATALOG.ember_tree)
            + len(CATALOG.weapon_mods)
            + len(CATALOG.room_milestones)
            + len(CATALOG.relic_orbs),
            len(CATALOG.location_name_to_id),
        )
        self.assertEqual(len(CATALOG.item_name_to_id), len(set(CATALOG.item_name_to_id.values())))
        self.assertEqual(len(CATALOG.location_name_to_id), len(set(CATALOG.location_name_to_id.values())))

    def test_item_and_location_names_do_not_collide(self) -> None:
        self.assertFalse(set(CATALOG.item_name_to_id) & set(CATALOG.location_name_to_id))

    def test_enemy_ids_are_unambiguous(self) -> None:
        enemy_ids = [
            enemy_id
            for encounter in [*CATALOG.minibosses, *CATALOG.bosses]
            for enemy_id in encounter.enemy_ids
        ]
        self.assertEqual(len(enemy_ids), len(set(enemy_ids)))
        self.assertNotIn(0, enemy_ids)

    def test_verified_game_ids(self) -> None:
        self.assertEqual({30, 11, 34, 32, 7, 35, 14, 13, 40, 39}, {s.ability_id for s in CATALOG.skills})
        self.assertEqual({2, 3, 4, 5, 6, 7}, {w.weapon_id for w in CATALOG.weapons})

    def test_verified_progression_counts(self) -> None:
        self.assertEqual(30, len(CATALOG.ember_tree))
        self.assertEqual(87, len(CATALOG.weapon_mods))
        self.assertEqual(18, len(CATALOG.room_milestones))
        # Definitions are unique game IDs.
        self.assertEqual(len(CATALOG.ember_tree), len({node.def_id for node in CATALOG.ember_tree}))
        self.assertEqual(len(CATALOG.weapon_mods), len({mod.def_id for mod in CATALOG.weapon_mods}))
        # Disabled Ember Tree nodes (Elite Slayer, Rare Hunter, Stat Dice) and DLC mods are excluded.
        self.assertFalse({11, 13, 16} & {node.def_id for node in CATALOG.ember_tree})
        self.assertTrue(all(mod.weapon_type <= 7 for mod in CATALOG.weapon_mods))

    def test_room_milestones_reference_known_worlds(self) -> None:
        regions = {"Scoggfirth", "Steadfast Citadel", "Wightsbury", "Anville", "Netherra"}
        self.assertTrue(all(milestone.world in regions for milestone in CATALOG.room_milestones))

    def test_relic_orbs_match_the_games_unlock_order(self) -> None:
        orbs = CATALOG.relic_orbs
        self.assertEqual(53, len(orbs))
        # The game hands orbs out in a fixed order 1..53; locations are numbered the same way.
        self.assertEqual(list(range(1, 54)), [orb.order for orb in orbs])
        self.assertEqual(53, len({orb.def_id for orb in orbs}))
        self.assertEqual(53, len({orb.name for orb in orbs}))
        # First and last relics of RelicsByUnlockOrder as dumped from the running game.
        self.assertEqual((48, "Spirit Shelter"), (orbs[0].def_id, orbs[0].name))
        self.assertEqual((183, "Eye of the Storm"), (orbs[-1].def_id, orbs[-1].name))
        # DLC orb relics (ids 211-220) and the cursed relics are not part of the base-game orb set.
        self.assertFalse({orb.def_id for orb in orbs} & set(range(211, 221)))
        self.assertEqual("Relic Orb 01", orbs[0].location_name)
        self.assertEqual("Relic Orb 53", orbs[-1].location_name)

    def test_relic_names_are_clean_english(self) -> None:
        for orb in CATALOG.relic_orbs:
            self.assertTrue(orb.name.isascii() or "'" in orb.name, orb.name)
            self.assertEqual(orb.name, orb.name.strip())
            self.assertNotIn("NewRelic", orb.name)

    def test_heat_modifier_table_matches_verified_limits(self) -> None:
        self.assertEqual(55, CATALOG.max_heat)
        self.assertEqual(17, len(CATALOG.heat_modifiers))
        usable = [mod for mod in CATALOG.heat_modifiers if mod.unsafe is None]
        self.assertEqual(15, len(usable))
        # Chaos and Shared Fate are marked unsafe and excluded from the usable max heat.
        self.assertEqual({16, 17}, {mod.id for mod in CATALOG.heat_modifiers if mod.unsafe is not None})
        total = sum(mod.costs[-1] if mod.kind == "level" else sum(mod.costs) for mod in usable)
        self.assertEqual(CATALOG.max_heat, total)


def filler_counts(test: EmberKnightsTestBase) -> dict[str, int]:
    names = [item.name for item in test.multiworld.itempool]
    return {
        "gold": names.count("50 Gold"),
        "ash": names.count(CATALOG.ash_item_name),
        "traps": sum(name in test.world.item_name_groups["Traps"] for name in names),
    }


class TestAshFillerDefault(EmberKnightsTestBase):
    """Default options: no traps, Ash on. Gold and Ash split the filler slots in half."""

    def test_ash_is_a_filler_that_shares_the_slots_with_gold(self) -> None:
        counts = filler_counts(self)
        self.assertEqual(0, counts["traps"])
        self.assertGreater(counts["ash"], 0)
        self.assertIn(counts["gold"] - counts["ash"], (0, 1))
        ash_items = [item for item in self.multiworld.itempool if item.name == CATALOG.ash_item_name]
        self.assertTrue(all(not item.advancement and not item.useful and not item.trap for item in ash_items))
        self.assertEqual(5, self.world.fill_slot_data()["ash_filler_amount"])

    def test_pool_size_still_matches_the_locations(self) -> None:
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))


class TestAshFillerDisabled(EmberKnightsTestBase):
    options = {"ash_filler_amount": 0, "traps": True}

    def test_zero_amount_removes_ash_items(self) -> None:
        counts = filler_counts(self)
        self.assertEqual(0, counts["ash"])
        self.assertGreater(counts["traps"], 0)
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))
        self.assertEqual(0, self.world.fill_slot_data()["ash_filler_amount"])


class TestAshFillerWithTraps(EmberKnightsTestBase):
    options = {"ash_filler_amount": 20, "traps": True}

    def test_three_way_split_and_amount_in_slot_data(self) -> None:
        counts = filler_counts(self)
        self.assertEqual(counts["ash"], counts["traps"])
        self.assertIn(counts["gold"] - counts["ash"], (0, 1, 2))
        self.assertEqual(20, self.world.fill_slot_data()["ash_filler_amount"])


class TestAshItemId(unittest.TestCase):
    def test_ash_was_appended_so_existing_filler_ids_did_not_move(self) -> None:
        expected_order = [
            "1 Archipelago Coin",
            "2 Archipelago Coins",
            "3 Archipelago Coins",
            "50 Gold",
            "Enemy Ambush Trap",
            "Boss Ambush Trap",
            "Praxis Arena Trap",
            "Ash",
        ]
        self.assertEqual(expected_order, [filler.name for filler in CATALOG.fillers])
        for index, name in enumerate(expected_order):
            self.assertEqual(CATALOG.id_base + 200 + index, CATALOG.item_name_to_id[name], name)

class TestCoinEconomy(EmberKnightsTestBase):
    def coin_items(self) -> list[str]:
        return [item.name for item in self.multiworld.itempool if item.name in self.world.item_name_groups["Archipelago Coins"]]

    def test_no_three_coin_bundles_are_placed_and_the_total_is_unchanged(self) -> None:
        names = self.coin_items()
        self.assertNotIn("3 Archipelago Coins", names)
        total = sum(bundle.amount * names.count(bundle.name) for bundle in CATALOG.coin_bundles)
        self.assertEqual(115, total)
        self.assertEqual(80, len(names))
        self.assertEqual(2, max(bundle.amount for bundle in CATALOG.coin_bundles if bundle.name in names))

    def test_every_ember_tree_node_is_still_reachable_with_the_pooled_coins(self) -> None:
        # Starting coins (default 1) plus everything in the pool must clear the highest gate, with the 100 coins of
        # unlocks and level-ups still affordable.
        highest_gate = max(node.required_coins for node in CATALOG.ember_tree)
        names = self.coin_items()
        pooled = sum(bundle.amount * names.count(bundle.name) for bundle in CATALOG.coin_bundles)
        self.assertGreaterEqual(1 + pooled, highest_gate)
        self.assertGreaterEqual(1 + pooled, 100)

    def test_the_retired_bundle_still_counts_for_gates_in_seeds_that_already_have_it(self) -> None:
        ultembera = self.multiworld.get_location("Ember Tree: Ultembera", self.player)
        state = CollectionState(self.multiworld)
        state.prog_items[self.player]["3 Archipelago Coins"] = 12
        self.assertTrue(ultembera.access_rule(state))

    def test_pool_size_still_matches_the_locations(self) -> None:
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))

class TestCuratedHeatDifficulty(EmberKnightsTestBase):
    options = {"heat_mode": "curated_heat_difficulty"}

    def test_slot_data_names_the_new_mode(self) -> None:
        self.assertEqual("curated_heat_difficulty", self.world.fill_slot_data()["heat_mode"])

    def test_no_slot_data_still_mentions_blitz_or_the_old_mode_name(self) -> None:
        values = [str(value) for value in self.world.fill_slot_data().values()]
        self.assertFalse(any("late_blitz" in value for value in values), values)


class TestOldHeatModeNameStillWorks(EmberKnightsTestBase):
    """YAML files written before the rename use random_assortment_late_blitz; it must resolve to the curated mode."""

    options = {"heat_mode": "random_assortment_late_blitz"}

    def test_old_name_resolves_to_the_curated_mode(self) -> None:
        self.assertEqual("curated_heat_difficulty", self.world.fill_slot_data()["heat_mode"])


class TestOtherHeatModesAreUnchanged(unittest.TestCase):
    def test_option_values(self) -> None:
        from ..options import HeatMode

        self.assertEqual(0, HeatMode.option_random_assortment)
        self.assertEqual(1, HeatMode.option_sequential)
        self.assertEqual(2, HeatMode.option_curated_heat_difficulty)
        self.assertEqual(0, HeatMode.default)
        self.assertNotIn("curated_heat_difficulty", ("random_assortment", "sequential"))