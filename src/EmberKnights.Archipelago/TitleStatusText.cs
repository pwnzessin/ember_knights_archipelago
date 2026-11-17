namespace EmberKnights.Archipelago;

/// <summary>
/// The single line of text the mod puts on the game's title screen: the mod name and version, and where the connection
/// stands. Pure formatting with no game dependencies so it can be unit tested. The server address is left out on purpose,
/// because the title screen is what ends up in screenshots and on streams.
/// </summary>
internal static class TitleStatusText
{
    /// <summary>Colours for the status word, as TextMeshPro rich-text hex values, matching the in-game overlay.</summary>
    private const string ConnectedColor = "#3DD68C";
    private const string WaitingColor = "#F2AB3B";
    private const string OfflineColor = "#8A93A6";

    /// <summary>
    /// Builds the title line. <paramref name="isConnected"/> means logged in to the server; <paramref name="isActive"/>
    /// means a connection was started (so a dropped link reads as reconnecting rather than offline).
    /// </summary>
    public static string Build(string version, bool isConnected, bool isActive, string? slotName)
    {
        var name = CleanSlot(slotName);
        var head = $"Archipelago Mod {version}";
        if (isConnected)
        {
            return name.Length > 0
                ? $"{head}  -  <color={ConnectedColor}>Connected</color> as {name}"
                : $"{head}  -  <color={ConnectedColor}>Connected</color>";
        }

        if (isActive)
            return $"{head}  -  <color={WaitingColor}>Reconnecting</color> (F6)";

        return name.Length > 0
            ? $"{head}  -  <color={OfflineColor}>Not connected</color> (F6 to connect as {name})"
            : $"{head}  -  <color={OfflineColor}>Not connected</color> (F6 to connect)";
    }

    /// <summary>
    /// A slot name is typed by the player and goes into rich text, so a stray '&lt;' could swallow the rest of the line.
    /// Strip the characters that open a tag and trim it to a length that fits one line.
    /// </summary>
    public static string CleanSlot(string? slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName))
            return "";
        var cleaned = slotName.Replace("<", "").Replace(">", "").Trim();
        const int MaxLength = 24;
        return cleaned.Length <= MaxLength ? cleaned : cleaned[..(MaxLength - 1)] + "…";
    }
}
