from dataclasses import dataclass

from Options import Choice, DefaultOnToggle, PerGameCommonOptions, Range, Toggle


class Goal(Choice):
    """Select the final boss that completes this world."""

    display_name = "Goal"
    option_praxis = 0
    option_arch_praxis = 1
    default = 0


class Dlc(Toggle):
    """Include the Wrath of the Architect DLC.

    Adds the four altered worlds (Mystic Forest, Inferno Bastille, Feudal Village, Utopic Labs) and the Architect level:
    their minibosses and bosses, ten skill tablets, thirteen to nineteen weapon mods for the Hyper Gloves and the Ember
    Buster, ten more relic orbs, and room milestones, as checks, with the new skills, weapons and relics as items.

    You must own the DLC. The DLC worlds are an alternate path you choose at the start of a run, so the base worlds and
    the DLC worlds can be played in any order. Off by default.
    """

    display_name = "Wrath of the Architect DLC"


class BossVariants(DefaultOnToggle):
    """Include checks for alternate minibosses and world bosses."""

    display_name = "Boss Variants"


class StartHeat(Range):
    """Starting difficulty ("heat") in the game's own modifier points, from 0 to 55.

    The mod controls the modifier ranks directly; finding Heat Reduction items in the multiworld
    lowers the active heat down toward Minimum Heat.
    """

    display_name = "Start Heat"
    range_start = 0
    range_end = 55
    default = 25


class MinimumHeat(Range):
    """Floor that Heat Reduction items cannot reduce the active heat below.

    15 is the Doom difficulty threshold required for the Arch Praxis fight. If Start Heat is set
    lower than Minimum Heat, the active heat stays at Start Heat.
    """

    display_name = "Minimum Heat"
    range_start = 0
    range_end = 55
    default = 15


class HeatReductionPerItem(Range):
    """How many heat points each Heat Reduction item removes, down to Minimum Heat."""

    display_name = "Heat Reduction Per Item"
    range_start = 1
    range_end = 15
    default = 1


class HeatMode(Choice):
    """How difficulty modifiers ("heat") are chosen at the start of a run and reduced by checks.

    - random_assortment: Chooses a random assortment of modifiers at the start of the run and reduces them as Heat Reduction items are found.
    - sequential: Fills the gentler stat/spawn modifiers first in a fixed order and reduces them in reverse order.
    - curated_heat_difficulty: Random assortment that is sorted into five difficulty tiers. The gentle tiers are
      always used up before a harsher one starts, so low heat only ever contains the mildest modifiers.
        Tier 1: Pantheon.
        Tier 2: Malice.
        Tier 3: Hulking, Haste, Ruin, Onslaught and Horde (levels 1-2), Challenger, Inflation (1), Tyrant.
        Tier 4: Hulking, Haste, Ruin, Onslaught and Horde (levels 3-4), Aficionado, Sealed, Inflation (2).
        Tier 5: Doused, Fragile, Blitz.
      Inside a tier the order is random, and the levels of one modifier always fill in from the lowest.
    """

    display_name = "Heat Mode"
    option_random_assortment = 0
    option_sequential = 1
    option_curated_heat_difficulty = 2

    default = 0


class HeatReroll(Choice):
    """When the random assortment of heat modifiers is chosen.

    - every_run: Rolls a new random assortment each time you start a new run (default).
    - once: Rolls the assortment once for the whole seed and never again. Heat Reduction items only
      remove points from that same fixed assortment.
    Has no effect when Heat Mode is sequential, which is always fixed.
    """

    display_name = "Heat Reroll"
    option_every_run = 0
    option_once = 1
    default = 0



class StartingArchipelagoCoins(Range):
    """Number of Archipelago Coins the player starts with for unlocking the Ember Tree.

    Minimum 1 is required so the first node (Thick Skin) can be bought to activate the tree.
    """

    display_name = "Starting Archipelago Coins"
    range_start = 1
    range_end = 25
    default = 1


class ExtraStartingWeapons(Range):
    """Number of random extra base-game weapons unlocked from the start in addition to Ember Blade (Sword).

    Ember Blade (Sword) is always active. Any remaining base-game weapons not rolled as starting
    weapons are placed into the multiworld item pool.
    """

    display_name = "Extra Starting Weapons"
    range_start = 0
    range_end = 6
    default = 1


class StartingStandardSkills(Range):
    """How many of the seven standard skills you start with, chosen at random.

    The standard skills are the ones every run has in the base game: Inferno, Bomb, Missile, Chakram,
    Frost Wall, Furious Charge and Cloak and Dagger. You start with this many of them, picked at random
    for your seed. The rest are no longer free: they become items found in the multiworld, next to the
    ten tablet skills.

    Set to 7 to keep all standard skills from the start, as in the base game. The minimum is 3 so a run
    always has real skills to choose from.
    """

    display_name = "Starting Standard Skills"
    range_start = 3
    range_end = 7
    default = 3


class StartingRelics(Range):
    """How many relics you start with, chosen at random from one big pool of relics.

    In the base game 43 relics are unlocked for every new save (Bone Plate, Meteor, Discount Card, Magic Tome and
    others), and the other 53 come from relic orbs. This option puts all of them into one pool of 96 (106 with the
    DLC). You start with this many, picked at random for your seed. Every other relic stops being free and becomes an
    item found in the multiworld; the orbs still send their checks, and what Esper shows still matches the multiworld.

    The minimum is 10 so a run always has some relics to find in its first relic select. Set to 43 to start with as
    many relics as the base game gives you for free (though not necessarily the same ones).
    """

    display_name = "Starting Relics"
    range_start = 10
    range_end = 43
    default = 10


class AshFillerAmount(Range):
    """How much Ash (the weapon-mod and ability-upgrade resource) each Ash item gives.

    Ash items fill the filler slots (50 Gold is no longer placed), sharing them with traps when those are on. The
    game only accepts as much Ash as is still needed for unbought weapon mods and ability upgrades, so an Ash item
    can give less than this once most of them are bought. Set to 0 and the filler items still exist, but give no Ash.
    """

    display_name = "Ash Filler Amount"
    range_start = 0
    range_end = 50
    default = 5


class Traps(Toggle):
    """When enabled, remaining filler slots are split between 50 Gold and trap rewards
    (Enemy Ambush Trap, Boss Ambush Trap, and Praxis Arena Trap).
    """

    display_name = "Traps"


class DeathLink(Toggle):
    """Send and receive DeathLinks when the local player dies."""

    display_name = "DeathLink"


@dataclass
class EmberKnightsOptions(PerGameCommonOptions):
    goal: Goal
    dlc: Dlc
    boss_variants: BossVariants
    start_heat: StartHeat
    minimum_heat: MinimumHeat
    heat_reduction_per_item: HeatReductionPerItem
    heat_mode: HeatMode
    heat_reroll: HeatReroll
    starting_archipelago_coins: StartingArchipelagoCoins
    extra_starting_weapons: ExtraStartingWeapons
    starting_standard_skills: StartingStandardSkills
    starting_relics: StartingRelics
    ash_filler_amount: AshFillerAmount
    traps: Traps
    death_link: DeathLink
