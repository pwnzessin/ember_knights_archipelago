"""Summarize heat probe snapshots: selected ranks versus the totals the game itself reports."""

import json
from pathlib import Path

build = Path(__file__).resolve().parent.parent / "build"
names = {
    1: "Hulking", 2: "Haste", 3: "Ruin", 4: "Onslaught", 5: "Horde", 6: "Malice", 7: "Pantheon",
    8: "Challenger", 9: "Doused", 10: "Fragile", 11: "Sealed", 12: "Inflation", 13: "Blitz",
    14: "PraxisEffects(Tyrant)", 15: "Aficionado", 16: "Shared Fate", 17: "Chaos", 18: "Trinity",
    19: "Discipline", 20: "Blind", 21: "Purge", 22: "Incompetence",
}

previous_total = None
for path in sorted(build.glob("heat-probe-*.json")):
    snapshot = json.loads(path.read_text(encoding="utf-8"))
    ranks = snapshot.get("world.GameMods.ModRanks", [])
    saved = snapshot.get("save.GameMods.ModRanks", [])
    active = {names.get(i, f"#{i}"): f"{value} (0b{value:b})" for i, value in enumerate(ranks) if value}
    total = snapshot.get("game.totalGameModifierOrbs")
    delta = "" if previous_total is None else f" (change {total - previous_total:+d})"
    print(
        f"#{snapshot['snapshot']:02d} total={total}{delta} tier ticks={snapshot.get('game.difficultyTier.NumTicks')} "
        f"doom={snapshot.get('game.isDoom')} trueEnding={snapshot.get('game.isTrueEndingEnabled')} "
        f"saveMatchesWorld={ranks == saved} ranks={active}"
    )
    previous_total = total
