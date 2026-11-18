using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace EmberKnights.Archipelago;

public sealed class SessionState
{
    public int Version { get; set; } = 1;
    public string Seed { get; set; } = "";
    public string SlotName { get; set; } = "";

    /// <summary>The team of the session, the third part of its identity. Zero for files written before teams were kept.</summary>
    public int Team { get; set; }
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

    /// <summary>
    /// Legacy Ember Tree baseline: every node that was merely unlocked at first connect, including the free Thick Skin
    /// and Slayer. Kept only so old state files load and can be migrated into <see cref="BaselineEmberTreeLevels"/>.
    /// </summary>
    public List<int> BaselineEmberTree { get; set; } = new();

    /// <summary>
    /// Ember Tree nodes that were already bought (unlocked with at least one level) at first connect and are not checks,
    /// with the level each had. A node the game gives for free sits at level 0 and is not listed, so buying its first
    /// level is reported like any other node.
    /// </summary>
    public Dictionary<int, int> BaselineEmberTreeLevels { get; set; } = new();

    /// <summary>True once <see cref="BaselineEmberTree"/> has been converted into <see cref="BaselineEmberTreeLevels"/>.</summary>
    public bool EmberTreeLevelBaselineTaken { get; set; }

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

    /// <summary>
    /// Loads the state of one multiworld session. A session is identified the way the Archipelago reference client
    /// identifies it: the server's <c>seed_name</c>, the team and the slot. Two rooms never share a file, so connecting
    /// to a different room with the same slot name leaves the first room's progress (item index, pending checks and
    /// unlock baseline) untouched, and returning to it restores that progress. The host and port are deliberately not
    /// part of the identity: a room that moves to another port is still the same session.
    /// </summary>
    public SessionState Load(string seed, int team, string slotName)
    {
        var path = GetPath(seed, team, slotName);
        if (!File.Exists(path))
        {
            var migrated = TryMigrateLegacy(seed, team, slotName, path);
            if (migrated is null)
                return new SessionState { SlotName = slotName, Seed = seed, Team = team };
            return migrated;
        }

        var state = Read(path);
        state.SlotName = slotName;
        state.Seed = seed;
        state.Team = team;
        return state;
    }

    public void Save(SessionState state)
    {
        Directory.CreateDirectory(stateDirectory);
        var path = GetPath(state.Seed, state.Team, state.SlotName);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Copy(temporaryPath, path, true);
        File.Delete(temporaryPath);
    }

    /// <summary>
    /// Earlier versions kept one file per slot name, whatever room it belonged to. When the file for this session does
    /// not exist yet, a legacy file for the same slot name is adopted only if it was written for this very seed, so a
    /// session that was already running keeps its progress. The legacy file is copied, not moved, and is never touched
    /// again; a file from another seed is ignored.
    /// </summary>
    private SessionState? TryMigrateLegacy(string seed, int team, string slotName, string newPath)
    {
        var legacyPath = GetLegacyPath(slotName);
        if (!File.Exists(legacyPath))
            return null;

        SessionState legacy;
        try
        {
            legacy = Read(legacyPath);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        if (!string.Equals(legacy.Seed, seed, StringComparison.Ordinal))
            return null;

        legacy.SlotName = slotName;
        legacy.Seed = seed;
        legacy.Team = team;
        Directory.CreateDirectory(stateDirectory);
        File.WriteAllText(newPath, JsonSerializer.Serialize(legacy, JsonOptions));
        return legacy;
    }

    private static SessionState Read(string path)
    {
        try
        {
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path), JsonOptions);
            if (state is null || state.Version != 1)
                throw new InvalidDataException("Unsupported Archipelago session-state format.");
            state.PendingChecks ??= new List<long>();
            state.PendingFillers ??= new List<long>();
            state.BaselineEmberTree ??= new List<int>();
            state.BaselineEmberTreeLevels ??= new Dictionary<int, int>();
            return state;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Cannot read {path}. Fix or move the file before connecting; item grants were not applied.", exception);
        }
    }

    /// <summary>The file name for a session: a hash of the seed, the team and the slot name.</summary>
    public string GetPath(string seed, int team, string slotName)
    {
        var key = $"{seed.Trim()}|{team}|{slotName.Trim().ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(stateDirectory, $"{Convert.ToHexString(hash)[..16]}.json");
    }

    /// <summary>The file name earlier versions used: a hash of the slot name alone.</summary>
    public string GetLegacyPath(string slotName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(slotName.Trim().ToLowerInvariant()));
        return Path.Combine(stateDirectory, $"{Convert.ToHexString(hash)[..16]}.json");
    }
}
