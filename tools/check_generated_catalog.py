"""Regression check for the catalog generator refactor, and a readable heat table."""

import json
from pathlib import Path

tools = Path(__file__).resolve().parent
catalog = json.loads((tools.parent / "apworld" / "ember_knights" / "catalog.json").read_text(encoding="utf-8"))

before = json.loads((tools.parent / "build" / "relic-names-before.json").read_text(encoding="utf-8"))
after = [orb["name"] for orb in catalog["relicOrbs"]]
print("relic names unchanged by the refactor:", before == after, f"({len(after)} names)")
if before != after:
    for index, (old, new) in enumerate(zip(before, after), start=1):
        if old != new:
            print(f"  orb {index}: {old!r} -> {new!r}")

heat = catalog["heat"]
print("maxHeat", heat["maxHeat"])
for mod in heat["modifiers"]:
    note = "   UNSAFE: " + mod["unsafe"][:60] if "unsafe" in mod else ""
    print(f"  {mod['id']:2d} {mod['name']:13s} {mod['kind']:6s} {mod['costs']}{note}")
