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
            });
        }

        return new
        {
            count = all.Count,
            relics = all,
            baseUnlockOrder = IdList(defs.RelicsByUnlockOrder),
            dlcUnlockOrder = defs.DLCRelicsByUnlockOrder is null ? new List<int>() : IdList(defs.DLCRelicsByUnlockOrder),
            cursed = defs.CursedRelics is null ? new List<int>() : IdList(defs.CursedRelics),
            enemyKillCounts = defs.RelicUnlockEnemyKillCounts is null ? new List<int>() : IntList(defs.RelicUnlockEnemyKillCounts),
            dlcEnemyKillCounts = defs.DLCRelicUnlockEnemyKillCounts is null ? new List<int>() : IntList(defs.DLCRelicUnlockEnemyKillCounts),
        };
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
