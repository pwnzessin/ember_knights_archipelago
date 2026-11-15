namespace EmberKnights.Archipelago;

/// <summary>Pure decisions about when Relic Orb location checks are sent. No game dependencies.</summary>
internal static class RelicOrbPolicy
{
    /// <summary>
    /// Reads the slot value for "relic_orb_checks". A seed generated before the option existed has no value and
    /// must keep the original behavior (check on pickup) so a running session does not change halfway through.
    /// </summary>
    public static bool ChecksOnEsperTurnIn(string? slotValue)
    {
        if (string.IsNullOrWhiteSpace(slotValue))
            return false;
        return string.Equals(slotValue, "on_esper_turn_in", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(slotValue, "0", StringComparison.Ordinal);
    }

    /// <summary>
    /// Chooses which relics Esper's reveal shows for the orbs being handed in. The game would show the relic it
    /// dropped (always the first locked one in its own order); the multiworld decides what the player really owns,
    /// so the reveal shows the relics received so far, oldest first, continuing after the ones already revealed.
    /// A slot with nothing received to show is <c>null</c> and keeps the game's own relic.
    /// </summary>
    public static int?[] ChooseRevealedRelics(IReadOnlyList<int> receivedRelicDefIds, int alreadyRevealed, int orbsHandedIn)
    {
        var shown = new int?[Math.Max(0, orbsHandedIn)];
        var next = Math.Max(0, alreadyRevealed);
        for (var slot = 0; slot < shown.Length && next < receivedRelicDefIds.Count; slot++)
            shown[slot] = receivedRelicDefIds[next++];
        return shown;
    }

    /// <summary>
    /// Counts the relics waiting for Esper that belong to the 53 randomized orbs. Other relics (DLC orbs, etc.)
    /// share the same pending list but are not Archipelago locations.
    /// </summary>
    public static int CountPendingOrbRelics(IEnumerable<int>? pendingRelicDefIds, Func<int, bool> isOrbRelic)
    {
        if (pendingRelicDefIds is null)
            return 0;
        var count = 0;
        foreach (var defId in pendingRelicDefIds)
        {
            if (isOrbRelic(defId))
                count++;
        }
        return count;
    }
}
