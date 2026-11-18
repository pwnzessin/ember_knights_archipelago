# Ember Knights Archipelago

An unofficial [Archipelago](https://archipelago.gg) multiworld integration for **Ember Knights** (Doom Turtle / Twin Sails Interactive). It has two parts, and you need both:

| Part | What it is | Where it goes |
| --- | --- | --- |
| **Client plugin** (`EmberKnights.Archipelago-vX.Y.Z.zip`) | A BepInEx 6 (IL2CPP) plugin that connects the running game to an Archipelago server. | Your Ember Knights game folder. |
| **APWorld** (`ember_knights.apworld`) | The Archipelago world: items, locations, logic and YAML options. | Your Archipelago installation, to generate seeds. |

Both are on the [Releases page](../../releases). The APWorld is released separately under tags named `apworld-vX.Y.Z`, because it does not change in every plugin release. Use the newest of each.

> **Status: v1.6, experimental.** Single-player campaign. Co-op is not tested, and Boss Rush and Daily Dungeon are outside the tested scope. DLC support is new and was built from the game's own data but not yet played through (see [DLC support](#dlc-support)). See [Known issues](#known-issues).

> **AI disclosure:** this project was basically fully vibe-coded; I only reviewed and tested the results in the game and inside of Archipelago lobbies. It has only been playtested a few times. Expect bugs, and any reports about issues you find are greatly appreciated.

**Not affiliated.** This is a fan project. It is not affiliated with, endorsed by, or supported by Doom Turtle, Twin Sails Interactive, or Archipelago. "Ember Knights" and related names belong to their owners. You need your own copy of the game, and no game files are included.

## Install

You need Ember Knights on PC (Steam), and Archipelago **0.6.8 or newer** to generate a seed. The client mod and the APWorld are installed separately.

### 1. Install the client mod (Windows, Steam)

1. Download `EmberKnights.Archipelago-vX.Y.Z.zip` from the [Releases page](../../releases) and **extract the whole zip** into a folder (right-click, Extract All). Do not run anything from inside the zip window.
2. **Close Ember Knights**, then double-click **`Install.bat`**.
   It finds your game through Steam, installs BepInEx and the mod, and asks for your server address, port and slot name. Press Enter to skip any question; you can set them later. If Windows shows a blue "Windows protected your PC" box, click **More info**, then **Run anyway**.
3. **Start Ember Knights from Steam.** The first start is slow (a minute or two) because BepInEx prepares files for the game on your PC. Wait for the main menu.
4. Load a **single-player** save. If you gave your connection details, the mod connects by itself. Otherwise press **F6**.

That is the whole install. To update, close the game and run `Install.bat` from the newer zip again; your settings and progress are kept. To remove the mod, run `Uninstall.bat` (it keeps BepInEx and your saves), or `Install.bat -Uninstall -RemoveBepInEx` from a Command Prompt to remove BepInEx too.

What the installer changes: it adds BepInEx (`winhttp.dll`, `doorstop_config.ini`, and the `BepInEx` and `dotnet` folders) and `BepInEx/plugins/EmberKnights.Archipelago` to the game folder, and writes `BepInEx/config/com.emberknights.archipelago.cfg`. On a fresh install that config also gets the standard on-screen settings (the HUD log window on, scale 0.8, held for 10 seconds, 0.4 idle opacity; see [Plugin config](#plugin-config)). The installer only adds a setting that is not in the config yet, so an update never changes a value you already have. The BepInEx download is checked against a known SHA-256 before anything is installed. It never touches your save files. The script is plain text; open `Install-EmberKnightsArchipelago.ps1` in Notepad to read exactly what it does.

If the installer cannot find your game, run it from a Command Prompt with `Install.bat -GamePath "D:\path\to\EmberKnights"`. If the BepInEx download is blocked, download the zip it names yourself and add `-BepInExZip "C:\path\to\that.zip"`.

<details>
<summary>Manual install (if you already use BepInEx 6 IL2CPP, or are not on Windows/Steam)</summary>

1. Install **BepInEx 6 (Unity IL2CPP, 64-bit, `6.0.0-be.791` or newer)** into the game folder, the one containing `EmberKnights_64.exe`, so that `winhttp.dll` and a `BepInEx` folder sit next to it.
2. Start the game once and wait for the main menu, then close it. This generates the files BepInEx needs.
3. Copy the `BepInEx` folder from the mod zip into the game folder, merging folders. You should have `BepInEx/plugins/EmberKnights.Archipelago/EmberKnights.Archipelago.dll`.
4. Start the game once more, close it, then edit `BepInEx/config/com.emberknights.archipelago.cfg` (see [Plugin config](#plugin-config)).

</details>
### 2. Install the APWorld

1. Download `ember_knights.apworld` from the Releases page (the `apworld-vX.Y.Z` release).
2. Put it in the `custom_worlds` folder of your Archipelago installation, or double-click it while the Archipelago Launcher is open. If you already have an older `ember_knights.apworld`, delete it first.
3. Restart the Archipelago Launcher.

### 3. Generate a seed

1. Write a YAML file with `game: Ember Knights` (see [YAML options](#yaml-options)). In the Launcher, **Options Creator** can build one for you.
2. Generate the multiworld and host it, or upload it to archipelago.gg.
3. Note the **slot name** (the `name:` in your YAML), and the host and port of the room.

### 4. Connect in game

1. Open `BepInEx/config/com.emberknights.archipelago.cfg` and set `host`, `port` and `slot_name` (spelled exactly as in your YAML), plus `password` if the room has one. See [Plugin config](#plugin-config).
2. Start the game and load a **single-player** save.
3. Press **F6** to connect. **F7** disconnects and restores the base game's behavior.

Set `auto_connect = true` in the config if you want it to connect by itself a few seconds after the game starts.

Use a fresh save for a new seed. Every Ember Tree node sends its check at the moment you buy it, whatever the save already held, so nothing you buy can be missed. On the first connect of a new seed, every weapon mod that is already unlocked is recorded and is not sent as a check.

## What the mod does

- **Checks:** skill tablets, minibosses, bosses (with optional variants), Ember Tree upgrades, weapon mods (Ash upgrades), world-progress room milestones and relic orbs.
- **Items:** skills, relics, weapons, Heat Reduction, Archipelago Coins, Ash and traps.
- **Ember Tree:** upgrades cost Archipelago Coins, and normal run Ember no longer feeds the tree while connected.
- **Reverse Heat:** difficulty starts high and is lowered by Heat Reduction items (see the heat options below).
- **Relic orbs:** the check is sent the moment you take the orb, whether you pick it up yourself or leave it on the ground and let the game pull it to you when you leave the room. You still hand the orb to Esper, and her reveal shows what that orb's location really holds. If it is one of your relics you see that relic. If it is anything else, the overlay names the item and the player it is for.
- **Tooltips:** Ember Tree and Ash upgrades show an `AP:` line naming the item the check holds. On Ember Tree nodes it appears while the node can be bought, and stays after you buy it with a `(sent)` mark once its check has been sent.
- **Starting skills:** the seven standard skills and the ten tablet skills (with the DLC, its ten tablet skills too) form one pool of 17 (27 with the DLC). You start with a random number of them (see `starting_skills`), and every other skill is an item in the multiworld. The tablets stay checks either way, so a tablet sends its location whichever skills you started with. Seeds generated before this option existed keep working: they only ever gated the standard skills.
- **Starting relics:** the base game gives every new save 43 ordinary relics for free (Bone Plate, Meteor, Magic Tome and others), and the other 53 come from relic orbs. All of them form one pool (with the DLC, the ten DLC orb relics join it too). You start with a random number of them (see `starting_relics`), and every other relic is an item in the multiworld. The orb checks do not change, and the 43 formerly free relics add items only, no new locations. At least seven of your starting relics are always Common relics that need no category to appear, so the first relic selects have several relics to rotate through instead of repeating the same few. Seeds generated before this option existed keep those 43 relics unlocked.
- **Short relic selects:** a relic select (the "pick one of three" screen) rolls each card as Common about 84% of the time and only offers relics you could have, so a player who owns few relics can get one or two cards. While connected, a select that comes out short is topped up to three with relics you own that passed every one of the game's own checks. A select the game already filled is never touched. Turn it off with `fill_short_relic_selectors`.
- **First shop:** the first shop of a new save is scripted to always sell Bone Plate, Tainted Conduit and an Apple Pie. While connected, the two scripted relic slots are rolled normally from the relics you own instead. The free Apple Pie stays. Disconnecting (F7) restores the scripted shop.
- **DeathLink and traps:** optional. Traps are Enemy Ambush, Boss Ambush and Praxis Arena. The enemies of an Enemy Ambush Trap now appear spread out in a small grid instead of stacked on one spot, because nothing in the game collides and a stack died to a single area attack. With `trap_enemies_any_world` on, each trapped enemy is drawn from every world your seed includes instead of copying the room's own enemies.
- **Title screen:** a small line on the title screen says `Archipelago Mod <version>` and whether you are connected, and as which slot. It never shows the server address. Turn it off with `title_screen_label`.
- **Overlay:** a status box shows connection state, heat and coins, with the latest Archipelago messages. See [HUD log window](#hud-log-window) for the optional in-game version.

## YAML options

These go in your player YAML under `Ember Knights:`. Defaults are in brackets.

| Option | Values | What it does |
| --- | --- | --- |
| `goal` | `praxis` [default], `arch_praxis` | The boss that completes your world. Defeating Arch Praxis also completes a `praxis` goal, because at heat 15 or more the game only offers Arch Praxis. `arch_praxis` needs Arch Praxis itself. |
| `dlc` | on / off [default] | Include the Wrath of the Architect DLC: its four altered worlds and the Architect level, their minibosses and bosses, ten skill tablets, nineteen Hyper Gloves and Ember Buster weapon mods, ten more relic orbs and room milestones as checks, with the new skills, weapons and relics as items. **You must own the DLC.** See [DLC support](#dlc-support). |
| `boss_variants` | on [default] / off | Include checks for alternate minibosses and world bosses. |
| `start_heat` | 0-55 [25] | Starting difficulty in the game's modifier points. |
| `minimum_heat` | 0-55 [15] | Floor that Heat Reduction items cannot go below. 15 is required for the Arch Praxis fight. At heat 15 or more the normal Praxis fight never appears, so with the `praxis` goal the seed is finished by defeating Arch Praxis; generation logs a warning for that combination (the default options trigger it). Set it below 15 to be able to face the normal Praxis. |
| `heat_reduction_per_item` | 1-15 [1] | Heat points each Heat Reduction item removes. |
| `heat_mode` | `random_assortment` [default], `sequential`, `curated_heat_difficulty` | How the difficulty modifiers are chosen and reduced. `curated_heat_difficulty` sorts them into five tiers so low heat only ever holds the mildest modifiers. |
| `heat_reroll` | `every_run` [default], `once` | Whether the random assortment is rolled again each run or once per seed. |
| `starting_archipelago_coins` | 1-25 [1] | Coins you start with. At least 1, so the first Ember Tree node can be bought. |
| `extra_starting_weapons` | 0-6 [1] | Random extra base-game weapons unlocked from the start, besides the Ember Blade. |
| `starting_skills` | 3-17 [3] | How many skills you start with, picked at random from one pool of every skill (the 7 standard skills, the 10 tablet skills, and the 10 DLC tablet skills when `dlc` is on). Every other skill becomes an item. The tablets stay checks. **Replaces `starting_standard_skills`.** |
| `starting_relics` | 10-43 [10] | How many relics you start with, picked at random from one pool of every relic (the 43 the base game gives away, the 53 orb relics, and the 10 DLC orb relics when `dlc` is on). Every other relic becomes an item. At least seven of them are always Common relics that need nothing else to appear. |
| `ash_filler_amount` | 0-50 [5] | Ash each Ash item gives. 50 Gold is no longer placed as filler, so Ash fills the filler slots. If this is 0 and `traps` is off, filler still falls back to Ash items. |
| `traps` | on / off | Split the filler slots between Ash and trap items. |
| `trap_enemies_any_world` | on / off [off] | Let the Enemy Ambush Trap draw its enemies from every world your seed includes (the five base worlds, plus the four altered worlds and the Architect level with `dlc` on), not only the world you are in. Bosses, minibosses, hatcheries and props are never drawn, and no DLC enemy is drawn without `dlc`. Only matters with `traps` on. |
| `death_link` | on / off | Send and receive DeathLinks. |

The seven standard skills are Inferno, Bomb, Missile, Chakram, Frost Wall, Furious Charge and Cloak and Dagger. A tablet skill that is not one of your starting skills is an item, and the tablet in the world is still a check either way.

**Upgrading from 1.5 or older:** `starting_standard_skills` no longer exists. A YAML that still sets it generates with a warning for the unknown option and uses the default of 3 starting skills, so rename it to `starting_skills`. The number now counts every skill (up to 17), not only the standard ones, so 7 no longer means "all the standard skills".

`starting_relics` replaces the old "always free" relics with items, so the number of items grows while the number of locations stays the same. The extra room comes from dropping 50 Gold, which is why filler is now Ash (and traps if enabled). The lowest setting (10) puts the most relics into the multiworld.

Options that older versions had and that no longer exist (`relic_orb_checks`, and the `random_assortment_late_blitz` heat mode) were removed in 1.2.0. A YAML that still sets them generates with a warning for the unknown option, or fails for the removed heat mode name, so delete or rename them.

## Plugin config

The file is `BepInEx/config/com.emberknights.archipelago.cfg`. BepInEx creates it on the first start and writes a comment above every setting.

| Section | Setting | Default | What it does |
| --- | --- | --- | --- |
| `Connection` | `host` | `archipelago.gg` | Server hostname or IP. |
| `Connection` | `port` | `38281` | Server port. |
| `Connection` | `slot_name` | `Player1` | Your slot name from the YAML. |
| `Connection` | `password` | empty | Room password, if any. |
| `Connection` | `auto_connect` | `false` | Connect shortly after the game starts. |
| `Gameplay` | `death_link` | `false` | DeathLink setting used only if the seed does not carry one. |
| `Gameplay` | `starting_archipelago_coins` | `1` | Fallback used only if the seed does not carry one. |
| `Gameplay` | `extra_starting_weapons` | `1` | Fallback used only if the seed does not list starting weapons. |
| `Gameplay` | `heat_mode` | `random_assortment` | Fallback used only if the seed does not carry one. |
| `Gameplay` | `heat_reroll` | `every_run` | Fallback used only if the seed does not carry one. |
| `Gameplay` | `fill_short_relic_selectors` | `true` | Top a relic select that came out with fewer than three cards up to three, using relics you own that passed every game check. |
| `Visuals` | `archipelago_coin_icon` | `true` | Replace the Ember icon with the Archipelago coin while connected. |
| `Visuals` | `title_screen_label` | `true` | Show the `Archipelago Mod` line and connection state on the title screen. Never shows the server address. |
| `Visuals` | `hud_log_window` | `false` | Experimental in-game log window (see below). |
| `Visuals` | `hud_log_scale` | `0.7` | Size of that window, 0.4 to 1.5. |
| `Visuals` | `hud_log_hold_seconds` | `6` | How long it stays in focus after a new message, 1 to 60. |
| `Visuals` | `hud_log_idle_opacity` | `0` | How visible it stays between messages. 0 hides it, 0.3 leaves it faint. |
| `Diagnostics` | `relic_diagnostics` | `true` | Log relic orb activity and the first few relic selects, with what every game check said about each relic you own, to `LogOutput.log` (`[RelicDiag]` lines). |
| `Diagnostics` | `praxis_diagnostics` | `true` | Log what happens after a Praxis Arena Trap (`[PraxisDiag]` lines). |

The `Gameplay` settings are fallbacks for seeds that do not carry the value. For a current seed the YAML decides, and these have no effect.

The defaults in the table are the plugin's own. The installer starts a new config with a different on-screen look: `hud_log_window = true`, `hud_log_scale = 0.8`, `hud_log_hold_seconds = 10` and `hud_log_idle_opacity = 0.4` (the coin icon and title line are on either way). It writes a value only when that setting is missing from the config, so reinstalling or updating never overwrites a setting you have changed. A manual install, which skips the installer, gets the plugin's own defaults.

If you upgrade from an older version, BepInEx leaves settings it no longer knows about in the file. They are harmless, and you can delete them.

## DLC support

Set `dlc: true` in your YAML to include **Wrath of the Architect**. It is off by default, and a seed made with it off is identical to one from before the DLC was supported. You must own the DLC, and the APWorld and the client mod both need to be 1.4.0 or newer.

With it on, the seed adds these checks:

| Checks | Count |
| --- | --- |
| Minibosses (Geode, Fire Dancer, Daimyo Ghost, Fly Hatcher) | 4 |
| Bosses (Oukranos, Lady Inferna, The Kasumi Clan, Terrorbyte, The Architect) and The Architect Reborn as a variant | 6 |
| Skill tablets (two per DLC world and two in the Architect level) | 10 |
| Weapon mods for the Hyper Gloves and Ember Buster (the always-unlocked Ember Buster 1-1 is not a check) | 19 |
| Relic orbs (numbered Relic Orb 54 to 63, after the base orbs) | 10 |
| Room milestones in the four altered worlds (rooms 1, 3, 5 and 7) and the Architect level (rooms 1 and 3) | 18 |

and these items: the ten DLC skills, the Hyper Gloves and the Ember Buster, and the ten DLC orb relics. The goal stays Praxis or Arch Praxis; the Architect is a check, not a goal. With `boss_variants` off, The Architect Reborn is left out.

**How the logic works:** the DLC worlds are an alternate path you choose when starting a run, so they form their own chain (Mystic Forest, Inferno Bastille, Feudal Village, Utopic Labs, then the Architect level). The first DLC world opens once Steadfast Citadel is cleared, because the game unlocks the DLC after Evee and Praxis in that world, and each later DLC world needs the previous one cleared. The base worlds keep their own chain. DLC weapon mods need the matching DLC weapon.

**What was checked and what was not.** The DLC data (worlds, enemies, skills, weapons, mods and relics) was read from a running copy of the game that owns the DLC, so the ids and counts come from the game. Unit tests and real seed generation cover the world, and the client mod was started once with the new build. Not verified: playing through any DLC world with a DLC seed. In particular these are untested guesses:

- the order of the DLC worlds (taken from the game's area numbers and an achievement guide),
- which Architect fight drops which of the two Architect skill tablets (the two tablets are named after their skills),
- the room milestone depths (copied from the base worlds' pattern),
- that DLC orbs carry their own relic ids (211 to 220) when picked up, which the mod relies on to tell them from base orbs.

A seed without `dlc` never touches the Hyper Gloves, the Ember Buster, DLC skills or DLC relics in your save.

## HUD log window

Set `hud_log_window = true` to draw the Archipelago log on the game's own HUD instead of the plain box. It fades in when a message arrives and fades out a few seconds later. It is **experimental and off by default**.

- It is drawn locally on your own screen and sends nothing over the game's network, so it does not affect co-op.
- **Other players only see it if they run the mod too.** The game has no chat channel to carry it, and this plugin does not add one.
- If the window cannot be drawn or is not fully on screen, the original box stays.
- If it is too large or small, change `hud_log_scale`.

## Known issues

- **Ash from an item:** your Ash total updates (you can see it when buying), but the Ash number in the top-left HUD does not until the game refreshes it.
- **Seeds that ran before this change:** a seed that had already received items before the unlock baseline existed keeps reporting every unlocked weapon mod. A new seed records the weapon mods that are already unlocked on its first connect and never reports those as checks. Ember Tree nodes are not judged from a baseline any more: each one is sent when you buy it, so a node bought before the mod first looked at the save, or one the game unlocks for free, can no longer be missed.
- **Starting skills:** gated standard skills in the Nexus shop and the in-run skill selector are untested.
- **Starting relics:** the first relic select was checked in a real run and only offered starting relics. Relics sold in shops other than the first one rely on the same unlock check, but were not looked at in play.
- **First shop:** the change that rolls the two scripted relic slots normally is new and was not yet checked in a real run. If Bone Plate or Tainted Conduit still show up in your first shop, please send `BepInEx/LogOutput.log`.
- **Praxis Arena Trap:** the Slow Hands, Laser Hands and Skill Disabler effects were made to work in 1.6.4 and were tried in the Dark Forest only. Other worlds' rooms were not looked at, so an effect may still look wrong in a room layout that was not tried.
- Co-op and online multiplayer are not tested.
- Boss Rush and Daily Dungeon are not covered.
- **DLC support has not been played through.** See [DLC support](#dlc-support) for exactly what was and was not checked.
- Seeds from an older APWorld do not have the newer options. Generate a new seed after updating.

## Troubleshooting

- **Nothing happens when I press F6.** Check that `BepInEx/plugins/EmberKnights.Archipelago/EmberKnights.Archipelago.dll` exists and that `BepInEx/LogOutput.log` contains `Ember Knights Archipelago ... loaded`. If it does not, BepInEx is not loading plugins: start the game once with BepInEx installed and wait for the menu.
- **It will not connect.** Check `host`, `port` and `slot_name` in the config. The slot name must match your YAML exactly. The overlay shows the reason on connect failure.
- **Wrong or missing items.** Check that the APWorld you generated with and the plugin are both the newest versions, and generate a new seed.
- **Game or BepInEx errors.** Send `BepInEx/LogOutput.log`. It is the first thing to look at.
- Your progress is stored in `BepInEx/plugins/EmberKnights.Archipelago/state/`, one small file per multiworld session. A session is identified the way the Archipelago reference client identifies it: the room's seed name, your team and your slot name. Using the same slot name in a different room starts a fresh session and leaves the first room's progress alone, and returning to the first room picks it up again. The host and port are not part of that identity, so a room that moves to a new port keeps its progress. Do not share or commit these files.

## Build from source

This is for developers. Players only need the Releases page.

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
.\build.ps1 -Install   # also copy the plugin into the game folder
.\test.ps1             # Archipelago world tests + packaged .apworld load test
dotnet test tools\ClientTests   # pure-logic client unit tests
```

`build.ps1 -Install` treats the **parent directory of this repository** as the game folder. To use `-Install`, clone the repo directly inside the game folder (for example `...\EmberKnights\ArchipelagoMod`), or edit `$projectRoot` in the script. Without `-Install`, the build only writes to `build/`.

`apworld/ember_knights/catalog.json` is committed. It contains only IDs and display names (no descriptions or localized prose). The scripts that produced its progression sections read the game's asset files directly and are not included; to update it after a game patch, use `tools/RelicDump` (an in-game BepInEx dumper) and edit `catalog.json` by hand or with your own extraction.

### What this repository does not contain

No game code, game assets, or data extracted from the game: no game binaries, no recovered or decompiled assemblies, no extracted ScriptableObject dumps or localization text, and no BepInEx or Archipelago distribution files. The plugin compiles against assemblies that BepInEx generates from **your own installed copy** of the game. The only artwork shipped is `Assets/archipelago_coin_32.png`, an Archipelago-style coin icon.

### Layout

```
apworld/ember_knights/        Archipelago world + tests
src/EmberKnights.Archipelago/ BepInEx client plugin
installer/                    Install.bat + PowerShell installer, uninstaller, packaging and installer tests
tools/ClientTests/            xUnit tests for pure logic (no game needed)
tools/RelicDump/              in-game relic definition dumper (needs the game)
tools/HeatProbe/              in-game read-only difficulty snapshot (F9)
tools/IntegrationHarness/     in-game integration test harness
tools/*.py                    packaging / verification helpers
build.ps1, test.ps1           build and test entry points
```

## License

No license has been chosen yet. Until one is added, all rights are reserved by the author. Add a `LICENSE` file before accepting contributions.
