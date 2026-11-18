namespace EmberKnights.Archipelago;

/// <summary>
/// Decides which Ember Tree nodes and weapon mods count as checks. A fresh save starts with some of them already
/// unlocked by the game itself (Ember Tree nodes at level 0, such as Thick Skin and Slayer), and a reused save can hold
/// many more. Reporting those would send checks the player never earned, so the first time a seed connects the weapon
/// mods that are already unlocked and the Ember Tree nodes that already have a bought level are written down as a
/// baseline and never reported. After that a weapon mod is a check when it is unlocked and not in the baseline, and an
/// Ember Tree node is a check when it has a bought level and is not in the baseline.
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
    /// True when an Ember Tree node counts as bought. The game unlocks a few nodes for free on a fresh save (Thick Skin
    /// and Slayer), at level 0, so "unlocked" alone cannot tell a gift from a purchase. A node is bought once it is
    /// unlocked and at least one upgrade level has been paid for.
    /// </summary>
    public static bool IsEmberTreeNodePurchased(bool unlocked, int upgradeLevels) => unlocked && upgradeLevels >= 1;

    /// <summary>
    /// Records the upgrade level of every Ember Tree node that is already bought, keyed by node id. A node whose level is
    /// 0 (a free gift, or never touched) is deliberately left out, so buying its first level later is still a check.
    /// </summary>
    public static Dictionary<int, int> TakeEmberTreeBaseline(IEnumerable<KeyValuePair<int, (bool Unlocked, int Levels)>> nodes)
    {
        var baseline = new Dictionary<int, int>();
        foreach (var node in nodes)
        {
            if (IsEmberTreeNodePurchased(node.Value.Unlocked, node.Value.Levels))
                baseline[node.Key] = node.Value.Levels;
        }
        return baseline;
    }

    /// <summary>
    /// True when an Ember Tree node should be reported as a check: it is bought now and was not already bought when the
    /// baseline was taken. A node that sat at level 0 in the baseline is reported as soon as its first level is paid.
    /// </summary>
    public static bool ShouldReportEmberTreeNode(int id, bool unlocked, int upgradeLevels, IReadOnlyDictionary<int, int> baseline) =>
        IsEmberTreeNodePurchased(unlocked, upgradeLevels) && !baseline.ContainsKey(id);

    /// <summary>
    /// True when a call to the game's Ember Tree purchase method (<c>SavedPlayerSlot.UnlockEmberTreeUpgade</c>) really
    /// bought the node, so its check should be sent. The game calls that method only when the player buys a node, never
    /// while it sets the tree up (that is <c>AddEmberTreeUnlock</c>), so the call is the purchase itself and needs no
    /// baseline. The node must be unlocked once the call returns; a call that left it locked (a refused purchase) is not
    /// a purchase. <paramref name="alreadyUnlockedBefore"/> keeps a repeated call for a node that was already unlocked
    /// from sending the same check again; the server ignores a duplicate, but a no-op should not look like a purchase.
    /// </summary>
    public static bool IsEmberTreePurchase(bool unlockedAfterCall, bool alreadyUnlockedBefore) =>
        unlockedAfterCall && !alreadyUnlockedBefore;

    /// <summary>
    /// Migrates the old id-only Ember Tree baseline (every node unlocked at first connect, including the free ones). The
    /// old list cannot say what level each node had, so every node that is purchased right now stays excluded and every
    /// node that is still at level 0 (Thick Skin and Slayer on an untouched tree) becomes reportable again.
    /// </summary>
    public static Dictionary<int, int> MigrateEmberTreeBaseline(
        IEnumerable<int> oldBaselineIds,
        IReadOnlyDictionary<int, (bool Unlocked, int Levels)> currentNodes)
    {
        var baseline = new Dictionary<int, int>();
        foreach (var id in oldBaselineIds)
        {
            if (currentNodes.TryGetValue(id, out var node) && IsEmberTreeNodePurchased(node.Unlocked, node.Levels))
                baseline[id] = node.Levels;
        }
        return baseline;
    }

    /// <summary>
    /// Whether a seed that has already run (it has received items or holds pending checks) should be given a baseline.
    /// Such a seed existed before the baseline did, so it has already reported everything it was going to, and giving it
    /// a baseline now would silently drop nodes the player bought but has not yet been credited for.
    /// </summary>
    public static bool SeedHasAlreadyRun(int nextItemIndex, int pendingCheckCount) =>
        nextItemIndex > 0 || pendingCheckCount > 0;
}
