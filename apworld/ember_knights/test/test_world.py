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
        # Orb checks are always sent at pickup; the old timing option is gone from the slot data and the options.
        self.assertNotIn("relic_orb_checks", self.world.fill_slot_data())
        self.assertFalse(hasattr(self.world.options, "relic_orb_checks"))

    def test_skills_relics_weapons_and_heat_reductions_match_options(self) -> None:
        item_names = [item.name for item in self.multiworld.itempool]
        starting_relics = {
            name for def_id, name in self.world.relic_pool() if def_id in self.world.starting_relic_ids
        }
        relic_items = {name for _, name in self.world.relic_pool()} - starting_relics
        starting_skills = {
            name for ability_id, name in self.world.skill_pool() if ability_id in self.world.starting_skill_ids
        }
        skill_items = {name for _, name in self.world.skill_pool()} - starting_skills
        single_items = skill_items | relic_items
        self.assertTrue(single_items.issubset(item_names))
        for name in single_items:
            self.assertEqual(1, item_names.count(name), name)
        for name in starting_relics | starting_skills:
            self.assertEqual(0, item_names.count(name), name)
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

    def test_default_starts_with_three_skills_out_of_seventeen_and_pools_the_other_fourteen(self) -> None:
        # Default starting_skills=3: three random skills of the 17 (7 standard + 10 tablet) are precollected.
        pool = self.world.skill_pool()
        self.assertEqual(17, len(pool))
        starting_ids = self.world.starting_skill_ids
        self.assertEqual(3, len(starting_ids))
        self.assertEqual(len(starting_ids), len(set(starting_ids)))
        self.assertTrue(set(starting_ids).issubset({ability_id for ability_id, _ in pool}))
        starting_names = {name for ability_id, name in pool if ability_id in starting_ids}
        all_names = {name for _, name in pool}
        in_pool = [item.name for item in self.multiworld.itempool if item.name in all_names]
        self.assertEqual(14, len(in_pool))
        self.assertEqual(len(in_pool), len(set(in_pool)))
        # Each skill is either a starting skill or in the pool, never both and never neither.
        self.assertFalse(starting_names & set(in_pool))
        self.assertEqual(all_names, starting_names | set(in_pool))
        precollected = {item.name for item in self.multiworld.precollected_items[self.player]}
        self.assertTrue(starting_names.issubset(precollected))
        self.assertEqual(starting_ids, self.world.fill_slot_data()["starting_skill_ids"])
        self.assertEqual(3, self.world.fill_slot_data()["starting_skills"])
        # The old key is gone: a seed with the new one is read by its own key only.
        self.assertNotIn("starting_standard_skill_ids", self.world.fill_slot_data())

    def test_standard_skills_are_useful_not_progression(self) -> None:
        for item in self.multiworld.itempool:
            if item.name in self.world.item_name_groups["Standard Skills"]:
                self.assertTrue(item.useful, item.name)
                self.assertFalse(item.advancement, item.name)

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
        # 53 orb relics + 43 default relics = 96 in the pool, minus the 10 the seed starts with.
        self.assertEqual(86, len(relics))
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
        # 50 Gold is retired; Ash and traps share the filler slots two ways, Ash taking the remainder (0 or 1 extra).
        self.assertEqual(0, len(gold))
        self.assertIn(len(ash) - len(traps), (0, 1))
        slot_data = self.world.fill_slot_data()
        self.assertEqual("arch_praxis", slot_data["goal"])
        self.assertEqual(30, slot_data["start_heat"])
        self.assertEqual(15, slot_data["minimum_heat"])
        self.assertEqual(4, slot_data["heat_reduction_per_item"])
        self.assertTrue(slot_data["traps"])
        self.assertTrue(slot_data["death_link"])
        self.assertNotIn("relic_orb_checks", slot_data)


class TestZeroHeatSpanWorld(EmberKnightsTestBase):
    options = {"start_heat": 10, "minimum_heat": 15, "heat_reduction_per_item": 2}

    def test_no_heat_reduction_items_when_start_is_at_or_below_minimum(self) -> None:
        item_names = [item.name for item in self.multiworld.itempool]
        self.assertNotIn(CATALOG.heat_reduction_item_name, item_names)


class TestAllSeventeenSkillsWorld(EmberKnightsTestBase):
    options = {"starting_skills": 17}

    def test_starting_with_all_seventeen_puts_none_in_the_pool(self) -> None:
        all_names = {name for _, name in self.world.skill_pool()}
        self.assertEqual(17, len(self.world.starting_skill_ids))
        self.assertEqual({ability_id for ability_id, _ in self.world.skill_pool()}, set(self.world.starting_skill_ids))
        self.assertEqual([], [item.name for item in self.multiworld.itempool if item.name in all_names])
        # The pool still exactly fills the locations, so the extra filler slots absorbed the missing skill items.
        locations = regular_locations(self)
        self.assertEqual(len(locations), len(self.multiworld.itempool))

    def test_every_tablet_is_still_a_check(self) -> None:
        # Starting with a tablet skill does not remove its tablet: the location exists whatever you started with.
        names = {location.name for location in regular_locations(self)}
        self.assertTrue({skill.location_name for skill in CATALOG.skills}.issubset(names))


class TestTenSkillsWorld(EmberKnightsTestBase):
    options = {"starting_skills": 10}

    def test_ten_start_and_seven_are_pooled(self) -> None:
        all_names = {name for _, name in self.world.skill_pool()}
        self.assertEqual(10, len(self.world.starting_skill_ids))
        self.assertEqual(7, len([item for item in self.multiworld.itempool if item.name in all_names]))
        locations = regular_locations(self)
        self.assertEqual(len(locations), len(self.multiworld.itempool))


class TestSkillsWithDlc(EmberKnightsTestBase):
    options = {"dlc": True, "starting_skills": 5}

    def test_the_dlc_tablet_skills_join_the_pool(self) -> None:
        pool = self.world.skill_pool()
        self.assertEqual(27, len(pool))
        self.assertEqual(5, len(self.world.starting_skill_ids))
        names = {item.name for item in self.multiworld.itempool}
        starting = set(self.world.starting_skill_ids)
        for skill in CATALOG.dlc_skills:
            self.assertEqual(skill.ability_id not in starting, skill.name in names, skill.name)
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))


class TestSkillsWithoutDlcNeverHoldDlcSkills(EmberKnightsTestBase):
    options = {"dlc": False, "starting_skills": 17}

    def test_no_dlc_skill_is_started_or_pooled(self) -> None:
        dlc_ids = {skill.ability_id for skill in CATALOG.dlc_skills}
        dlc_names = {skill.name for skill in CATALOG.dlc_skills}
        self.assertFalse(dlc_ids & set(self.world.starting_skill_ids))
        self.assertFalse(dlc_names & {item.name for item in self.multiworld.itempool})
        precollected = {item.name for item in self.multiworld.precollected_items[self.player]}
        self.assertFalse(dlc_names & precollected)


class TestSkillsTightestPool(EmberKnightsTestBase):
    """The minimum start (3) pools the most skills; the pool must still fit with every other option at its tightest."""

    options = {
        "boss_variants": False,
        "starting_skills": 3,
        "extra_starting_weapons": 0,
        "start_heat": 0,
        "minimum_heat": 0,
        "starting_relics": 10,
    }

    def test_pool_fits(self) -> None:
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))


class TestStartingRelicsDefault(EmberKnightsTestBase):
    options = {}

    def test_ten_relics_start_unlocked_and_the_rest_are_items(self) -> None:
        pool = self.world.relic_pool()
        self.assertEqual(96, len(pool))
        starting = self.world.starting_relic_ids
        self.assertEqual(10, len(starting))
        self.assertEqual(len(starting), len(set(starting)))
        self.assertTrue(set(starting).issubset({def_id for def_id, _ in pool}))
        in_pool = {item.name for item in self.multiworld.itempool} & {name for _, name in pool}
        self.assertEqual({name for def_id, name in pool if def_id not in starting}, in_pool)
        precollected = {item.name for item in self.multiworld.precollected_items[self.player]}
        self.assertTrue({name for def_id, name in pool if def_id in starting}.issubset(precollected))
        self.assertEqual(starting, self.world.fill_slot_data()["starting_relic_ids"])
        self.assertEqual(10, self.world.fill_slot_data()["starting_relics"])

    def test_pool_still_exactly_fills_the_locations(self) -> None:
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))
        # The 43 default relics add items but no locations: the location count is unchanged.
        self.assertEqual(228, len(regular_locations(self)))

    def test_every_orb_still_has_its_location(self) -> None:
        names = {location.name for location in regular_locations(self)}
        self.assertTrue({orb.location_name for orb in CATALOG.relic_orbs}.issubset(names))


class TestStartingRelicsMaximum(EmberKnightsTestBase):
    options = {"starting_relics": 43}

    def test_forty_three_start_and_fifty_three_remain(self) -> None:
        self.assertEqual(43, len(self.world.starting_relic_ids))
        relics = [item for item in self.multiworld.itempool if item.name in self.world.item_name_groups["Relics"]]
        self.assertEqual(96 - 43, len(relics))
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))


class TestStartingRelicsWithDlc(EmberKnightsTestBase):
    options = {"dlc": True, "starting_relics": 20}

    def test_the_dlc_orb_relics_join_the_pool(self) -> None:
        self.assertEqual(106, len(self.world.relic_pool()))
        self.assertEqual(20, len(self.world.starting_relic_ids))
        names = {item.name for item in self.multiworld.itempool}
        starting = set(self.world.starting_relic_ids)
        for orb in CATALOG.dlc_relic_orbs:
            self.assertEqual(orb.def_id not in starting, orb.name in names, orb.name)
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))


class TestStartingRelicsTightestPool(EmberKnightsTestBase):
    """The configuration with the fewest filler slots must still fit all the extra relic items."""

    options = {
        "boss_variants": False,
        "starting_skills": 17,
        "extra_starting_weapons": 0,
        "start_heat": 0,
        "minimum_heat": 0,
        "starting_relics": 10,
    }

    def test_pool_fits(self) -> None:
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))


class TestPraxisGoalNote(unittest.TestCase):
    """The warning for a Praxis goal that can only be finished through Arch Praxis. It is a pure function of three numbers."""

    def note(self, goal_is_praxis: bool, minimum: int, start: int):
        from .. import praxis_goal_is_unreachable_note

        return praxis_goal_is_unreachable_note(goal_is_praxis, minimum, start)

    def test_the_reported_case_warns(self) -> None:
        # Goal Praxis, minimum heat 15, start heat 15 (the values from the run that could not finish).
        text = self.note(True, 15, 15)
        self.assertIsNotNone(text)
        self.assertIn("Arch Praxis", text)
        self.assertIn("15", text)
        self.assertIn("completes a Praxis goal", text)

    def test_the_default_options_warn(self) -> None:
        # minimum_heat defaults to 15 and start_heat to 25: the lowest heat is 15, so a default Praxis seed gets the note.
        self.assertIsNotNone(self.note(True, 15, 25))

    def test_a_minimum_below_the_threshold_does_not_warn(self) -> None:
        self.assertIsNone(self.note(True, 14, 25))
        self.assertIsNone(self.note(True, 0, 25))

    def test_a_start_heat_below_the_threshold_does_not_warn(self) -> None:
        # The active heat never goes above the start heat, so a low start heat makes the normal fight reachable.
        self.assertIsNone(self.note(True, 15, 10))
        self.assertIsNone(self.note(True, 40, 14))

    def test_the_threshold_is_exactly_fifteen(self) -> None:
        self.assertIsNone(self.note(True, 14, 14))
        self.assertIsNotNone(self.note(True, 15, 15))
        self.assertIsNotNone(self.note(True, 16, 16))

    def test_the_arch_praxis_goal_never_warns(self) -> None:
        for minimum, start in ((15, 15), (15, 25), (0, 0), (55, 55)):
            self.assertIsNone(self.note(False, minimum, start))

    def test_the_numbers_in_the_text_are_the_real_ones(self) -> None:
        text = self.note(True, 20, 30)
        self.assertIn("Minimum Heat 20", text)
        self.assertIn("Start Heat 30", text)
        self.assertIn("the lowest heat this seed can ever have is 20", text)

    def test_the_constant_matches_the_documented_threshold(self) -> None:
        from .. import ARCH_PRAXIS_HEAT
        from ..options import MinimumHeat

        self.assertEqual(15, ARCH_PRAXIS_HEAT)
        self.assertEqual(ARCH_PRAXIS_HEAT, MinimumHeat.default)


class TestPraxisGoalWorldWarns(EmberKnightsTestBase):
    """A real generation with a Praxis goal and minimum heat 15 still works and still logs the warning."""

    options = {"goal": "praxis", "minimum_heat": 15, "start_heat": 15}

    def test_generation_succeeds_and_slot_data_keeps_the_praxis_goal(self) -> None:
        slot_data = self.world.fill_slot_data()
        self.assertEqual("praxis", slot_data["goal"])
        self.assertEqual(15, slot_data["minimum_heat"])
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))

    def test_the_seed_is_still_beatable(self) -> None:
        # The victory event sits in Netherra for a Praxis goal; the goal must still be completable with every item.
        self.assertTrue(self.multiworld.can_beat_game(self.multiworld.get_all_state(False)))


class TestArchPraxisGoalWorld(EmberKnightsTestBase):
    options = {"goal": "arch_praxis", "minimum_heat": 15, "start_heat": 15}

    def test_the_goal_is_unchanged(self) -> None:
        self.assertEqual("arch_praxis", self.world.fill_slot_data()["goal"])

class TestStarterFriendlyRelicsDefault(EmberKnightsTestBase):
    options = {}

    def test_at_least_seven_starting_relics_are_common_without_a_category_requirement(self) -> None:
        friendly = CATALOG.starter_friendly_relics
        starting = self.world.starting_relic_ids
        self.assertEqual(10, len(starting))
        self.assertGreaterEqual(len(set(starting) & friendly), 7)

    def test_the_start_is_still_exactly_ten_distinct_relics_from_the_pool(self) -> None:
        starting = self.world.starting_relic_ids
        self.assertEqual(10, len(set(starting)))
        self.assertTrue(set(starting).issubset({def_id for def_id, _ in self.world.relic_pool()}))
        self.assertEqual(starting, sorted(starting))
        self.assertEqual(starting, self.world.fill_slot_data()["starting_relic_ids"])

    def test_the_pool_still_exactly_fills_the_locations(self) -> None:
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))


class TestStarterFriendlyRelicsSmallStart(EmberKnightsTestBase):
    options = {"starting_relics": 10, "dlc": False}

    def test_the_guarantee_holds_without_the_dlc(self) -> None:
        self.assertGreaterEqual(len(set(self.world.starting_relic_ids) & CATALOG.starter_friendly_relics), 7)


class TestStarterFriendlyRelicsLargeStart(EmberKnightsTestBase):
    options = {"starting_relics": 43, "dlc": True}

    def test_a_large_start_keeps_the_guarantee_and_the_count(self) -> None:
        starting = self.world.starting_relic_ids
        self.assertEqual(43, len(set(starting)))
        self.assertGreaterEqual(len(set(starting) & CATALOG.starter_friendly_relics), 7)
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))


class TestStarterFriendlyRelicsCatalogContract(unittest.TestCase):
    def test_the_list_is_the_common_unrestricted_base_relics(self) -> None:
        friendly = CATALOG.starter_friendly_relics
        self.assertEqual(21, len(friendly))
        base_pool = {relic.def_id for relic in CATALOG.default_relics} | {orb.def_id for orb in CATALOG.relic_orbs}
        # Every entry is a base relic the starting pool can hold, and none is a DLC orb relic.
        self.assertTrue(friendly.issubset(base_pool))
        self.assertFalse(friendly & {orb.def_id for orb in CATALOG.dlc_relic_orbs})
        # Chronograph (40), the first orb relic, is one of them; Necronomicon (214, DLC) and Bone Plate's Epic neighbours are not.
        self.assertIn(40, friendly)
        self.assertNotIn(214, friendly)

    def test_seven_is_always_reachable_with_the_smallest_start(self) -> None:
        # The guarantee must never exceed what exists, or the pick would have to invent relics.
        self.assertGreaterEqual(len(CATALOG.starter_friendly_relics), 7)
        from ..options import StartingRelics

        self.assertGreaterEqual(StartingRelics.range_start, 7)

class TestStartingRelicsCatalogContract(unittest.TestCase):
    def test_the_option_range_is_ten_to_forty_three(self) -> None:
        from ..options import StartingRelics

        self.assertEqual(10, StartingRelics.range_start)
        self.assertEqual(43, StartingRelics.range_end)
        self.assertEqual(10, StartingRelics.default)

    def test_there_are_forty_three_default_relics_that_are_not_orbs(self) -> None:
        defaults = CATALOG.default_relics
        self.assertEqual(43, len(defaults))
        self.assertEqual(43, len({relic.def_id for relic in defaults}))
        orb_ids = {orb.def_id for orb in CATALOG.relic_orbs} | {orb.def_id for orb in CATALOG.dlc_relic_orbs}
        self.assertFalse({relic.def_id for relic in defaults} & orb_ids)
        # Chronograph is an orb relic, so it is not one of the default relics.
        self.assertNotIn(40, {relic.def_id for relic in defaults})
        self.assertEqual([6, 17, 22], [relic.def_id for relic in defaults[:3]])

    def test_default_relic_item_ids_do_not_collide_or_move_anything(self) -> None:
        base = CATALOG.id_base
        ids = [CATALOG.item_name_to_id[relic.name] for relic in CATALOG.default_relics]
        self.assertEqual([base + 600 + i for i in range(43)], ids)
        all_ids = list(CATALOG.item_name_to_id.values())
        self.assertEqual(len(all_ids), len(set(all_ids)))
        # Existing ranges stay exactly where they were.
        self.assertEqual(base + 300, CATALOG.item_name_to_id[CATALOG.relic_orbs[0].name])
        self.assertEqual(base + 540, CATALOG.item_name_to_id[CATALOG.dlc_relic_orbs[0].name])


class TestStandardSkillCatalogContract(unittest.TestCase):
    def test_standard_skills_are_the_seven_base_game_defaults(self) -> None:
        self.assertEqual({1, 2, 3, 4, 6, 8, 36}, {s.ability_id for s in CATALOG.standard_skills})
        self.assertEqual(7, len(CATALOG.standard_skills))

    def test_standard_skill_ids_do_not_collide_with_or_move_any_existing_id(self) -> None:
        base = CATALOG.id_base
        # Tablet skills keep +1..+10 exactly as before.
        self.assertEqual([base + 1 + i for i in range(10)], [CATALOG.item_name_to_id[s.name] for s in CATALOG.skills])
        # Standard skills sit at +20..+26, clear of tablet skills (+1..+10) and weapons (+100..).
        self.assertEqual(
            [base + 20 + i for i in range(7)], [CATALOG.item_name_to_id[s.name] for s in CATALOG.standard_skills]
        )
        self.assertEqual(base + 100, CATALOG.item_name_to_id[CATALOG.weapons[0].name])
        ids = list(CATALOG.item_name_to_id.values())
        self.assertEqual(len(ids), len(set(ids)))

    def test_standard_skills_have_no_locations(self) -> None:
        standard = {skill.name for skill in CATALOG.standard_skills}
        for name in CATALOG.location_name_to_id:
            self.assertNotIn(name.replace(" Skill Tablet", ""), standard)

    def test_option_range_never_allows_fewer_than_three(self) -> None:
        from ..options import StartingSkills

        self.assertEqual(3, StartingSkills.range_start)
        self.assertEqual(17, StartingSkills.range_end)
        self.assertEqual(3, StartingSkills.default)

    def test_the_old_option_is_gone(self) -> None:
        from .. import options

        self.assertFalse(hasattr(options, "StartingStandardSkills"))


def expected_dlc_location_count(boss_variants: bool) -> int:
    encounters = [*CATALOG.dlc_minibosses, *CATALOG.dlc_bosses]
    if not boss_variants:
        encounters = [encounter for encounter in encounters if not encounter.variant]
    return (
        len(CATALOG.dlc_skills)
        + len(encounters)
        + len(CATALOG.dlc_weapon_mods)
        + len(CATALOG.dlc_room_milestones)
        + len(CATALOG.dlc_relic_orbs)
    )


class TestDlcOffByDefault(EmberKnightsTestBase):
    options = {}

    def test_default_seed_has_no_dlc_locations_items_or_regions(self) -> None:
        # The default seed must be exactly the 1.2.0 seed: 228 locations, no DLC anything.
        locations = regular_locations(self)
        self.assertEqual(228, len(locations))
        dlc_names = self.world.location_name_groups["DLC"]
        self.assertFalse({location.name for location in locations} & dlc_names)
        dlc_items = (
            {skill.name for skill in CATALOG.dlc_skills}
            | {weapon.name for weapon in CATALOG.dlc_weapons}
            | {orb.name for orb in CATALOG.dlc_relic_orbs}
        )
        pool = {item.name for item in self.multiworld.itempool}
        self.assertFalse(pool & dlc_items)
        self.assertFalse({region.name for region in self.multiworld.get_regions(self.player)} & set(CATALOG.dlc_regions))
        self.assertIs(False, self.world.fill_slot_data()["dlc"])
        self.assertFalse(self.world.options.dlc)


class TestDlcOn(EmberKnightsTestBase):
    options = {"dlc": True}

    def test_counts_and_item_pool_include_the_dlc(self) -> None:
        locations = regular_locations(self)
        self.assertEqual(expected_location_count(True) + expected_dlc_location_count(True), len(locations))
        self.assertEqual(len(locations), len(self.multiworld.itempool))
        self.assertIs(True, self.world.fill_slot_data()["dlc"])

    def test_every_dlc_check_exists_and_has_its_own_id(self) -> None:
        names = {location.name: location.address for location in regular_locations(self)}
        for name in self.world.location_name_groups["DLC"]:
            self.assertIn(name, names, name)
            self.assertEqual(CATALOG.location_name_to_id[name], names[name])

    def test_dlc_skills_weapons_and_orb_relics_are_items_exactly_once(self) -> None:
        pool = [item.name for item in self.multiworld.itempool]
        for name in [weapon.name for weapon in CATALOG.dlc_weapons]:
            self.assertEqual(1, pool.count(name), name)
        # A DLC skill is in the pool exactly once, unless the seed starts with it, in which case it is not.
        starting_skills = set(self.world.starting_skill_ids)
        for skill in CATALOG.dlc_skills:
            self.assertEqual(0 if skill.ability_id in starting_skills else 1, pool.count(skill.name), skill.name)
        # A DLC orb relic is in the pool exactly once, unless the seed starts with it, in which case it is not.
        starting = set(self.world.starting_relic_ids)
        for orb in CATALOG.dlc_relic_orbs:
            self.assertEqual(0 if orb.def_id in starting else 1, pool.count(orb.name), orb.name)

    def test_dlc_orbs_continue_the_base_numbering(self) -> None:
        names = [orb.location_name for orb in CATALOG.dlc_relic_orbs]
        self.assertEqual(
            [f"Relic Orb {number}" for number in range(len(CATALOG.relic_orbs) + 1, len(CATALOG.relic_orbs) + 11)], names
        )

    def test_dlc_weapons_are_never_starting_weapons(self) -> None:
        dlc_ids = {weapon.weapon_id for weapon in CATALOG.dlc_weapons}
        self.assertFalse(dlc_ids & set(self.world.starting_weapon_ids))

    def test_dlc_weapon_mods_need_their_weapon(self) -> None:
        state = CollectionState(self.multiworld)
        locked = [location for location in regular_locations(self) if location.name in {mod.location_name for mod in CATALOG.dlc_weapon_mods}]
        self.assertTrue(locked)
        # With nothing collected, no DLC mod check is reachable.
        for location in locked:
            self.assertFalse(location.can_reach(state), location.name)
        for weapon in CATALOG.dlc_weapons:
            state.collect(self.world.create_item(weapon.name), prevent_sweep=True)
        for location in locked:
            self.assertTrue(location.can_reach(state), location.name)

    def test_dlc_chain_is_gated_by_steadfast_citadel_then_each_previous_dlc_clear(self) -> None:
        state = CollectionState(self.multiworld)
        first = self.multiworld.get_region(CATALOG.dlc_regions[0], self.player)
        self.assertFalse(first.can_reach(state))
        # Steadfast Citadel itself needs Scoggfirth's clear, so the DLC is not open before the base chain gets that far.
        state.prog_items[self.player]["Scoggfirth Clear"] = 1
        state.update_reachable_regions(self.player)
        self.assertFalse(first.can_reach(state), "the first DLC world opened before Steadfast Citadel was cleared")
        # Steadfast Citadel's clear then opens the first DLC world; each later one needs the previous DLC clear.
        state.prog_items[self.player]["Steadfast Citadel Clear"] = 1
        previous = None
        for name in CATALOG.dlc_regions:
            region = self.multiworld.get_region(name, self.player)
            if previous is not None:
                self.assertFalse(region.can_reach(state), f"{name} reachable before {previous} was cleared")
                state.prog_items[self.player][f"{previous} Clear"] = 1
            state.update_reachable_regions(self.player)
            self.assertTrue(region.can_reach(state), name)
            previous = name

    def test_the_goal_is_unchanged_by_the_dlc(self) -> None:
        self.assertEqual("praxis", self.world.fill_slot_data()["goal"])
        self.assertIn("Netherra", {region.name for region in self.multiworld.get_regions(self.player)})


class TestDlcOnWithoutBossVariants(EmberKnightsTestBase):
    options = {"dlc": True, "boss_variants": False}

    def test_the_architect_variant_is_dropped_with_the_other_variants(self) -> None:
        names = {location.name for location in regular_locations(self)}
        self.assertNotIn("The Architect Reborn Defeated", names)
        self.assertIn("The Architect Defeated", names)
        self.assertEqual(expected_location_count(False) + expected_dlc_location_count(False), len(names))


class TestDlcCatalogContract(unittest.TestCase):
    def test_dlc_ids_never_collide_with_or_move_any_base_id(self) -> None:
        base = CATALOG.id_base
        dlc_items = (
            [CATALOG.dlc_skill_item_id(i) for i in range(len(CATALOG.dlc_skills))]
            + [CATALOG.dlc_weapon_item_id(i) for i in range(len(CATALOG.dlc_weapons))]
            + [CATALOG.dlc_relic_item_id(i) for i in range(len(CATALOG.dlc_relic_orbs))]
        )
        dlc_locations = (
            [CATALOG.dlc_skill_location_id(i) for i in range(len(CATALOG.dlc_skills))]
            + [CATALOG.dlc_miniboss_location_id(i) for i in range(len(CATALOG.dlc_minibosses))]
            + [CATALOG.dlc_boss_location_id(i) for i in range(len(CATALOG.dlc_bosses))]
            + [CATALOG.dlc_weapon_mod_location_id(i) for i in range(len(CATALOG.dlc_weapon_mods))]
            + [CATALOG.dlc_room_milestone_location_id(i) for i in range(len(CATALOG.dlc_room_milestones))]
            + [CATALOG.dlc_relic_orb_location_id(i) for i in range(len(CATALOG.dlc_relic_orbs))]
        )
        base_items = (
            [CATALOG.skill_item_id(i) for i in range(len(CATALOG.skills))]
            + [CATALOG.standard_skill_item_id(i) for i in range(len(CATALOG.standard_skills))]
            + [CATALOG.weapon_item_id(i) for i in range(len(CATALOG.weapons))]
            + [CATALOG.filler_item_id(i) for i in range(len(CATALOG.fillers))]
            + [CATALOG.relic_item_id(i) for i in range(len(CATALOG.relic_orbs))]
            + [CATALOG.heat_reduction_item_id()]
        )
        base_locations = (
            [CATALOG.skill_location_id(i) for i in range(len(CATALOG.skills))]
            + [CATALOG.miniboss_location_id(i) for i in range(len(CATALOG.minibosses))]
            + [CATALOG.boss_location_id(i) for i in range(len(CATALOG.bosses))]
            + [CATALOG.ember_tree_location_id(i) for i in range(len(CATALOG.ember_tree))]
            + [CATALOG.weapon_mod_location_id(i) for i in range(len(CATALOG.weapon_mods))]
            + [CATALOG.room_milestone_location_id(i) for i in range(len(CATALOG.room_milestones))]
            + [CATALOG.relic_orb_location_id(i) for i in range(len(CATALOG.relic_orbs))]
        )
        self.assertFalse(set(dlc_items) & set(base_items))
        self.assertFalse(set(dlc_locations) & set(base_locations))
        self.assertEqual(len(dlc_items), len(set(dlc_items)))
        self.assertEqual(len(dlc_locations), len(set(dlc_locations)))
        # The base ids are exactly where 1.2.0 put them.
        self.assertEqual(base + 1, CATALOG.skill_item_id(0))
        self.assertEqual(base + 7000, CATALOG.relic_orb_location_id(0))
        self.assertEqual(base + 7052, CATALOG.relic_orb_location_id(52))

    def test_dlc_data_matches_what_the_game_reported(self) -> None:
        # Counts read from the in-game dump: 9 selector skills + the Architect pair is 10 tablets (Gun Ammo is not a tablet).
        self.assertEqual(10, len(CATALOG.dlc_skills))
        self.assertEqual(2, len(CATALOG.dlc_weapons))
        self.assertEqual(10, len(CATALOG.dlc_relic_orbs))
        self.assertEqual(4, len(CATALOG.dlc_minibosses))
        self.assertEqual(6, len(CATALOG.dlc_bosses))
        # 20 DLC mods minus the always-unlocked Gun 1-1.
        self.assertEqual(19, len(CATALOG.dlc_weapon_mods))
        self.assertEqual({8, 9}, {weapon.weapon_id for weapon in CATALOG.dlc_weapons})
        self.assertEqual(list(range(211, 221)), [orb.def_id for orb in CATALOG.dlc_relic_orbs])

    def test_dlc_enemy_ids_do_not_overlap_the_base_ones(self) -> None:
        base = {enemy for encounter in [*CATALOG.minibosses, *CATALOG.bosses] for enemy in encounter.enemy_ids}
        dlc = [enemy for encounter in [*CATALOG.dlc_minibosses, *CATALOG.dlc_bosses] for enemy in encounter.enemy_ids]
        self.assertFalse(base & set(dlc))
        self.assertEqual(len(dlc), len(set(dlc)))

    def test_dlc_ability_ids_do_not_overlap_base_skills(self) -> None:
        base = {skill.ability_id for skill in CATALOG.skills} | {skill.ability_id for skill in CATALOG.standard_skills}
        self.assertFalse(base & {skill.ability_id for skill in CATALOG.dlc_skills})

    def test_the_dlc_option_defaults_to_off(self) -> None:
        from ..options import Dlc

        self.assertEqual(0, Dlc.default)
class TestCatalogContract(unittest.TestCase):
    def test_ids_and_names_are_unique(self) -> None:
        self.assertEqual(
            len(CATALOG.skills)
            + len(CATALOG.standard_skills)
            + len(CATALOG.weapons)
            + len(CATALOG.relic_orbs)
            + len(CATALOG.default_relics)
            + len(CATALOG.fillers)
            + 1
            + len(CATALOG.dlc_skills)
            + len(CATALOG.dlc_weapons)
            + len(CATALOG.dlc_relic_orbs),
            len(CATALOG.item_name_to_id),
        )
        self.assertEqual(
            len(CATALOG.skills)
            + len(CATALOG.minibosses)
            + len(CATALOG.bosses)
            + len(CATALOG.ember_tree)
            + len(CATALOG.weapon_mods)
            + len(CATALOG.room_milestones)
            + len(CATALOG.relic_orbs)
            + len(CATALOG.dlc_skills)
            + len(CATALOG.dlc_minibosses)
            + len(CATALOG.dlc_bosses)
            + len(CATALOG.dlc_weapon_mods)
            + len(CATALOG.dlc_room_milestones)
            + len(CATALOG.dlc_relic_orbs),
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
    """Default options: no traps, Ash on. Ash takes every filler slot because 50 Gold is retired."""

    def test_ash_is_the_only_filler(self) -> None:
        counts = filler_counts(self)
        self.assertEqual(0, counts["traps"])
        self.assertEqual(0, counts["gold"])
        self.assertGreater(counts["ash"], 0)
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

    def test_two_way_split_and_amount_in_slot_data(self) -> None:
        counts = filler_counts(self)
        self.assertEqual(0, counts["gold"])
        self.assertIn(counts["ash"] - counts["traps"], (0, 1))
        self.assertEqual(20, self.world.fill_slot_data()["ash_filler_amount"])


class TestNoFillerKindEnabledFallsBackToAsh(EmberKnightsTestBase):
    options = {"ash_filler_amount": 0, "traps": False}

    def test_the_pool_still_fills_every_location_without_gold(self) -> None:
        counts = filler_counts(self)
        self.assertEqual(0, counts["gold"])
        self.assertEqual(0, counts["traps"])
        self.assertGreater(counts["ash"], 0)
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))
        self.assertEqual("Ash", self.world.get_filler_item_name())


class TestTrapEnemiesAnyWorldDefault(EmberKnightsTestBase):
    options = {}

    def test_off_by_default_and_in_slot_data(self) -> None:
        self.assertEqual(0, self.world.options.trap_enemies_any_world.value)
        self.assertIs(False, bool(self.world.fill_slot_data()["trap_enemies_any_world"]))

    def test_the_option_changes_no_items_or_locations(self) -> None:
        self.assertEqual(228, len(regular_locations(self)))
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))


class TestTrapEnemiesAnyWorldOn(EmberKnightsTestBase):
    options = {"trap_enemies_any_world": True, "traps": True}

    def test_slot_data_carries_the_option(self) -> None:
        self.assertTrue(self.world.fill_slot_data()["trap_enemies_any_world"])
        self.assertTrue(self.world.fill_slot_data()["traps"])

    def test_it_does_not_touch_the_pool(self) -> None:
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))


class TestTrapEnemiesAnyWorldWithDlc(EmberKnightsTestBase):
    """The option is independent of the DLC: both are reported, and the client decides what the pool may hold."""

    options = {"trap_enemies_any_world": True, "dlc": True, "traps": True}

    def test_both_flags_reach_the_client(self) -> None:
        slot_data = self.world.fill_slot_data()
        self.assertTrue(slot_data["trap_enemies_any_world"])
        self.assertIs(True, slot_data["dlc"])
        self.assertEqual(len(regular_locations(self)), len(self.multiworld.itempool))


class TestTrapEnemiesAnyWorldWithoutDlc(EmberKnightsTestBase):
    options = {"trap_enemies_any_world": True, "dlc": False}

    def test_the_dlc_stays_off_so_the_client_excludes_dlc_enemies(self) -> None:
        slot_data = self.world.fill_slot_data()
        self.assertTrue(slot_data["trap_enemies_any_world"])
        self.assertIs(False, slot_data["dlc"])


class TestTrapEnemiesAnyWorldOptionContract(unittest.TestCase):
    def test_it_is_a_toggle_that_defaults_to_off(self) -> None:
        from Options import Toggle

        from ..options import TrapEnemiesAnyWorld

        self.assertTrue(issubclass(TrapEnemiesAnyWorld, Toggle))
        self.assertEqual(0, TrapEnemiesAnyWorld.default)


class TestGoldIsRetiredButStillKnown(unittest.TestCase):
    def test_gold_stays_in_the_catalog_so_old_seeds_still_resolve(self) -> None:
        self.assertEqual(CATALOG.id_base + 203, CATALOG.item_name_to_id["50 Gold"])


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

    def test_the_old_mode_name_is_no_longer_accepted(self) -> None:
        from ..options import HeatMode

        self.assertFalse(hasattr(HeatMode, "alias_random_assortment_late_blitz"))
        self.assertNotIn("late_blitz", HeatMode.name_lookup.values())



class TestOtherHeatModesAreUnchanged(unittest.TestCase):
    def test_option_values(self) -> None:
        from ..options import HeatMode

        self.assertEqual(0, HeatMode.option_random_assortment)
        self.assertEqual(1, HeatMode.option_sequential)
        self.assertEqual(2, HeatMode.option_curated_heat_difficulty)
        self.assertEqual(0, HeatMode.default)
        self.assertNotIn("curated_heat_difficulty", ("random_assortment", "sequential"))