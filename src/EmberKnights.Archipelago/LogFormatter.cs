using System.Text;

namespace EmberKnights.Archipelago;

/// <summary>
/// Turns log rows into the TextMeshPro rich text the HUD window shows. Pure logic with no game dependencies.
///
/// The game's HUD font is not monospaced, so alignment cannot use spaces. Every row instead starts with a
/// <c>&lt;pos&gt;</c> tag that moves the text to a fixed horizontal position, in font units, so all message text and
/// every wrapped row start in the same column however wide the time digits are. User text is escaped, so a message can
/// never inject markup into the window.
/// </summary>
internal static class LogFormatter
{
    public const string TimeColor = "#7d8697";
    public const string InfoColor = "#e1e8f4";
    public const string ReceivedColor = "#8fe3a8";
    public const string SentColor = "#8fd3ff";
    public const string ChatColor = "#f0d58a";
    public const string WarningColor = "#ff9b8a";

    public static string ColorFor(LogKind kind) => kind switch
    {
        LogKind.Received => ReceivedColor,
        LogKind.Sent => SentColor,
        LogKind.Chat => ChatColor,
        LogKind.Warning => WarningColor,
        _ => InfoColor,
    };

    /// <summary>
    /// Builds the window text. <paramref name="textStart"/> is where message text begins, in font units (em/100 in
    /// TextMeshPro's <c>&lt;pos&gt;</c> tag, here as a percentage of the font size). A blank line is added between
    /// messages only when <paramref name="gapBetweenMessages"/> is true.
    /// </summary>
    public static string Build(IReadOnlyList<LogBuffer.Row> rows, float textStart = 3.2f, bool gapBetweenMessages = false)
    {
        var text = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (i > 0)
            {
                text.Append('\n');
                if (gapBetweenMessages && row.StartsMessage)
                    text.Append('\n');
            }

            if (row.Time is not null)
                text.Append("<color=").Append(TimeColor).Append('>').Append(Escape(row.Time)).Append("</color>");
            // Both the time-bearing row and the continuation rows jump to the same column before the text.
            text.Append("<pos=").Append(textStart.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)).Append("em>");
            text.Append("<color=").Append(ColorFor(row.Kind)).Append('>').Append(Escape(row.Text)).Append("</color>");
        }
        return text.ToString();
    }

    /// <summary>
    /// Makes user text safe to put inside rich text. TextMeshPro treats <c>&lt;</c> as the start of a tag, so a
    /// player name or chat line such as "&lt;color=red&gt;" could restyle or break the window. Each <c>&lt;</c> is
    /// followed by a zero-width space, which keeps it from forming a tag while it still reads as "&lt;" on screen. This
    /// does not rely on any optional TextMeshPro tag, so it works whatever the game's TMP version supports.
    /// </summary>
    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('<') < 0)
            return value;
        return value.Replace("<", "<\u200B", StringComparison.Ordinal);
    }
}
