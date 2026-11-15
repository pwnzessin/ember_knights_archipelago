# Ember Knights Archipelago

An unofficial [Archipelago](https://archipelago.gg) multiworld integration for **Ember Knights** (Doom Turtle / Twin Sails Interactive). It has two parts:

- **`apworld/ember_knights`** — the Archipelago world (item/location definitions, logic, options). Packaged as `ember_knights.apworld`.
- **`src/EmberKnights.Archipelago`** — a BepInEx 6 (IL2CPP) client plugin that connects the running game to an Archipelago server.

> **Status: v1.1, experimental.** Single-player campaign only. Co-op, Boss Rush, Daily Dungeon and DLC encounters are outside the tested scope.

## Not affiliated

This project is fan-made and is not affiliated with, endorsed by, or supported by Doom Turtle, Twin Sails Interactive, or Archipelago. "Ember Knights" and related names are the property of their owners. You need your own copy of the game.

## What this repository does not contain

No game code, game assets, or data extracted from the game is included. Specifically, this repo has:

- no game binaries and no recovered/decompiled assemblies,
- no extracted ScriptableObject dumps or localization text,
- no BepInEx or Archipelago distribution files.

The plugin compiles against assemblies that BepInEx generates from **your own installed copy** of the game (see below). The only artwork shipped is `Assets/archipelago_coin_32.png`, an Archipelago-style coin icon.

## What the mod does

- Items and locations: skill tablets, minibosses, bosses (with optional variants), Ember Tree upgrades, weapon mods, world-progress room milestones, and relic orbs are checks; skills, orb relics, heat reductions, Archipelago Coins and gold arrive as items.
- Ember Tree upgrades cost Archipelago Coins; normal run Ember no longer feeds the Ember Tree while connected.
- Reverse Heat: difficulty modifiers start high and are reduced by Heat Reduction items (YAML options `start_heat`, `minimum_heat`, `heat_reduction_per_item`, `heat_mode`, `heat_reroll`).
- Optional DeathLink, and trap items (enemy ambush, boss ambush, Praxis arena trap).
- Relic Orb checks are sent the moment you pick the orb up. You still hand the orb to Esper, and her reveal shows the real item that orb's location holds (the relic itself when it is one of yours, otherwise a message naming the item and the player).
- Ember Tree and Ash (weapon mod) upgrades show an `AP:` line in their description naming the item the check holds. On Ember Tree nodes it only appears while the node can be unlocked.
- Optional `starting_standard_skills` (3-7, default 3): you start with that many random standard skills (Inferno, Bomb, Missile, Chakram, Frost Wall, Furious Charge, Cloak and Dagger). The rest become items in the multiworld next to the ten tablet skills. Seeds generated before this option keep all standard skills unlocked.
- Compact status overlay. **F6** connects/reconnects, **F7** disconnects and restores vanilla behavior.

## Install (players)

1. Install BepInEx 6 (Unity IL2CPP build) into the game folder and start the game once so it generates its interop assemblies.
2. Build the plugin (below) and copy `EmberKnights.Archipelago.dll`, `Archipelago.MultiClient.Net.dll` and `Newtonsoft.Json.dll` into `BepInEx/plugins/EmberKnights.Archipelago/`.
3. Put `ember_knights.apworld` into your Archipelago `custom_worlds` folder and restart the launcher.
4. Generate a multiworld with an Ember Knights YAML.
5. Edit `BepInEx/config/com.emberknights.archipelago.cfg` (`host`, `port`, `slot_name`, optional `password`), then press **F6** in game.

## Build

`build.ps1` expects a local toolchain under `tools/` that is **not** in this repository (see `.gitignore`):

| Path | What it is |
| --- | --- |
| `tools/bepinex/` | BepInEx 6 IL2CPP distribution |
| `tools/interop/assemblies/` | Interop assemblies BepInEx generated from your own game install (`BepInEx/interop`) |
| `tools/python-ap/` | A Python venv for Archipelago |
| `tools/archipelago-0.6.8/Archipelago-0.6.8/` | Archipelago 0.6.8 source |
| `tools/dotnet/` (optional) | A .NET SDK; otherwise a system `dotnet` is used |

```powershell
.\build.ps1            # compile + package the plugin and .apworld into build/
.\build.ps1 -Install   # also copy the plugin into the game folder (run from inside the game directory)
.\test.ps1             # Archipelago world tests + packaged .apworld load test
dotnet test tools\ClientTests   # pure-logic client unit tests
```

`build.ps1 -Install` treats the **parent directory of this repository** as the game folder (`$projectRoot = Split-Path -Parent $PSScriptRoot`). To use `-Install`, clone the repo directly inside the game folder (for example `...\EmberKnights\ArchipelagoMod`), or edit `$projectRoot` in the script. Without `-Install`, the build only writes to `build/`.

## Regenerating game-derived catalog data

`apworld/ember_knights/catalog.json` is committed. It contains only IDs and display names (no descriptions or localized prose) for the checks and items. The scripts that originally produced the progression sections read the game's asset files directly and are therefore not included here; if you need to update the catalog after a game patch, use `tools/RelicDump` (an in-game BepInEx dumper) and edit `catalog.json` by hand or with your own extraction.

## Layout

```
apworld/ember_knights/        Archipelago world + tests
src/EmberKnights.Archipelago/ BepInEx client plugin
tools/ClientTests/            xUnit tests for pure logic (no game needed)
tools/RelicDump/              in-game relic definition dumper (needs the game)
tools/HeatProbe/              in-game read-only difficulty snapshot (F9)
tools/IntegrationHarness/     in-game integration test harness
tools/*.py                    packaging / verification helpers
build.ps1, test.ps1           build and test entry points
```

## Troubleshooting

Runtime logs: `BepInEx/LogOutput.log`. Per-slot session state is written to `BepInEx/plugins/EmberKnights.Archipelago/state/` (never commit it). Use a single-player campaign save; multiplayer is not supported.

## License

No license has been chosen yet. Until one is added, all rights are reserved by the author. Add a `LICENSE` file before accepting contributions.
