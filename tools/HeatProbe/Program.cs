using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace EmberKnights.HeatProbe;

/// <summary>
/// Read-only observer. Press F9 in the game to write a snapshot of the difficulty ("heat") state:
/// the selected mod ranks and the totals the game itself computes from them. It never changes game state.
/// </summary>
[BepInPlugin("com.emberknights.heatprobe", "Ember Knights Heat Probe", "0.1.0")]
public sealed class ProbePlugin : BasePlugin
{
    public static ProbePlugin Instance { get; private set; } = null!;

    public override void Load()
    {
        Instance = this;
        AddComponent<ProbeBehaviour>();
        Log.LogInfo("Heat probe loaded. Press F9 in the game to write a read-only snapshot.");
    }
}

public sealed class ProbeBehaviour : MonoBehaviour
{
    private int snapshotNumber;

    public ProbeBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F9))
            return;

        snapshotNumber++;
        var report = new Dictionary<string, object?> { ["snapshot"] = snapshotNumber, ["time"] = DateTime.Now.ToString("s") };
        try
        {
            Capture(report);
        }
        catch (Exception exception)
        {
            report["fatal"] = exception.ToString();
        }
        Write(report);
    }

    private static void Try(Dictionary<string, object?> report, string name, Func<object?> read)
    {
        try
        {
            report[name] = read();
        }
        catch (Exception exception)
        {
            report[name] = "ERROR: " + exception.Message;
        }
    }

    private static List<int> Ranks(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>? ranks)
    {
        var list = new List<int>();
        if (ranks is null)
            return list;
        for (var i = 0; i < ranks.Length; i++)
            list.Add(ranks[i]);
        return list;
    }

    private static void Capture(Dictionary<string, object?> report)
    {
        var game = GameCode.Game;
        report["gameExists"] = game is not null;

        GameCode.XWorld? world = null;
        Try(report, "worldExists", () =>
        {
            world = game?.World;
            return world is not null;
        });

        if (world is not null)
        {
            Try(report, "area", () => world.currentArea?.areaName.ToString());
            Try(report, "world.GameMods.ModRanks", () => Ranks(world.GameMods?.ModRanks));
            Try(report, "world.GameMods.RunTimer", () => world.GameMods?.RunTimer);
            Try(report, "game.totalGameModifierOrbs", () => GameCode.GameUtils.GetTotalGameModifierOrbs(world));
            Try(report, "game.difficultyTier.NumTicks", () => GameCode.GameUtils.GetGameModDifficulty(world)?.NumTicks);
            Try(report, "game.difficultyTier.Token", () => GameCode.GameUtils.GetGameModDifficulty(world)?.Token);
            Try(report, "game.isDoom", () => GameCode.GameUtils.IsDoomDifficulty(GameCode.GameUtils.GetGameModDifficulty(world)));
            Try(report, "game.isDemon", () => GameCode.GameUtils.IsDemonDifficulty(GameCode.GameUtils.GetGameModDifficulty(world)));
            Try(report, "game.isTrueEndingEnabled", () => GameCode.GameUtils.IsTrueEndingEnabled(world));
            Try(report, "game.isTrueArchitect(total)", () =>
                GameCode.GameUtils.IsTrueArchitect(GameCode.GameUtils.GetTotalGameModifierOrbs(world)));
        }

        Try(report, "save.GameMods.ModRanks", () => Ranks(game?.LocalSaveSlot?.GameMods?.ModRanks));
        Try(report, "save.SelectedDifficulty", () => game?.LocalSaveSlot?.SelectedDifficulty.ToString());
    }

    private void Write(Dictionary<string, object?> report)
    {
        try
        {
            var path = Path.Combine(Paths.GameRootPath, "ArchipelagoMod", "build", $"heat-probe-{snapshotNumber:D2}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            ProbePlugin.Instance.Log.LogInfo($"Heat snapshot {snapshotNumber} written to {path}.");
        }
        catch (Exception exception)
        {
            ProbePlugin.Instance.Log.LogError($"Could not write the heat snapshot: {exception}");
        }
    }
}
