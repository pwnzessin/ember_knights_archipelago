using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace EmberKnights.RelicDump;

[BepInPlugin("com.emberknights.relicdump", "Ember Knights Relic Dump", "0.1.0")]
public sealed class DumpPlugin : BasePlugin
{
    public static DumpPlugin Instance { get; private set; } = null!;

    public override void Load()
    {
        Instance = this;
        AddComponent<DumpBehaviour>();
        Log.LogInfo("Relic dump plugin loaded; it will write the definitions once and quit the game.");
    }
}

public sealed class DumpBehaviour : MonoBehaviour
{
    private const float TimeoutSeconds = 90f;
    private bool finished;

    public DumpBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    private float nextStatusLogAt;
    private bool loggedFirstUpdate;

    private void Update()
    {
        if (finished)
            return;

        if (!loggedFirstUpdate)
        {
            loggedFirstUpdate = true;
            DumpPlugin.Instance.Log.LogInfo("Dump behaviour is updating.");
        }

        RelicDataFile? defs = null;
        string status;
        try
        {
            var manager = GameCode.Defs;
            defs = manager?.relicFile;
            status = $"manager={(manager is not null)}, relicFile={(defs is not null)}, " +
                     $"RelicDefs={(defs?.RelicDefs is not null)}, byUnlockOrder={(defs?.RelicsByUnlockOrder is not null)}";
        }
        catch (Exception exception)
        {
            status = $"reading definitions threw: {exception.Message}";
            defs = null;
        }

        if (Time.realtimeSinceStartup >= nextStatusLogAt)
        {
            nextStatusLogAt = Time.realtimeSinceStartup + 10f;
            DumpPlugin.Instance.Log.LogInfo($"t={Time.realtimeSinceStartup:F0}s: {status}");
        }

        if (defs?.RelicDefs is null || defs.RelicsByUnlockOrder is null)
        {
            if (Time.realtimeSinceStartup > TimeoutSeconds)
                Finish(new { error = "Relic definitions were not available before the timeout.", status });
            return;
        }

        try
        {
            Finish(Build(defs));
        }
        catch (Exception exception)
        {
            Finish(new { error = exception.ToString() });
        }
    }

    private static object Build(RelicDataFile defs)
    {
        var all = new List<Dictionary<string, object?>>();
        for (var i = 0; i < defs.RelicDefs.Count; i++)
        {
            var relic = defs.RelicDefs[i];
            if (relic is null)
                continue;
            all.Add(new Dictionary<string, object?>
            {
                ["id"] = relic.id,
                ["niceName"] = relic.niceName,
                ["enabled"] = relic.enabled,
                ["dlcContent"] = relic.DLCContent,
                ["rarity"] = (int)relic.Rarity,
                ["unlockedByDefault"] = relic.UnlockedByDefault,
                ["unlockOrder"] = relic.UnlockOrder,
                ["dlcUnlockOrder"] = relic.DLCUnlockOrder,
                ["isCursed"] = relic.IsCursedRelic,
                ["inNexusShop"] = relic.InNexusShop,
                ["maxOwned"] = relic.MaxOwned,
                ["compendiumSortOrder"] = relic.CompendiumSortOrder,
                // What decides whether the relic selector can offer a relic, asked of the game itself so nothing is
                // guessed from how the lists are stored. Pools are the game's own eRelicPoolType values.
                ["requiresWeaponType"] = (int)relic.RequiresWeaponType,
                ["categoryBitmask"] = relic.CategoryBitmask,
                ["requiredCategoryBitmask"] = relic.RequiredCategoryBitmask,
                ["checkAllPlayers"] = relic.CheckAllPlayers,
                ["maxGloballyOwned"] = relic.MaxGloballyOwned,
                ["maxPlayerOwned"] = relic.MaxPlayerOwned,
                ["pools"] = PoolAreas(relic),
                // The requirement is a count, not just a bit: a category plus how many owned items must carry it.
                ["requiredCategories"] = RequiredCategories(relic),
                ["categories"] = CategoryList(relic.Categories),
            });
        }

        return new
        {
            count = all.Count,
            relics = all,
            // How the game rolls a relic's rarity. Each table is indexed by rarity: Common 0, Rare 1, Epic 2, Legendary 3.
            // The relic selector uses NEW_relicRollChances; the other tables are for chests, wells and the like.
            newRelicChance01 = defs.NewRelicChance01,
            rollTables = new Dictionary<string, List<object>>
            {
                ["NEW_relicRollChances"] = RollTable(defs.NEW_relicRollChances),
                ["relicRollChances"] = RollTable(defs.relicRollChances),
                ["randomRelicRollChances"] = RollTable(defs.randomRelicRollChances),
                ["rareRelicRollChances"] = RollTable(defs.rareRelicRollChances),
                ["FortuneModRelicRollChances"] = RollTable(defs.FortuneModRelicRollChances),
                ["CursedRelic199RollChances"] = RollTable(defs.CursedRelic199RollChances),
            },
            relicsByRarity = RarityLists(defs.RelicsByRarity),
            baseUnlockOrder = IdList(defs.RelicsByUnlockOrder),
            dlcUnlockOrder = defs.DLCRelicsByUnlockOrder is null ? new List<int>() : IdList(defs.DLCRelicsByUnlockOrder),
            cursed = defs.CursedRelics is null ? new List<int>() : IdList(defs.CursedRelics),
            enemyKillCounts = defs.RelicUnlockEnemyKillCounts is null ? new List<int>() : IntList(defs.RelicUnlockEnemyKillCounts),
            dlcEnemyKillCounts = defs.DLCRelicUnlockEnemyKillCounts is null ? new List<int>() : IntList(defs.DLCRelicUnlockEnemyKillCounts),
        };
    }

    private static List<object> RequiredCategories(XRelicDef relic)
    {
        var rows = new List<object>();
        var required = relic.RequiredCategories;
        if (required is null)
            return rows;
        for (var i = 0; i < required.Length; i++)
            rows.Add(new { category = (int)required[i].Category, categoryName = required[i].Category.ToString(), count = required[i].Count });
        return rows;
    }

    private static List<string> CategoryList(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<eItemCategory>? categories)
    {
        var names = new List<string>();
        if (categories is null)
            return names;
        for (var i = 0; i < categories.Length; i++)
            names.Add(categories[i].ToString());
        return names;
    }

    private static List<object> RollTable(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<XRelicChestRarity>? table)
    {
        var rows = new List<object>();
        if (table is null)
            return rows;
        for (var i = 0; i < table.Length; i++)
        {
            var entry = table[i];
            rows.Add(entry is null
                ? new { rarity = i, baseChance = (float?)null, chanceGrowthPerLevel = (float?)null }
                : new { rarity = i, baseChance = (float?)entry.baseChance, chanceGrowthPerLevel = (float?)entry.chanceGrowthPerLevel });
        }
        return rows;
    }

    /// <summary>Which relic ids the game filed under each rarity, because the selector picks within a rarity.</summary>
    private static Dictionary<string, List<int>> RarityLists(
        Il2CppSystem.Collections.Generic.Dictionary<eRarity, Il2CppSystem.Collections.Generic.List<XRelicDef>>? byRarity)
    {
        var result = new Dictionary<string, List<int>>();
        if (byRarity is null)
            return result;
        foreach (var rarity in new[] { eRarity.Common, eRarity.Rare, eRarity.Epic, eRarity.Legendary })
        {
            var ids = new List<int>();
            try
            {
                if (byRarity.ContainsKey(rarity))
                {
                    var list = byRarity[rarity];
                    for (var i = 0; list is not null && i < list.Count; i++)
                        ids.Add(list[i].id);
                }
            }
            catch (Exception)
            {
                // A rarity the game does not file anything under simply has no relics.
            }
            result[rarity.ToString()] = ids;
        }
        return result;
    }

    /// <summary>The pools a relic can be drawn from: for each game pool type, the area numbers where IsInPool says yes.</summary>
    private static Dictionary<string, List<int>> PoolAreas(XRelicDef relic)
    {
        var result = new Dictionary<string, List<int>>();
        // The run areas: the five base worlds, the four DLC worlds and the Architect level.
        var areas = new[] { 1, 2, 3, 4, 5, 8, 9, 10, 11, 14 };
        foreach (var pool in new[]
                 {
                     eRelicPoolType.Common, eRelicPoolType.Shop, eRelicPoolType.RelicRoom,
                     eRelicPoolType.Offering, eRelicPoolType.NexusShop,
                 })
        {
            var inAreas = new List<int>();
            foreach (var area in areas)
            {
                try
                {
                    if (relic.IsInPool(pool, (eAreaName)area))
                        inAreas.Add(area);
                }
                catch (Exception)
                {
                    // A pool the game does not define for this relic simply has no areas.
                }
            }
            result[pool.ToString()] = inAreas;
        }

        var dropAreas = new List<int>();
        foreach (var area in areas)
        {
            try
            {
                if (relic.CanDropInArea((eAreaName)area))
                    dropAreas.Add(area);
            }
            catch (Exception)
            {
                // Same: no answer means not droppable there.
            }
        }
        result["CanDropInArea"] = dropAreas;
        return result;
    }

    private static List<int> IdList(Il2CppSystem.Collections.Generic.List<XRelicDef> relics)
    {
        var ids = new List<int>();
        for (var i = 0; i < relics.Count; i++)
            ids.Add(relics[i].id);
        return ids;
    }

    private static List<int> IntList(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> values)
    {
        var list = new List<int>();
        for (var i = 0; i < values.Length; i++)
            list.Add(values[i]);
        return list;
    }

    private void Finish(object result)
    {
        finished = true;
        try
        {
            // The launcher re-spawns the game, so an environment variable does not reliably arrive;
            // write to a fixed location inside the game folder's scratch area instead.
            var path = Path.Combine(Paths.GameRootPath, "ArchipelagoMod", "build", "relic-dump.json");
            File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            DumpPlugin.Instance.Log.LogInfo($"Relic dump written to {path}.");
        }
        catch (Exception exception)
        {
            DumpPlugin.Instance.Log.LogError($"Could not write the relic dump: {exception}");
        }
        finally
        {
            Application.Quit();
        }
    }
}
