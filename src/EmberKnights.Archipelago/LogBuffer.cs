namespace EmberKnights.Archipelago;

/// <summary>
/// Keeps the Archipelago log lines the HUD window shows. Pure logic with no game dependencies, so it can be unit
/// tested. Lines are stored whole; wrapping to the window width happens when the visible text is built, so resizing
/// or changing the font never loses anything.
/// </summary>
internal sealed class LogBuffer
{
    private readonly List<string> lines = new();
    private readonly int capacity;

    public LogBuffer(int capacity)
    {
        this.capacity = Math.Max(1, capacity);
    }

    public int Count => lines.Count;

    /// <summary>Bumped on every change so a window can tell when it needs to rebuild its text.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Adds a line. Blank lines are ignored, and a line identical to the newest one is dropped so a repeated status
    /// message does not fill the window. The oldest line is discarded once the buffer is full.
    /// </summary>
    public bool Add(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;
        var text = line.Trim();
        if (lines.Count > 0 && string.Equals(lines[^1], text, StringComparison.Ordinal))
            return false;
        if (lines.Count >= capacity)
            lines.RemoveAt(0);
        lines.Add(text);
        Version++;
        return true;
    }

    public void Clear()
    {
        if (lines.Count == 0)
            return;
        lines.Clear();
        Version++;
    }

    /// <summary>
    /// The newest lines that fit into <paramref name="maxRows"/> rows of <paramref name="maxCharsPerRow"/> characters,
    /// oldest first. A long line wraps onto several rows at word boundaries, and a single word longer than a row is
    /// split. Older lines that no longer fit are left out, so the newest line is always visible.
    /// </summary>
    public IReadOnlyList<string> VisibleRows(int maxRows, int maxCharsPerRow)
    {
        maxRows = Math.Max(1, maxRows);
        maxCharsPerRow = Math.Max(8, maxCharsPerRow);

        var rows = new List<string>();
        // Walk from the newest line backwards and stop once the window is full.
        for (var i = lines.Count - 1; i >= 0 && rows.Count < maxRows; i--)
        {
            var wrapped = Wrap(lines[i], maxCharsPerRow);
            var room = maxRows - rows.Count;
            // When a wrapped line does not fit completely, keep its first rows rather than its last, so the start
            // of the message stays readable.
            var take = Math.Min(room, wrapped.Count);
            for (var r = take - 1; r >= 0; r--)
                rows.Insert(0, wrapped[r]);
            if (take < wrapped.Count)
                break;
        }
        return rows;
    }

    /// <summary>Breaks one line into rows no longer than <paramref name="width"/>, preferring spaces.</summary>
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
