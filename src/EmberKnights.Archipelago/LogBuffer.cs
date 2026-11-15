namespace EmberKnights.Archipelago;

/// <summary>What kind of line a log message is, so the window can tint it. Never changes the text.</summary>
internal enum LogKind
{
    Info,
    Received,
    Sent,
    Chat,
    Warning,
}

/// <summary>One message with the time it arrived. The text is stored as received, without markup.</summary>
internal readonly record struct LogEntry(DateTime Time, string Text, LogKind Kind);

/// <summary>
/// Keeps the Archipelago log lines the HUD window shows, and lays them out as rows. Pure logic with no game
/// dependencies, so it can be unit tested. Messages are stored whole; wrapping to the window width happens when the
/// visible rows are built, so resizing or changing the font never loses anything.
///
/// Layout: the first row of a message starts with its time, and any wrapped rows hang underneath the text, indented
/// to the same column, so it is clear which rows belong to which message. A blank row separates messages when there
/// is room for it.
/// </summary>
internal sealed class LogBuffer
{
    /// <summary>Width of the time column in characters, including the gap after it ("14:23" plus two spaces).</summary>
    public const int TimeColumn = 7;

    private readonly List<LogEntry> entries = new();
    private readonly int capacity;

    public LogBuffer(int capacity)
    {
        this.capacity = Math.Max(1, capacity);
    }

    public int Count => entries.Count;

    /// <summary>Bumped on every change so a window can tell when it needs to rebuild its text.</summary>
    public int Version { get; private set; }

    /// <summary>The stored plain text of every message, oldest first.</summary>
    public IReadOnlyList<string> Texts => entries.ConvertAll(entry => entry.Text);

    /// <summary>
    /// Adds a message. Blank messages are ignored, and a message identical to the newest one is dropped so a repeated
    /// status line does not fill the window. The oldest message is discarded once the buffer is full.
    /// </summary>
    public bool Add(string? line) => Add(line, DateTime.Now);

    /// <summary>Adds a message with an explicit time, so tests do not depend on the clock.</summary>
    public bool Add(string? line, DateTime time)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;
        var text = line.Trim();
        if (entries.Count > 0 && string.Equals(entries[^1].Text, text, StringComparison.Ordinal))
            return false;
        if (entries.Count >= capacity)
            entries.RemoveAt(0);
        entries.Add(new LogEntry(time, text, Classify(text)));
        Version++;
        return true;
    }

    public void Clear()
    {
        if (entries.Count == 0)
            return;
        entries.Clear();
        Version++;
    }

    /// <summary>Decides how a message is tinted from its wording. Only affects colour, never the text.</summary>
    public static LogKind Classify(string text)
    {
        if (text.StartsWith("Warning", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("could not", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("connection closed", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Set slot_name", StringComparison.OrdinalIgnoreCase))
            return LogKind.Warning;
        if (text.StartsWith("Received", StringComparison.OrdinalIgnoreCase))
            return LogKind.Received;
        if (text.StartsWith("Check:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Goal achieved", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("DeathLink sent", StringComparison.OrdinalIgnoreCase))
            return LogKind.Sent;
        // Server chat is "Name: text". A short name before the first colon, with a space after it, is a chat line.
        var colon = text.IndexOf(": ", StringComparison.Ordinal);
        if (colon is > 0 and <= 24 && !text.AsSpan(0, colon).Contains(' '))
            return LogKind.Chat;
        return LogKind.Info;
    }

    /// <summary>One drawn row of the log. <see cref="Time"/> is only set on the first row of a message.</summary>
    internal readonly record struct Row(string? Time, string Text, LogKind Kind, bool StartsMessage);

    /// <summary>
    /// The newest messages that fit into <paramref name="maxRows"/> display rows, oldest first. The text area is
    /// <paramref name="maxCharsPerRow"/> characters wide, not counting the time column. A long message wraps onto
    /// several rows at word boundaries (a single word longer than a row is split). When the window cannot hold the
    /// newest message completely, its first rows are kept so its start stays readable.
    ///
    /// With <paramref name="gapBetweenMessages"/> a blank row is counted between two messages, so the returned rows
    /// plus those gaps never exceed <paramref name="maxRows"/>. A gap is only ever placed before a message that
    /// follows another one, never at the very top.
    /// </summary>
    public IReadOnlyList<Row> VisibleRows(int maxRows, int maxCharsPerRow, bool gapBetweenMessages = false)
    {
        maxRows = Math.Max(1, maxRows);
        var textWidth = Math.Max(8, maxCharsPerRow);

        var rows = new List<Row>();
        var used = 0;
        // Walk from the newest message backwards and stop once the window is full.
        for (var i = entries.Count - 1; i >= 0 && used < maxRows; i--)
        {
            var entry = entries[i];
            var wrapped = Wrap(entry.Text, textWidth);
            // The gap sits between this message and the newer one already placed, so it is paid for here.
            var gap = gapBetweenMessages && rows.Count > 0 ? 1 : 0;
            var room = maxRows - used - gap;
            if (room < 1)
                break;
            var take = Math.Min(room, wrapped.Count);
            for (var r = take - 1; r >= 0; r--)
                rows.Insert(0, new Row(r == 0 ? FormatTime(entry.Time) : null, wrapped[r], entry.Kind, r == 0));
            used += take + gap;
            if (take < wrapped.Count)
                break;
        }
        return rows;
    }

    public static string FormatTime(DateTime time) => time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Breaks one message into rows no longer than <paramref name="width"/>, preferring spaces.</summary>
    public static List<string> Wrap(string text, int width)
    {
        var result = new List<string>();
        var remaining = text.Trim();
        while (remaining.Length > width)
        {
            var cut = remaining.LastIndexOf(' ', width);
            if (cut <= 0)
                cut = width;
            result.Add(remaining[..cut].TrimEnd());
            remaining = remaining[cut..].TrimStart();
        }
        if (remaining.Length > 0 || result.Count == 0)
            result.Add(remaining);
        return result;
    }
}
