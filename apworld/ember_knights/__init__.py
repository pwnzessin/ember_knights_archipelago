from typing import Any

from BaseClasses import Item, ItemClassification, Location, Region
from rule_builder.rules import Has
from worlds.AutoWorld import World

from .catalog import CATALOG
from .options import EmberKnightsOptions, Goal, HeatMode, HeatReroll


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
        "Skills": {skill.name for skill in CATALOG.skills} | {skill.name for skill in CATALOG.standard_skills},
        "Standard Skills": {skill.name for skill in CATALOG.standard_skills},
        "Weapons": {weapon.name for weapon in CATALOG.weapons},
        "Relics": {orb.name for orb in CATALOG.relic_orbs},
        "Heat": {CATALOG.heat_reduction_item_name},
        "Archipelago Coins": {bundle.name for bundle in CATALOG.coin_bundles},
        "Traps": {trap.name for trap in CATALOG.trap_items},
        "Resources": {"50 Gold", CATALOG.ash_item_name},
    }
    location_name_groups = {
        "Ember Tree": {node.location_name for node in CATALOG.ember_tree},
        "Weapon Mods": {mod.location_name for mod in CATALOG.weapon_mods},
        "Room Milestones": {milestone.location_name for milestone in CATALOG.room_milestones},
        "Relic Orbs": {orb.location_name for orb in CATALOG.relic_orbs},
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
        self.starting_standard_skill_ids: list[int] = []

    def generate_early(self) -> None:
        extra_count = max(0, min(len(CATALOG.weapons), int(self.options.extra_starting_weapons)))
        chosen = self.random.sample(CATALOG.weapons, extra_count)
        self.starting_weapon_ids = [1, *sorted(weapon.weapon_id for weapon in chosen)]
        for weapon in chosen:
            self.multiworld.push_precollected(self.create_item(weapon.name))

        # Pick which standard skills this seed starts with. The others become items in the pool.
        skill_count = max(3, min(len(CATALOG.standard_skills), int(self.options.starting_standard_skills)))
        chosen_skills = self.random.sample(CATALOG.standard_skills, skill_count)
        self.starting_standard_skill_ids = sorted(skill.ability_id for skill in chosen_skills)
        for skill in chosen_skills:
            self.multiworld.push_precollected(self.create_item(skill.name))

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
        return "50 Gold"

    def create_regions(self) -> None:
        menu = Region("Menu", self.player, self.multiworld)
        # The Ember Tree and weapon-mod menus are available from the very start of the game.
        nexus = Region("Nexus", self.player, self.multiworld)
        regions = {name: Region(name, self.player, self.multiworld) for name in self._region_order}
        self.multiworld.regions += [menu, nexus, *regions.values()]
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

        if self.options.goal == Goal.option_praxis:
            regions["Netherra"].add_event(
                "Praxis Victory", "Victory", location_type=EmberKnightsLocation, item_type=EmberKnightsItem
            )
        else:
            regions["Architect's Realm"].add_event(
                "Arch Praxis Victory", "Victory", location_type=EmberKnightsLocation, item_type=EmberKnightsItem
            )

    def set_rules(self) -> None:
        self.set_completion_rule(Has("Victory"))

    def create_items(self) -> None:
        permanent_items = [skill.name for skill in CATALOG.skills]
        permanent_items.extend(
            skill.name for skill in CATALOG.standard_skills if skill.ability_id not in self.starting_standard_skill_ids
        )
        permanent_items.extend(
            weapon.name for weapon in CATALOG.weapons if weapon.weapon_id not in self.starting_weapon_ids
        )
        permanent_items.extend(orb.name for orb in CATALOG.relic_orbs)
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
        """Names for every filler slot. Slots are shared evenly between the enabled kinds: 50 Gold, Ash (unless its
        amount is 0) and traps (when enabled). Any remainder goes to 50 Gold, which is always enabled."""
        kinds: list[list[str]] = [["50 Gold"]]
        if int(self.options.ash_filler_amount) > 0:
            kinds.append([CATALOG.ash_item_name])
        if self.options.traps:
            kinds.append([trap.name for trap in CATALOG.trap_items])

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
            "starting_standard_skills",
            "ash_filler_amount",
            "traps",
        )
        slot_data["starting_weapon_ids"] = list(self.starting_weapon_ids)
        # Presence of this key tells the client the standard skills are gated by this seed. Seeds generated before
        # the option existed lack it, and the client then keeps every standard skill unlocked as in the base game.
        slot_data["starting_standard_skill_ids"] = list(self.starting_standard_skill_ids)
        slot_data["heat_reroll"] = "once" if self.options.heat_reroll == HeatReroll.option_once else "every_run"
        # Retired: orb checks are always sent at pickup now. The key is still written so older clients read a value.
        slot_data["relic_orb_checks"] = "on_pickup"
        # heat_mode is the single source of truth for how heat is chosen, including whether the curated tiers apply.
        if self.options.heat_mode == HeatMode.option_sequential:
            slot_data["heat_mode"] = "sequential"
        elif self.options.heat_mode == HeatMode.option_curated_heat_difficulty:
            slot_data["heat_mode"] = "curated_heat_difficulty"
        else:
            slot_data["heat_mode"] = "random_assortment"
        slot_data["goal"] = "arch_praxis" if self.options.goal == Goal.option_arch_praxis else "praxis"
        slot_data["mod_version"] = "1.1.0"
        return slot_data
