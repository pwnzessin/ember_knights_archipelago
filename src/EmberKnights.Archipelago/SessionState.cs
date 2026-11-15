using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace EmberKnights.Archipelago;

public sealed class SessionState
{
    public int Version { get; set; } = 1;
    public string Seed { get; set; } = "";
    public string SlotName { get; set; } = "";
    public int NextItemIndex { get; set; }

    /// <summary>How many relic orbs have been collected; the next orb is "Relic Orb (value + 1)".</summary>
    public int OrbsCollected { get; set; }

    /// <summary>How many received relics Esper's reveal has already shown, oldest first.</summary>
    public int RelicsRevealed { get; set; }

    /// <summary>
    /// True once the Ember Tree nodes and weapon mods that were already unlocked when this seed first connected have
    /// been written to <see cref="BaselineEmberTree"/> and <see cref="BaselineWeaponMods"/>. They are never reported as
    /// checks. A seed that ran before this existed leaves it false and is marked as having no baseline (see
    /// <see cref="BaselineSkipped"/>), so it keeps reporting every unlocked node as it always did.
    /// </summary>
    public bool BaselineTaken { get; set; }

    /// <summary>True for a seed that had already run when the baseline was introduced; it is never given one.</summary>
    public bool BaselineSkipped { get; set; }

    /// <summary>Ember Tree node ids that were already unlocked at first connect and are not checks.</summary>
    public List<int> BaselineEmberTree { get; set; } = new();

    /// <summary>Weapon mod ids that were already unlocked at first connect and are not checks.</summary>
    public List<int> BaselineWeaponMods { get; set; } = new();

    /// <summary>
    /// DLC orbs collected so far, counted on their own because the game hands out DLC orb relics from a separate list.
    /// Zero in a state file written before the DLC existed in the plugin, which is the right starting value.
    /// </summary>
    public int DlcOrbsCollected { get; set; }

    /// <summary>DLC orbs Esper has already identified (the DLC counterpart of <see cref="RelicsRevealed"/>).</summary>
    public int DlcRelicsRevealed { get; set; }

    /// <summary>Weapon mods that were already unlocked when this seed first connected with the DLC on.</summary>
    public List<int> BaselineDlcWeaponMods { get; set; } = new();

    /// <summary>True once the DLC weapon mod baseline has been recorded for this seed (see <see cref="BaselineDlcWeaponMods"/>).</summary>
    public bool DlcBaselineTaken { get; set; }

    public List<long> PendingChecks { get; set; } = new();
    public List<long> PendingFillers { get; set; } = new();
    public bool GoalPending { get; set; }
}

public sealed class SessionStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string stateDirectory;

    public SessionStateStore(string pluginDirectory)
    {
        stateDirectory = Path.Combine(pluginDirectory, "state");
    }

    public SessionState Load(string slotName)
    {
        var path = GetPath(slotName);
        if (!File.Exists(path))
            return new SessionState { SlotName = slotName };

        try
        {
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path), JsonOptions);
            if (state is null || state.Version != 1)
                throw new InvalidDataException("Unsupported Archipelago session-state format.");
            state.PendingChecks ??= new List<long>();
            state.PendingFillers ??= new List<long>();
            state.SlotName = slotName;
            return state;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Cannot read {path}. Fix or move the file before connecting; item grants were not applied.", exception);
        }
    }

    public void Save(SessionState state)
    {
        Directory.CreateDirectory(stateDirectory);
        var path = GetPath(state.SlotName);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Copy(temporaryPath, path, true);
        File.Delete(temporaryPath);
    }

    private string GetPath(string slotName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(slotName.Trim().ToLowerInvariant()));
        return Path.Combine(stateDirectory, $"{Convert.ToHexString(hash)[..16]}.json");
    }
}
