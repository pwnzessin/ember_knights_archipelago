namespace EmberKnights.Archipelago;

/// <summary>
/// Builds the "what does this upgrade send out" line shown in the Ember Tree and Ash (weapon mod) upgrade
/// panels. Pure text logic with no game dependencies, so it can be unit tested.
/// </summary>
internal static class UpgradeTooltip
{
    /// <summary>Marker that identifies a description this mod already extended, so it is never added twice.</summary>
    public const string Marker = "<color=#8fd3ff>AP:</color>";

    /// <summary>
    /// The line for one upgrade: <c>AP: &lt;item&gt;</c>, naming only what the multiworld holds at that check.
    /// <paramref name="locationName"/> is the Archipelago location ("Ember Tree: Regeneration") and is shown only as
    /// a fallback when the item has not been scouted (yet), so the line is never a bare "AP:".
    /// </summary>
    public static string Format(string locationName, ScoutedItem? item, bool alreadyChecked)
    {
        var text = item is null ? locationName : Describe(item);
        var state = alreadyChecked ? " <color=#8a93a6>(sent)</color>" : "";
        return $"{Marker} {text}{state}";
    }

    private static string Describe(ScoutedItem item) =>
        item.IsOwnPlayer ? item.ItemName : $"{item.ItemName} for {item.PlayerName}";

    /// <summary>
    /// True when an Ember Tree node can be bought right now: it is not unlocked yet and its stage is open.
    /// A stage opens once the total number of upgrades invested reaches the game's own threshold for that stage
    /// (<c>EmberTreeLevels[stage - 1]</c>, so stage 1 needs 0 upgrades, stage 2 needs 1, and so on). Coins are
    /// deliberately not part of this, so players can see what a node holds while saving up for it.
    /// </summary>
    /// <param name="alreadyUnlocked">The save already has this node unlocked.</param>
    /// <param name="unlockStage">The node's stage, from its game definition.</param>
    /// <param name="upgradesInvested">The save's total upgrades (unlocks plus levels).</param>
    /// <param name="stageThresholds">The game's <c>EmberTreeLevels</c>: upgrades required per stage, stage 1 first.</param>
    public static bool CanUnlockEmberTreeNode(
        bool alreadyUnlocked,
        int unlockStage,
        int upgradesInvested,
        IReadOnlyList<int>? stageThresholds)
    {
        if (alreadyUnlocked || unlockStage < 1 || stageThresholds is null)
            return false;
        // A stage the table does not describe is treated as closed rather than guessed open.
        if (unlockStage > stageThresholds.Count)
            return false;
        return upgradesInvested >= stageThresholds[unlockStage - 1];
    }

    /// <summary>Appends the line to the game's own description, keeping the original text intact.</summary>
    public static string Append(string? originalDescription, string line)
    {
        var original = originalDescription ?? "";
        if (original.Contains(Marker, StringComparison.Ordinal))
            return original;
        return original.Length == 0 ? line : $"{original}\n{line}";
    }
}

/// <summary>An item the multiworld holds at some location, from a silent scout.</summary>
internal sealed class ScoutedItem
{
    public string ItemName { get; init; } = "";
    public string PlayerName { get; init; } = "";
    public bool IsOwnPlayer { get; init; }
}
