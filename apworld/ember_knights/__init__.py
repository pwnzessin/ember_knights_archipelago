import logging
from typing import Any

from BaseClasses import Item, ItemClassification, Location, Region
from rule_builder.rules import Has
from worlds.AutoWorld import World

from .catalog import CATALOG
from .options import EmberKnightsOptions, Goal, HeatMode, HeatReroll


# The heat at and above which the game only offers the Arch Praxis fight (the Doom difficulty threshold). The client holds the
# active heat at the seed's minimum heat, so a seed whose minimum is this high never gets the normal Praxis fight.
ARCH_PRAXIS_HEAT = 15


def praxis_goal_is_unreachable_note(goal_is_praxis: bool, minimum_heat: int, start_heat: int) -> str | None:
    """Returns a warning when a Praxis goal can only be finished through Arch Praxis, or None when it needs no warning.

    The active heat never goes below the minimum heat, and never above the start heat, so the heat the player can ever have is
    at least min(start, minimum) and the normal Praxis fight needs the heat to be able to drop below the Arch Praxis threshold.
    Reaching the goal still works: the client counts a defeated Arch Praxis as completing a Praxis goal. The note exists so the
    player is not surprised that the easier fight never appears.
    """
    if not goal_is_praxis:
        return None
    lowest_possible_heat = min(int(start_heat), int(minimum_heat))
    if lowest_possible_heat < ARCH_PRAXIS_HEAT:
        return None
    return (
        f"Goal is Praxis, but the lowest heat this seed can ever have is {lowest_possible_heat} (Minimum Heat {minimum_heat}, "
        f"Start Heat {start_heat}). At heat {ARCH_PRAXIS_HEAT} or more the game only offers the Arch Praxis fight, so the normal "
        f"Praxis fight will never appear. Defeating Arch Praxis completes a Praxis goal, so the seed stays winnable. Lower "
        f"Minimum Heat below {ARCH_PRAXIS_HEAT} (and Start Heat with it) to be able to face the normal Praxis."
    )


class EmberKnightsItem(Item):
    game = "Ember Knights"


class EmberKnightsLocation(Location):
    game = "Ember Knights"


class EmberKnightsWorld(World):
    """Roguelite skill and progression randomization for Ember Knights.

    All weapons stay unlocked for the player. Skill tablets, relic orbs, weapon mods, Ember Tree
    nodes, boss encounters, and world progress are the checks; skills and relics are the items.
    """

    game = "Ember Knights"
    options_dataclass = EmberKnightsOptions
    options: EmberKnightsOptions
    topology_present = True

    item_name_to_id = CATALOG.item_name_to_id
    location_name_to_id = CATALOG.location_name_to_id
    item_name_groups = {
        "Skills": {skill.name for skill in CATALOG.skills}
        | {skill.name for skill in CATALOG.standard_skills}
        | {skill.name for skill in CATALOG.dlc_skills},
        "Standard Skills": {skill.name for skill in CATALOG.standard_skills},
        "Weapons": {weapon.name for weapon in CATALOG.weapons} | {weapon.name for weapon in CATALOG.dlc_weapons},
        "Relics": {orb.name for orb in CATALOG.relic_orbs}
        | {orb.name for orb in CATALOG.dlc_relic_orbs}
        | {relic.name for relic in CATALOG.default_relics},
        "Heat": {CATALOG.heat_reduction_item_name},
        "Archipelago Coins": {bundle.name for bundle in CATALOG.coin_bundles},
        "Traps": {trap.name for trap in CATALOG.trap_items},
        "Resources": {"50 Gold", CATALOG.ash_item_name},  # 50 Gold is retired from the pool but kept for old seeds
    }
    location_name_groups = {
        "Ember Tree": {node.location_name for node in CATALOG.ember_tree},
        "Weapon Mods": {mod.location_name for mod in CATALOG.weapon_mods}
        | {mod.location_name for mod in CATALOG.dlc_weapon_mods},
        "Room Milestones": {milestone.location_name for milestone in CATALOG.room_milestones}
        | {milestone.location_name for milestone in CATALOG.dlc_room_milestones},
        "Relic Orbs": {orb.location_name for orb in CATALOG.relic_orbs}
        | {orb.location_name for orb in CATALOG.dlc_relic_orbs},
        "DLC": (
            {skill.location_name for skill in CATALOG.dlc_skills}
            | {encounter.location_name for encounter in [*CATALOG.dlc_minibosses, *CATALOG.dlc_bosses]}
            | {mod.location_name for mod in CATALOG.dlc_weapon_mods}
            | {milestone.location_name for milestone in CATALOG.dlc_room_milestones}
            | {orb.location_name for orb in CATALOG.dlc_relic_orbs}
        ),
    }

    _region_order = [
        "Scoggfirth",
        "Steadfast Citadel",
        "Wightsbury",
        "Anville",
        "Netherra",
        "Architect's Realm",
    ]

    def __init__(self, multiworld: Any, player: int) -> None:
        super().__init__(multiworld, player)
        self.starting_weapon_ids: list[int] = [1]
        self.dlc_enabled = False
        self.starting_skill_ids: list[int] = []
        self.starting_relic_ids: list[int] = []

    def relic_pool(self) -> list[tuple[int, str]]:
        """Every relic this seed can hold as an item: the 43 relics the game unlocks for free, the 53 base orb relics
        and, with the DLC, the 10 DLC orb relics. Ordered by relic id so the same seed always rolls the same way."""
        pool = {relic.def_id: relic.name for relic in CATALOG.default_relics}
        pool.update({orb.def_id: orb.name for orb in CATALOG.relic_orbs})
        if self.dlc_enabled:
            pool.update({orb.def_id: orb.name for orb in CATALOG.dlc_relic_orbs})
        return sorted(pool.items())

    # How many starting relics are guaranteed to be a Common relic with no category requirement (the relics a relic selector
    # can always offer). With the minimum start of 10 this is most of the start, which is the point: the first selectors then
    # rotate through several relics instead of repeating the same few.
    GUARANTEED_STARTER_FRIENDLY_RELICS = 7

    def pick_starting_relics(self, pool: list[tuple[int, str]], count: int) -> list[tuple[int, str]]:
        """Chooses the starting relics: first GUARANTEED_STARTER_FRIENDLY_RELICS (or `count` if smaller) at random from the
        Common relics without a category requirement, then the rest at random from everything still left. The result has
        exactly `count` distinct relics from `pool`. Draws only from this world's own random source, so a seed always
        rolls the same way."""
        friendly = [entry for entry in pool if entry[0] in CATALOG.starter_friendly_relics]
        guaranteed = min(self.GUARANTEED_STARTER_FRIENDLY_RELICS, count, len(friendly))
        first = self.random.sample(friendly, guaranteed)
        taken = {def_id for def_id, _ in first}
        rest = [entry for entry in pool if entry[0] not in taken]
        return first + self.random.sample(rest, count - guaranteed)

    def skill_pool(self) -> list[tuple[int, str]]:
        """Every skill this seed can hold as an item: the 7 standard skills the game unlocks for free, the 10 tablet skills
        and, with the DLC, the 10 DLC tablet skills. Ordered by ability id so the same seed always rolls the same way."""
        pool = {skill.ability_id: skill.name for skill in CATALOG.standard_skills}
        pool.update({skill.ability_id: skill.name for skill in CATALOG.skills})
        if self.dlc_enabled:
            pool.update({skill.ability_id: skill.name for skill in CATALOG.dlc_skills})
        return sorted(pool.items())

    def generate_early(self) -> None:
        self.dlc_enabled = bool(self.options.dlc)
        note = praxis_goal_is_unreachable_note(
            self.options.goal == Goal.option_praxis, int(self.options.minimum_heat), int(self.options.start_heat)
        )
        if note:
            # A warning, not an error: the seed is still winnable because Arch Praxis completes a Praxis goal.
            logging.warning("Ember Knights (%s): %s", self.player_name, note)
        extra_count = max(0, min(len(CATALOG.weapons), int(self.options.extra_starting_weapons)))
        chosen = self.random.sample(CATALOG.weapons, extra_count)
        self.starting_weapon_ids = [1, *sorted(weapon.weapon_id for weapon in chosen)]
        for weapon in chosen:
            self.multiworld.push_precollected(self.create_item(weapon.name))

        # Pick which skills this seed starts with, out of the one big pool. The others become items in the pool.
        skills = self.skill_pool()
        skill_count = max(3, min(len(skills), int(self.options.starting_skills)))
        chosen_skills = self.random.sample(skills, skill_count)
        self.starting_skill_ids = sorted(ability_id for ability_id, _ in chosen_skills)
        for _, name in chosen_skills:
            self.multiworld.push_precollected(self.create_item(name))

        # Pick which relics this seed starts with, out of the one big pool. The others become items in the pool. A relic
        # selector rolls each card's rarity (about 84% Common) and only offers relics the player may have, so a start of mostly
        # Epic relics, or relics that need a category the player does not hold yet, repeats the same few. Guarantee a number
        # of Common relics with no category requirement among the starting relics; the rest of the start stays random.
        pool = self.relic_pool()
        relic_count = max(10, min(43, int(self.options.starting_relics)))
        chosen_relics = self.pick_starting_relics(pool, relic_count)
        self.starting_relic_ids = sorted(def_id for def_id, _ in chosen_relics)
        for _, name in chosen_relics:
            self.multiworld.push_precollected(self.create_item(name))

    def create_item(self, name: str) -> EmberKnightsItem:
        classification = ItemClassification.filler
        if name in self.item_name_groups["Archipelago Coins"] or name in self.item_name_groups["Weapons"]:
            classification = ItemClassification.progression
        elif name in self.item_name_groups["Traps"]:
            classification = ItemClassification.trap
        elif (
            name in self.item_name_groups["Skills"]
            or name in self.item_name_groups["Relics"]
            or name == CATALOG.heat_reduction_item_name
        ):
            classification = ItemClassification.useful
        return EmberKnightsItem(name, classification, self.item_name_to_id[name], self.player)

    def _has_archipelago_coins(self, state: Any, required: int) -> bool:
        if required <= 0:
            return True
        starting = max(1, int(self.options.starting_archipelago_coins))
        total = starting + sum(bundle.amount * state.count(bundle.name, self.player) for bundle in CATALOG.coin_bundles)
        return total >= required

    def heat_reduction_item_count(self) -> int:
        span = max(0, int(self.options.start_heat) - int(self.options.minimum_heat))
        per_item = max(1, int(self.options.heat_reduction_per_item))
        return (span + per_item - 1) // per_item

    def get_filler_item_name(self) -> str:
        return CATALOG.ash_item_name

    def create_regions(self) -> None:
        menu = Region("Menu", self.player, self.multiworld)
        # The Ember Tree and weapon-mod menus are available from the very start of the game.
        nexus = Region("Nexus", self.player, self.multiworld)
        regions = {name: Region(name, self.player, self.multiworld) for name in self._region_order}
        dlc_regions = (
            {name: Region(name, self.player, self.multiworld) for name in CATALOG.dlc_regions}
            if self.dlc_enabled
            else {}
        )
        self.multiworld.regions += [menu, nexus, *regions.values(), *dlc_regions.values()]
        menu.connect(nexus, "Menu to Nexus")

        for node in CATALOG.ember_tree:
            nexus.add_locations(
                {node.location_name: self.location_name_to_id[node.location_name]},
                EmberKnightsLocation,
            )
            if node.required_coins > 0:
                location = self.multiworld.get_location(node.location_name, self.player)
                location.access_rule = (
                    lambda state, required=node.required_coins: self._has_archipelago_coins(state, required)
                )
        for mod in CATALOG.weapon_mods:
            nexus.add_locations(
                {mod.location_name: self.location_name_to_id[mod.location_name]},
                EmberKnightsLocation,
            )
            weapon = CATALOG.weapons_by_id.get(mod.weapon_type)
            if weapon is not None:
                location = self.multiworld.get_location(mod.location_name, self.player)
                location.access_rule = lambda state, weapon_name=weapon.name: state.has(weapon_name, self.player)

        if self.dlc_enabled:
            for mod in CATALOG.dlc_weapon_mods:
                nexus.add_locations(
                    {mod.location_name: self.location_name_to_id[mod.location_name]},
                    EmberKnightsLocation,
                )
                weapon = CATALOG.dlc_weapons_by_id.get(mod.weapon_type)
                if weapon is not None:
                    location = self.multiworld.get_location(mod.location_name, self.player)
                    location.access_rule = lambda state, weapon_name=weapon.name: state.has(weapon_name, self.player)

        for milestone in CATALOG.room_milestones:
            regions[milestone.world].add_locations(
                {milestone.location_name: self.location_name_to_id[milestone.location_name]},
                EmberKnightsLocation,
            )

        # Orbs drop from enemy kill counters in any world, so they live in the first world, which every
        # run starts in and which is open from the beginning.
        regions["Scoggfirth"].add_locations(
            {orb.location_name: self.location_name_to_id[orb.location_name] for orb in CATALOG.relic_orbs},
            EmberKnightsLocation,
        )

        for skill in CATALOG.skills:
            regions[skill.region].add_locations(
                {skill.location_name: self.location_name_to_id[skill.location_name]}, EmberKnightsLocation
            )

        for encounter in [*CATALOG.minibosses, *CATALOG.bosses]:
            if encounter.variant and not self.options.boss_variants:
                continue
            regions[encounter.region].add_locations(
                {encounter.location_name: self.location_name_to_id[encounter.location_name]},
                EmberKnightsLocation,
            )

        previous_region = menu
        previous_clear_item = None
        for region_name in self._region_order:
            region = regions[region_name]
            if previous_clear_item is not None:
                previous_region.connect(
                    region,
                    f"{previous_region.name} to {region_name}",
                    lambda state, required=previous_clear_item: state.has(required, self.player),
                )
            else:
                previous_region.connect(region, f"Menu to {region_name}")

            clear_item = f"{region_name} Clear"
            region.add_event(
                f"{region_name} Cleared",
                clear_item,
                location_type=EmberKnightsLocation,
                item_type=EmberKnightsItem,
            )
            previous_region = region
            previous_clear_item = clear_item

        if self.dlc_enabled:
            self._create_dlc_regions(menu, regions, dlc_regions)

        if self.options.goal == Goal.option_praxis:
            regions["Netherra"].add_event(
                "Praxis Victory", "Victory", location_type=EmberKnightsLocation, item_type=EmberKnightsItem
            )
        else:
            regions["Architect's Realm"].add_event(
                "Arch Praxis Victory", "Victory", location_type=EmberKnightsLocation, item_type=EmberKnightsItem
            )

    def _create_dlc_regions(self, menu: Region, regions: dict[str, Region], dlc_regions: dict[str, Region]) -> None:
        # Orbs drop from enemy kill counters in any world. The DLC orbs live in the first DLC world, which opens as
        # soon as the DLC does, just like the base orbs live in the first base world.
        first = dlc_regions[CATALOG.dlc_regions[0]]
        first.add_locations(
            {orb.location_name: self.location_name_to_id[orb.location_name] for orb in CATALOG.dlc_relic_orbs},
            EmberKnightsLocation,
        )
        for milestone in CATALOG.dlc_room_milestones:
            dlc_regions[milestone.world].add_locations(
                {milestone.location_name: self.location_name_to_id[milestone.location_name]}, EmberKnightsLocation
            )
        for skill in CATALOG.dlc_skills:
            dlc_regions[skill.region].add_locations(
                {skill.location_name: self.location_name_to_id[skill.location_name]}, EmberKnightsLocation
            )
        for encounter in [*CATALOG.dlc_minibosses, *CATALOG.dlc_bosses]:
            if encounter.variant and not self.options.boss_variants:
                continue
            dlc_regions[encounter.region].add_locations(
                {encounter.location_name: self.location_name_to_id[encounter.location_name]},
                EmberKnightsLocation,
            )

        # The game unlocks the DLC after meeting Evee and seeing Praxis in the second base world, so the DLC chain opens
        # once Steadfast Citadel is cleared. After that each DLC world opens when the previous one is cleared.
        previous_region = regions["Steadfast Citadel"]
        previous_clear_item = "Steadfast Citadel Clear"
        for region_name in CATALOG.dlc_regions:
            region = dlc_regions[region_name]
            previous_region.connect(
                region,
                f"{previous_region.name} to {region_name}",
                lambda state, required=previous_clear_item: state.has(required, self.player),
            )
            clear_item = f"{region_name} Clear"
            region.add_event(
                f"{region_name} Cleared",
                clear_item,
                location_type=EmberKnightsLocation,
                item_type=EmberKnightsItem,
            )
            previous_region = region
            previous_clear_item = clear_item

    def set_rules(self) -> None:
        self.set_completion_rule(Has("Victory"))

    def create_items(self) -> None:
        # Every skill in the big pool that is not a starting skill is an item. The tablet skills keep their locations (the
        # tablets are checks whatever you started with); the standard skills have none, so they only add items.
        permanent_items = [name for ability_id, name in self.skill_pool() if ability_id not in self.starting_skill_ids]
        permanent_items.extend(
            weapon.name for weapon in CATALOG.weapons if weapon.weapon_id not in self.starting_weapon_ids
        )
        # Every relic in the big pool that is not a starting relic is an item. The orb relics keep their locations
        # ("Relic Orb NN"); the 43 default relics have none, so they only add items.
        permanent_items.extend(name for def_id, name in self.relic_pool() if def_id not in self.starting_relic_ids)
        if self.dlc_enabled:
            permanent_items.extend(weapon.name for weapon in CATALOG.dlc_weapons)
        permanent_items.extend(CATALOG.heat_reduction_item_name for _ in range(self.heat_reduction_item_count()))
        permanent_items.extend(self.coin_bundle_names())
        self.multiworld.itempool += [self.create_item(name) for name in permanent_items]

        regular_location_count = sum(
            location.address is not None for location in self.multiworld.get_locations(self.player)
        )
        filler_count = regular_location_count - len(permanent_items)
        if filler_count < 0:
            raise RuntimeError("Ember Knights has more permanent items than regular locations.")
        self.multiworld.itempool += [self.create_item(name) for name in self.filler_pool_names(filler_count)]

    # 80 bundles totaling 115 Archipelago Coins: enough to buy all 30 node unlocks and all 70 level-ups (100 coins)
    # with a 15 coin buffer. The largest bundle is 2 so the Ember Tree opens up gradually; "3 Archipelago Coins" is
    # no longer placed. It stays in the catalog because item IDs are positions in the filler list, and a seed that
    # was already generated may still send it.
    COIN_BUNDLES_IN_POOL = (("2 Archipelago Coins", 35), ("1 Archipelago Coin", 45))

    def coin_bundle_names(self) -> list[str]:
        return [name for name, count in self.COIN_BUNDLES_IN_POOL for _ in range(count)]

    def filler_pool_names(self, filler_count: int) -> list[str]:
        """Names for every filler slot. Slots are shared evenly between the enabled kinds: Ash (unless its amount is
        0) and traps (when enabled). Any remainder goes to the first kind.

        50 Gold is no longer placed: the relic pool added up to 43 items and the Gold slots made room for them. It
        stays in the catalog because item IDs are positions in the filler list, and a seed that was already generated
        may still send it. When Ash is set to 0 and traps are off there would be nothing left to place, so the filler
        falls back to Ash (an Ash item still gives nothing once every unbought upgrade is paid for, which is harmless).
        """
        kinds: list[list[str]] = []
        if int(self.options.ash_filler_amount) > 0:
            kinds.append([CATALOG.ash_item_name])
        if self.options.traps:
            kinds.append([trap.name for trap in CATALOG.trap_items])
        if not kinds:
            kinds.append([CATALOG.ash_item_name])

        share, remainder = divmod(filler_count, len(kinds))
        names: list[str] = []
        for index, choices in enumerate(kinds):
            count = share + (remainder if index == 0 else 0)
            names.extend(self.random.choice(choices) for _ in range(count))
        return names

    def fill_slot_data(self) -> dict[str, Any]:
        slot_data = self.options.as_dict(
            "boss_variants",
            "death_link",
            "start_heat",
            "minimum_heat",
            "heat_reduction_per_item",
            "starting_archipelago_coins",
            "extra_starting_weapons",
            "starting_skills",
            "starting_relics",
            "ash_filler_amount",
            "traps",
            "trap_enemies_any_world",
        )
        slot_data["starting_weapon_ids"] = list(self.starting_weapon_ids)
        # Presence of "dlc" tells the client this seed includes the DLC checks and items. Seeds from before this key
        # existed lack it, and the client then treats the DLC as off, exactly as it always did.
        slot_data["dlc"] = bool(self.dlc_enabled)
        # Presence of this key tells the client the skills are gated by this seed: the standard skills stop being free,
        # the tablets stop granting their skill, and only these starting skills (plus whatever the multiworld sends) are
        # unlocked. Seeds generated before the option existed carry starting_standard_skill_ids instead (standard skills
        # only, tablets always items); the client still understands that key, and a seed with neither leaves every
        # standard skill unlocked as in the base game.
        slot_data["starting_skill_ids"] = list(self.starting_skill_ids)
        # Presence of this key tells the client the relics are gated by this seed: the default relics stop being free
        # and only these ones (plus whatever the multiworld sends) are unlocked. Seeds generated before the option
        # existed lack it, and the client then leaves every default relic unlocked as in the base game.
        slot_data["starting_relic_ids"] = list(self.starting_relic_ids)
        slot_data["heat_reroll"] = "once" if self.options.heat_reroll == HeatReroll.option_once else "every_run"

        # heat_mode is the single source of truth for how heat is chosen, including whether the curated tiers apply.
        if self.options.heat_mode == HeatMode.option_sequential:
            slot_data["heat_mode"] = "sequential"
        elif self.options.heat_mode == HeatMode.option_curated_heat_difficulty:
            slot_data["heat_mode"] = "curated_heat_difficulty"
        else:
            slot_data["heat_mode"] = "random_assortment"
        slot_data["goal"] = "arch_praxis" if self.options.goal == Goal.option_arch_praxis else "praxis"
        slot_data["mod_version"] = "1.6.4"
        return slot_data
