namespace EmberKnights.Archipelago;

/// <summary>
/// Decides whether the connection settings have been filled in, and what to tell a player who has not done so yet.
/// Pure logic with no game dependencies, so it can be unit tested.
/// </summary>
internal static class ConnectionSetup
{
    /// <summary>The slot name a fresh config file contains. It is a placeholder, never a real slot.</summary>
    public const string PlaceholderSlot = "Player1";

    /// <summary>
    /// True when the slot name is empty or still the placeholder. A real YAML can name its player anything, but a
    /// fresh install must not try to connect as "Player1", so the placeholder counts as "not set up yet".
    /// </summary>
    public static bool IsSlotMissing(string? slotName) =>
        string.IsNullOrWhiteSpace(slotName) ||
        string.Equals(slotName.Trim(), PlaceholderSlot, StringComparison.Ordinal);

    /// <summary>
    /// The one line shown when the player presses F6 (or auto-connect fires) before setting a slot name. It names the
    /// file to edit and the installer, so nobody has to go looking.
    /// </summary>
    public static string MissingSlotMessage(string? configPath)
    {
        var file = string.IsNullOrWhiteSpace(configPath) ? "BepInEx/config/com.emberknights.archipelago.cfg" : configPath!;
        return $"Set slot_name (and host/port) in {file}, or run Install.bat again, then press F6.";
    }
}
