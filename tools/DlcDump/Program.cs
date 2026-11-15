using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace EmberKnights.DlcDump;

/// <summary>
/// Read-only, one-shot dumper for the "Wrath of the Architect" DLC definitions. It runs inside the game (as a BepInEx
/// plugin), writes <c>BepInEx/dlc-dump.json</c> once the game's definition tables are loaded, then quits the game.
/// It changes nothing in the game or in any save file. The output is what the Archipelago catalog needs to add DLC
/// checks without guessing ids: DLC areas with their miniboss and boss, every enemy flagged as a DLC boss or miniboss,
/// DLC skills and where their tablets drop, DLC weapons, DLC weapon mods and DLC relics.
///
/// It does not prove the DLC is licensed on this account; it reports what the game's own tables contain, plus what
/// <c>HasDLC</c> answers if that can be called.
/// </summary>
[BepInPlugin("com.emberknights.dlcdump", "Ember Knights DLC Dump", "0.1.0")]
public sealed class DumpPlugin : BasePlugin
{
    public static DumpPlugin Instance { get; private set; } = null!;

    public override void Load()
    {
        Instance = this;
        AddComponent<DumpBehaviour>();
        Log.LogInfo("DLC dump plugin loaded; it will write BepInEx/dlc-dump.json once and quit the game.");
    }
}

public sealed class DumpBehaviour : MonoBehaviour
{
    private const float TimeoutSeconds = 120f;
    private bool finished;
    private float nextStatusLogAt;

    public DumpBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    private void Update()
    {
        if (finished)
            return;

        string status;
        var ready = false;
        try
        {
            var defs = GameCode.Defs;
            ready = defs?.enemyFile?.enemyDefs is not null && defs.areaFile?.Defs is not null &&
                    defs.abilitiesFile?.AbilityDefs is not null && defs.weaponDefs?.WeaponDefs is not null &&
                    defs.weaponModsFile?.Defs is not null && defs.relicFile?.RelicDefs is not null;
            status = $"defs={(defs is not null)}, tablesReady={ready}";
        }
        catch (Exception exception)
        {
            status = $"reading definitions threw: {exception.Message}";
        }

        if (Time.realtimeSinceStartup >= nextStatusLogAt)
        {
            nextStatusLogAt = Time.realtimeSinceStartup + 10f;
            DumpPlugin.Instance.Log.LogInfo($"t={Time.realtimeSinceStartup:F0}s: {status}");
        }

        if (!ready)
        {
            if (Time.realtimeSinceStartup > TimeoutSeconds)
                Finish(new { error = "The definition tables were not available before the timeout.", status });
            return;
        }

        try
        {
            Finish(Build());
        }
        catch (Exception exception)
        {
            Finish(new { error = exception.ToString() });
        }
    }

    private static object Build()
    {
        var defs = GameCode.Defs!;

        var areas = new List<object>();
        foreach (var area in defs.areaFile.Defs)
        {
            if (area is null)
                continue;
            var roomIds = new List<int>();
            var rooms = new List<object>();
            if (area.AllRoomDefs is not null)
            {
                for (var i = 0; i < area.AllRoomDefs.Count; i++)
                {
                    var room = area.AllRoomDefs[i];
                    if (room is null)
                        continue;
                    roomIds.Add(room.id);
                    rooms.Add(new { id = room.id, type = room.roomType.ToString(), size = room.roomSize.ToString(), teleporter = room.hasTeleporter, modEnergy = room.IsModEnergyRoom });
                }
            }

            var bossId = SafeInt(() => (int)area.BossId);
            areas.Add(new
            {
                id = area.id,
                niceName = area.niceName,
                areaName = area.areaName.ToString(),
                areaNameValue = (int)area.areaName,
                enabled = area.enabled,
                dlcContent = area.DLCContent,
                minibossEnemyId = (int)area.MinibossId,
                minibossEnemy = area.MinibossId.ToString(),
                bossEnemyId = bossId,
                bossEnemy = SafeString(() => area.BossId.ToString()),
                roomCount = roomIds.Count,
                rooms,
            });
        }

        var enemies = new List<object>();
        foreach (var enemy in defs.enemyFile.enemyDefs)
        {
            if (enemy is null)
                continue;
            // Everything flagged DLC, plus every boss or miniboss, so base and DLC bosses can be told apart.
            if (!enemy.DLCContent && !enemy.IsBoss && !enemy.IsMiniBoss)
                continue;
            enemies.Add(new
            {
                id = enemy.id,
                niceName = enemy.niceName,
                enabled = enemy.enabled,
                dlcContent = enemy.DLCContent,
                isBoss = enemy.IsBoss,
                isMiniBoss = enemy.IsMiniBoss,
                area = enemy.Area.ToString(),
                areaValue = (int)enemy.Area,
            });
        }

        var abilities = new List<object>();
        foreach (var ability in defs.abilitiesFile.AbilityDefs)
        {
            if (ability is null || !ability.DLCContent)
                continue;
            abilities.Add(new
            {
                id = ability.id,
                niceName = ability.niceName,
                enabled = ability.enabled,
                unlockedByDefault = ability.UnlockedByDefault,
                showInSelector = ability.ShowInSelector,
                tabletArea = ability.SkillTabletDropsInArea.ToString(),
                tabletAreaValue = (int)ability.SkillTabletDropsInArea,
                goldCost = ability.GoldCostBase,
                emberCost = ability.EmberCost,
            });
        }

        var weapons = new List<object>();
        foreach (var weapon in defs.weaponDefs.WeaponDefs)
        {
            if (weapon is null)
                continue;
            weapons.Add(new { id = weapon.id, niceName = weapon.niceName, enabled = weapon.enabled, dlcContent = weapon.DLCContent });
        }

        var mods = new List<object>();
        foreach (var mod in defs.weaponModsFile.Defs)
        {
            if (mod is null || !mod.DLCContent)
                continue;
            mods.Add(new
            {
                id = mod.id,
                niceName = mod.niceName,
                enabled = mod.enabled,
                weaponType = mod.WeaponType.ToString(),
                weaponTypeValue = (int)mod.WeaponType,
                isDefault = mod.IsDefault,
                unlockCost = mod.UnlockCost,
            });
        }

        var relics = new List<object>();
        var orderByRelic = new Dictionary<int, int>();
        var dlcOrder = defs.relicFile.DLCRelicsByUnlockOrder;
        if (dlcOrder is not null)
        {
            for (var i = 0; i < dlcOrder.Count; i++)
            {
                if (dlcOrder[i] is not null)
                    orderByRelic[dlcOrder[i].id] = i + 1;
            }
        }
        foreach (var relic in defs.relicFile.RelicDefs)
        {
            if (relic is null || !relic.DLCContent)
                continue;
            relics.Add(new
            {
                id = relic.id,
                niceName = relic.niceName,
                enabled = relic.enabled,
                rarity = relic.Rarity.ToString(),
                isCursed = relic.IsCursedRelic,
                dlcOrbOrder = orderByRelic.TryGetValue(relic.id, out var order) ? order : 0,
            });
        }

        return new
        {
            note = "Read-only dump of DLC definitions from the running game.",
            hasDlcSelf = SafeBool(() => GameCode.Game.HasDLC(eDLCCheck.Self)),
            hasDlcAuthority = SafeBool(() => GameCode.Game.HasDLC(eDLCCheck.Authority)),
            areas,
            enemies,
            abilities,
            weapons,
            weaponMods = mods,
            relics,
        };
    }

    private static int SafeInt(Func<int> read)
    {
        try { return read(); } catch { return -1; }
    }

    private static string SafeString(Func<string> read)
    {
        try { return read(); } catch { return ""; }
    }

    private static bool? SafeBool(Func<bool> read)
    {
        try { return read(); } catch { return null; }
    }

    private void Finish(object result)
    {
        finished = true;
        try
        {
            var path = Path.Combine(Paths.BepInExRootPath, "dlc-dump.json");
            File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            DumpPlugin.Instance.Log.LogInfo($"DLC dump written to {path}.");
        }
        catch (Exception exception)
        {
            DumpPlugin.Instance.Log.LogError($"Could not write the DLC dump: {exception}");
        }
        finally
        {
            Application.Quit();
        }
    }
}
