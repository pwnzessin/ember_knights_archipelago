namespace EmberKnights.Archipelago;

/// <summary>
/// Decides what the plugin is allowed to control when a seed includes the Wrath of the Architect DLC, and what it must
/// leave exactly as the game has it. Pure logic with no game dependencies, so it can be unit tested.
///
/// The rule behind every method here: with the DLC off, the plugin behaves exactly as it did before the DLC existed in
/// it. A player who owns the DLC may already have the Hyper Gloves, the Ember Buster, DLC skills or DLC relics in their
/// real save, and a seed that does not use the DLC must never take them away or hand them out.
/// </summary>
internal static class DlcPolicy
{
    /// <summary>
    /// True when the seed was generated with the DLC option on. Reads <c>dlc</c> from slot data. A seed from before the
    /// option existed has no such key and counts as off, so it keeps the earlier behavior.
    /// </summary>
    public static bool ReadDlcActive(object? raw) => raw switch
    {
        bool value => value,
        string text => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1",
        null => false,
        _ => TryNumber(raw),
    };

    private static bool TryNumber(object raw)
    {
        try
        {
            return Convert.ToInt64(raw) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the plugin manages this weapon, meaning it may add it to or remove it from the save. A base weapon is
    /// managed whenever the plugin is active. A DLC weapon is managed only when the seed uses the DLC.
    /// </summary>
    public static bool IsManagedWeapon(bool isBaseRandomized, bool isDlcWeapon, bool dlcActive) =>
        isBaseRandomized || (isDlcWeapon && dlcActive);

    /// <summary>Whether the plugin controls this skill's unlocked state. A DLC skill is controlled only with the DLC on.</summary>
    public static bool IsManagedAbility(bool isTabletSkill, bool isDlcSkill, bool dlcActive) =>
        isTabletSkill || (isDlcSkill && dlcActive);

    /// <summary>Whether the plugin controls this relic. A DLC orb relic is controlled only with the DLC on.</summary>
    public static bool IsManagedRelic(bool isBaseOrbRelic, bool isDlcOrbRelic, bool dlcActive) =>
        isBaseOrbRelic || (isDlcOrbRelic && dlcActive);

    /// <summary>
    /// The number of orb checks a seed has: the base orbs, plus the DLC orbs when the DLC is on. An orb past this count
    /// has no location and is ignored.
    /// </summary>
    public static int OrbCheckCount(int baseOrbs, int dlcOrbs, bool dlcActive) => baseOrbs + (dlcActive ? dlcOrbs : 0);

    /// <summary>
    /// The area numbers whose rooms count toward milestones: the five base worlds, plus the four DLC worlds and the
    /// Architect level when the DLC is on.
    /// </summary>
    public static bool IsTrackedWorld(int areaName, bool dlcActive) =>
        (areaName >= 1 && areaName <= 5) || (dlcActive && (areaName is >= 8 and <= 11 || areaName == 14));

    /// <summary>
    /// Whether an area is a place a run is played in (as opposed to the Nexus hub), which is what resets the per-run
    /// heat roll when the player returns to the hub.
    /// </summary>
    public static bool IsRunWorld(int areaName, bool dlcActive) => IsTrackedWorld(areaName, dlcActive);
}
