namespace EmberKnights.Archipelago;

/// <summary>
/// Decides which Ember Tree nodes and weapon mods count as checks. A fresh save starts with some of them already
/// unlocked by the game itself (five Ember Tree nodes), and a reused save can hold many more. Reporting those would send
/// checks the player never earned, so the first time a seed connects the nodes that are already unlocked are written
/// down as a baseline and never reported. After that a node is a check only when it is unlocked and not in the baseline.
/// The baseline is stored with the seed's session state, so it is taken once: a node bought later is still reported
/// after a disconnect and reconnect, and a seed that has run before keeps working as it did.
/// Pure logic with no game dependencies, so it can be unit tested.
/// </summary>
internal static class UnlockBaseline
{
    /// <summary>
    /// True when the baseline still has to be taken: the seed has never recorded one. The first scan of a seed is the
    /// moment to take it, because nothing the player did can have happened before the very first scan.
    /// </summary>
    public static bool NeedsBaseline(bool baselineTaken) => !baselineTaken;

    /// <summary>
    /// Splits the currently unlocked ids into those to remember as the baseline (all of them, on the first scan).
    /// </summary>
    public static HashSet<int> TakeBaseline(IEnumerable<int> unlockedNow) => new(unlockedNow);

    /// <summary>
    /// True when <paramref name="id"/> should be reported as a check: it is unlocked now and was not part of the
    /// baseline. A node that is not unlocked, or that the game gave for free, is never reported.
    /// </summary>
    public static bool ShouldReport(int id, bool unlockedNow, ISet<int> baseline) => unlockedNow && !baseline.Contains(id);

    /// <summary>
    /// Whether a seed that has already run (it has received items or holds pending checks) should be given a baseline.
    /// Such a seed existed before the baseline did, so it has already reported everything it was going to, and giving it
    /// a baseline now would silently drop nodes the player bought but has not yet been credited for.
    /// </summary>
    public static bool SeedHasAlreadyRun(int nextItemIndex, int pendingCheckCount) =>
        nextItemIndex > 0 || pendingCheckCount > 0;
}
