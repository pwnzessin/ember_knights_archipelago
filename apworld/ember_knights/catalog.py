import json
import pkgutil
from dataclasses import dataclass
from typing import Optional


@dataclass(frozen=True)
class Skill:
    name: str
    ability_id: int
    location_name: str
    region: str


@dataclass(frozen=True)
class Weapon:
    name: str
    weapon_id: int


@dataclass(frozen=True)
class Filler:
    name: str
    kind: str
    amount: int


@dataclass(frozen=True)
class Encounter:
    name: str
    enemy_ids: tuple[int, ...]
    region: str
    variant: bool
    final_goal: Optional[str] = None

    @property
    def location_name(self) -> str:
        return f"{self.name} Defeated"


@dataclass(frozen=True)
class EmberTreeNode:
    name: str
    def_id: int
    stage: int
    required_coins: int

    @property
    def location_name(self) -> str:
        return f"Ember Tree: {self.name}"


@dataclass(frozen=True)
class WeaponMod:
    name: str
    def_id: int
    weapon_type: int

    @property
    def location_name(self) -> str:
        return self.name


@dataclass(frozen=True)
class RoomMilestone:
    world: str
    area_name: int
    room: int

    @property
    def location_name(self) -> str:
        return f"{self.world} Room {self.room} Reached"


@dataclass(frozen=True)
class RelicOrb:
    """The n-th relic orb the player collects, and the relic the game would unlock for it."""

    order: int
    def_id: int
    name: str

    @property
    def location_name(self) -> str:
        return f"Relic Orb {self.order:02d}"


@dataclass(frozen=True)
class HeatModifier:
    """One difficulty modifier from the game's heat screen."""

    id: int
    name: str
    kind: str
    costs: tuple[int, ...]
    unsafe: Optional[str] = None


class Catalog:
    def __init__(self) -> None:
        catalog_data = pkgutil.get_data(__name__, "catalog.json")
        if catalog_data is None:
            raise FileNotFoundError("Ember Knights catalog.json is missing from the world package.")
        raw = json.loads(catalog_data.decode("utf-8"))
        self.id_base: int = raw["idBase"]
        self.skills = [
            Skill(entry["name"], entry["abilityId"], entry["locationName"], entry["region"])
            for entry in raw["skills"]
        ]
        self.weapons = [Weapon(entry["name"], entry["weaponId"]) for entry in raw["weapons"]]
        self.weapons_by_id = {weapon.weapon_id: weapon for weapon in self.weapons}
        self.fillers = [Filler(**entry) for entry in raw["fillers"]]
        self.minibosses = [
            Encounter(entry["name"], tuple(entry["enemyIds"]), entry["region"], entry["variant"])
            for entry in raw["minibosses"]
        ]
        self.bosses = [
            Encounter(
                entry["name"], tuple(entry["enemyIds"]), entry["region"], entry["variant"], entry.get("finalGoal")
            )
            for entry in raw["bosses"]
        ]
        self.ember_tree = [
            EmberTreeNode(entry["name"], entry["defId"], entry["stage"], entry["requiredCoins"])
            for entry in raw["emberTree"]
        ]
        self.coin_bundles = [filler for filler in self.fillers if filler.kind == "coin"]
        self.trap_items = [filler for filler in self.fillers if filler.kind.startswith("trap_")]
        self.ash_item_name = next(filler.name for filler in self.fillers if filler.kind == "ash")
        self.weapon_mods = [
            WeaponMod(entry["name"], entry["defId"], entry["weaponType"]) for entry in raw["weaponMods"]
        ]
        self.room_milestones = [
            RoomMilestone(entry["world"], entry["areaName"], entry["room"]) for entry in raw["roomMilestones"]
        ]
        self.relic_orbs = [RelicOrb(entry["order"], entry["defId"], entry["name"]) for entry in raw["relicOrbs"]]
        heat = raw["heat"]
        self.max_heat: int = heat["maxHeat"]
        self.heat_modifiers = [
            HeatModifier(entry["id"], entry["name"], entry["kind"], tuple(entry["costs"]), entry.get("unsafe"))
            for entry in heat["modifiers"]
        ]
        self.heat_reduction_item_name = "Heat Reduction"

        self.item_name_to_id: dict[str, int] = {}
        self.location_name_to_id: dict[str, int] = {}
        for index, skill in enumerate(self.skills):
            self.item_name_to_id[skill.name] = self.skill_item_id(index)
            self.location_name_to_id[skill.location_name] = self.skill_location_id(index)
        for index, weapon in enumerate(self.weapons):
            self.item_name_to_id[weapon.name] = self.weapon_item_id(index)
        for index, filler in enumerate(self.fillers):
            self.item_name_to_id[filler.name] = self.filler_item_id(index)
        for index, encounter in enumerate(self.minibosses):
            self.location_name_to_id[encounter.location_name] = self.miniboss_location_id(index)
        for index, encounter in enumerate(self.bosses):
            self.location_name_to_id[encounter.location_name] = self.boss_location_id(index)
        for index, node in enumerate(self.ember_tree):
            self.location_name_to_id[node.location_name] = self.ember_tree_location_id(index)
        for index, mod in enumerate(self.weapon_mods):
            self.location_name_to_id[mod.location_name] = self.weapon_mod_location_id(index)
        for index, milestone in enumerate(self.room_milestones):
            self.location_name_to_id[milestone.location_name] = self.room_milestone_location_id(index)
        for index, orb in enumerate(self.relic_orbs):
            self.item_name_to_id[orb.name] = self.relic_item_id(index)
            self.location_name_to_id[orb.location_name] = self.relic_orb_location_id(index)
        self.item_name_to_id[self.heat_reduction_item_name] = self.heat_reduction_item_id()

    def skill_item_id(self, index: int) -> int:
        return self.id_base + 1 + index

    def skill_location_id(self, index: int) -> int:
        return self.id_base + 1000 + index

    def weapon_item_id(self, index: int) -> int:
        return self.id_base + 100 + index

    def filler_item_id(self, index: int) -> int:
        return self.id_base + 200 + index

    def miniboss_location_id(self, index: int) -> int:
        return self.id_base + 2000 + index

    def boss_location_id(self, index: int) -> int:
        return self.id_base + 3000 + index

    def ember_tree_location_id(self, index: int) -> int:
        return self.id_base + 4000 + index

    def weapon_mod_location_id(self, index: int) -> int:
        return self.id_base + 5000 + index

    def room_milestone_location_id(self, index: int) -> int:
        return self.id_base + 6000 + index

    def relic_orb_location_id(self, index: int) -> int:
        return self.id_base + 7000 + index

    def relic_item_id(self, index: int) -> int:
        return self.id_base + 300 + index

    def heat_reduction_item_id(self) -> int:
        return self.id_base + 400


CATALOG = Catalog()
