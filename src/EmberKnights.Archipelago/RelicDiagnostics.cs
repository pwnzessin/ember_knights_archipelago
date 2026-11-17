using System.Text;
using HarmonyLib;

namespace EmberKnights.Archipelago;

/// <summary>
/// Read-only logging that records how the vanilla relic flow (orb pickup, then Esper's identification
/// turn-in in the hub) touches the save's relic lists. Used to design the "check on Esper turn-in" behavior.
/// None of the patches below change game behavior.
/// </summary>
internal static class RelicDiagnostics
{
    private static bool lastCanIdentify;
    private static bool lastCanIdentifyKnown;
    private static bool insideEsperSequenceStart;

    public static bool Enabled => ArchipelagoPlugin.Instance?.RelicDiagnosticsEnabled?.Value ?? false;

    public static void Log(string message) =>
        ArchipelagoPlugin.LogSource.LogInfo($"[RelicDiag] {message}");

    public static string Describe(GameCode.SavedPlayerSlot? slot)
    {
        if (slot is null)
            return "slot=null";
        return $"unknown={Format(slot.unknownRelics)} new={Format(slot.newRelics)} unlocked={Count(slot.relicUnlocks)}";
    }

    public static string DescribeCurrent()
    {
        try
        {
            return Describe(GameCode.Game.LocalSaveSlot);
        }
        catch (Exception exception)
        {
            return $"slot unavailable ({exception.GetType().Name})";
        }
    }

    private static string Count(Il2CppSystem.Collections.Generic.List<int>? list) =>
        list is null ? "null" : list.Count.ToString();

    private static string Format(Il2CppSystem.Collections.Generic.List<int>? list)
    {
        if (list is null)
            return "null";
        var builder = new StringBuilder("[");
        for (var index = 0; index < list.Count; index++)
        {
            if (index > 0)
                builder.Append(',');
            builder.Append(list[index]);
        }
        return builder.Append(']').ToString();
    }

    public static void NoteCanIdentify(bool result, GameCode.SavedPlayerSlot? slot)
    {
        if (lastCanIdentifyKnown && lastCanIdentify == result)
            return;
        lastCanIdentifyKnown = true;
        lastCanIdentify = result;
        Log($"CanEsperIdentifyUnknownRelics -> {result} ({Describe(slot)})");
    }

    public static void EnterSequenceStart() => insideEsperSequenceStart = true;

    public static void ExitSequenceStart() => insideEsperSequenceStart = false;

    public static bool InsideSequenceStart => insideEsperSequenceStart;
}

[HarmonyPatch(typeof(GameCode.PlayerUtils), "PickupUnknownRelicDrop")]
internal static class RelicDiagPickupPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameCode.XDropCMP DropCMP, bool __result)
    {
        try
        {
            if (!RelicDiagnostics.Enabled)
                return;
            var defId = DropCMP?.UnknownRelicDefId ?? -1;
            RelicDiagnostics.Log(
                $"PickupUnknownRelicDrop result={__result} relicDefId={defId} isOrbRelic={GameCatalog.Data.IsOrbRelic(defId)} " +
                RelicDiagnostics.DescribeCurrent());
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.PickupUnknownRelicDrop", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.NpcUtils), nameof(GameCode.NpcUtils.CanEsperIdentifyUnknownRelics))]
internal static class RelicDiagCanIdentifyPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameCode.SavedPlayerSlot SaveSlot, bool __result)
    {
        try
        {
            if (RelicDiagnostics.Enabled)
                RelicDiagnostics.NoteCanIdentify(__result, SaveSlot);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.CanEsperIdentifyUnknownRelics", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.GameUtils), nameof(GameCode.GameUtils.StartEsperIdentifyRelicsSequence))]
internal static class RelicDiagEsperStartPatch
{
    [HarmonyPrefix]
    private static void Prefix(GameCode.SavedPlayerSlot SaveSlot)
    {
        try
        {
            if (!RelicDiagnostics.Enabled)
                return;
            RelicDiagnostics.EnterSequenceStart();
            RelicDiagnostics.Log($"StartEsperIdentifyRelicsSequence BEFORE ({RelicDiagnostics.Describe(SaveSlot)})");
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.StartEsperIdentifyRelicsSequence(pre)", exception);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(GameCode.SavedPlayerSlot SaveSlot)
    {
        try
        {
            if (!RelicDiagnostics.Enabled)
                return;
            RelicDiagnostics.ExitSequenceStart();
            RelicDiagnostics.Log($"StartEsperIdentifyRelicsSequence AFTER ({RelicDiagnostics.Describe(SaveSlot)})");
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.StartEsperIdentifyRelicsSequence(post)", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.AddUnknownRelic))]
internal static class RelicDiagAddUnknownPatch
{
    [HarmonyPostfix]
    private static void Postfix(int __0)
    {
        try
        {
            if (RelicDiagnostics.Enabled)
                RelicDiagnostics.Log($"AddUnknownRelic({__0}) -> {RelicDiagnostics.DescribeCurrent()}");
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.AddUnknownRelic", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.RemoveUnknownRelic))]
internal static class RelicDiagRemoveUnknownPatch
{
    [HarmonyPostfix]
    private static void Postfix(int __0)
    {
        try
        {
            if (RelicDiagnostics.Enabled)
                RelicDiagnostics.Log(
                    $"RemoveUnknownRelic({__0}) insideSequenceStart={RelicDiagnostics.InsideSequenceStart} -> {RelicDiagnostics.DescribeCurrent()}");
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.RemoveUnknownRelic", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.AddNewRelic))]
internal static class RelicDiagAddNewPatch
{
    [HarmonyPostfix]
    private static void Postfix(int __0)
    {
        try
        {
            if (RelicDiagnostics.Enabled)
                RelicDiagnostics.Log(
                    $"AddNewRelic({__0}) insideSequenceStart={RelicDiagnostics.InsideSequenceStart} -> {RelicDiagnostics.DescribeCurrent()}");
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.AddNewRelic", exception);
        }
    }
}

/// <summary>
/// Logs what the relic selector (the "pick one of three" screen) was offered and what the mod knew when it rolled, so a
/// selector that offers too few relics can be diagnosed from <c>LogOutput.log</c> instead of guessed at. Read-only: the
/// postfix only looks at the finished selections and never changes them.
/// </summary>
internal static class SelectorDiagnostics
{
    private static int reports;

    /// <summary>Only the first few selectors are described; every one is a screenful of lines.</summary>
    private const int MaxReports = 4;

    public static void Describe(
        Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<GameCode.XRelicInst>>? selections,
        GameCode.SavedPlayerSlot? saveSlot,
        eAreaName area)
    {
        if (reports >= MaxReports)
            return;
        reports++;

        var service = ArchipelagoPlugin.Instance.Service;
        var offered = new List<string>();
        var groups = selections?.Count ?? 0;
        for (var g = 0; g < groups; g++)
        {
            var group = selections![g];
            var ids = new List<string>();
            for (var i = 0; group is not null && i < group.Count; i++)
            {
                var relic = group[i];
                if (relic is null)
                    continue;
                var id = relic.DefId;
                ids.Add($"{id}({Name(id)}{(service.OwnsRelic(id) ? ",owned" : ",NOT-owned")})");
            }
            offered.Add($"player{g}=[{string.Join(" ", ids)}]");
        }

        RelicDiagnostics.Log(
            $"RelicSelector #{reports} area={(int)area} offered: {(offered.Count == 0 ? "nothing" : string.Join(" ", offered))}");
        RelicDiagnostics.Log(
            $"RelicSelector #{reports} mod state: active={service.IsActive} dlc={service.DlcActive} " +
            $"startingRelicsKnown={service.StartingRelicCount >= 0} startingRelics={service.StartingRelicCount} " +
            $"ownedRelics={service.OwnedRelicCount} save: {RelicDiagnostics.Describe(saveSlot)}");

        // For every relic the game could roll: what the mod says, what the game's own list says. A relic the mod calls
        // unlocked but the selector never offered was dropped by a rule other than the unlock check.
        var rows = new List<string>();
        var defs = GameCode.Defs?.relicFile?.RelicDefs;
        if (defs is not null && saveSlot is not null)
        {
            for (var i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                if (def is null || !def.enabled)
                    continue;
                var managed = service.IsManagedRelic(def.id);
                var owned = service.OwnsRelic(def.id);
                var vanilla = saveSlot.IsRelicUnlocked(def.id);
                if (!managed && !owned)
                    continue;
                rows.Add(
                    $"{def.id}:{(managed ? "M" : "-")}{(owned ? "O" : "-")}{(vanilla ? "U" : "-")}" +
                    $"r{(int)def.Rarity}max{def.MaxOwned}");
            }
        }
        RelicDiagnostics.Log(
            $"RelicSelector #{reports} per relic (id:Managed/Owned/vanillaUnlocked rarity maxOwned), managed or owned only: " +
            string.Join(" ", rows));
    }

    /// <summary>
    /// Prints what each eligibility check answered for every relic the player owns. A check missing from a relic's line
    /// means the game never asked it, which means an earlier check had already removed the relic.
    /// </summary>
    /// <summary>
    /// Logs how many cards the game itself wants per player for a relic selector. A selector that came out shorter than this
    /// ran out of eligible relics; knowing the target is what lets a short one be told apart from a normal one.
    /// Read-only: it asks the game the same question the selector asks.
    /// </summary>
    public static void DescribeTarget(GameCode.XWorld? world, int selectorNumber)
    {
        try
        {
            if (world is null)
            {
                RelicDiagnostics.Log($"RelicSelector #{selectorNumber} target card count: no world available.");
                return;
            }
            var counts = new Il2CppSystem.Collections.Generic.List<int>();
            GameCode.PlayerUtils.GetNumberOfItemSelectionsForMode(world, GameCode.eUIItemSelectMode.Relics, counts);
            var shown = new List<string>();
            for (var i = 0; i < counts.Count; i++)
                shown.Add($"player{i}={counts[i]}");
            RelicDiagnostics.Log(
                $"RelicSelector #{selectorNumber} target card count per player (what the game asks for): " +
                (shown.Count == 0 ? "none reported" : string.Join(" ", shown)));
        }
        catch (Exception exception)
        {
            RelicDiagnostics.Log($"RelicSelector #{selectorNumber} target card count unavailable: {exception.Message}");
        }
    }

    /// <summary>
    /// Logs what the game counts as owned in each item category for each live player, using the same function the selector
    /// uses to test a relic's required category amounts. Compared against the dump's per-relic requirement (a category plus
    /// a count), this shows why a relic with a requirement was or was not eligible. Read-only.
    /// </summary>
    public static void DescribeOwnedCategories(GameCode.XWorld? world, int selectorNumber)
    {
        try
        {
            var players = world?.LivePlayers;
            if (players is null || players.Count == 0)
            {
                RelicDiagnostics.Log($"RelicSelector #{selectorNumber} owned category counts: no live players.");
                return;
            }
            var names = Enum.GetNames(typeof(eItemCategory));
            for (var p = 0; p < players.Count; p++)
            {
                var player = players[p];
                if (player is null)
                    continue;
                var counts = new Il2CppSystem.Collections.Generic.List<int>();
                GameCode.PlayerUtils.GetOwnedCategoryMaskAndCounts(player, out var mask, counts);
                var parts = new List<string>();
                for (var i = 0; i < counts.Count && i < names.Length; i++)
                {
                    if (counts[i] != 0)
                        parts.Add($"{names[i]}={counts[i]}");
                }
                RelicDiagnostics.Log(
                    $"RelicSelector #{selectorNumber} owned category counts player{p} (mask {mask}): " +
                    (parts.Count == 0 ? "none" : string.Join(" ", parts)));
            }
        }
        catch (Exception exception)
        {
            RelicDiagnostics.Log($"RelicSelector #{selectorNumber} owned category counts unavailable: {exception.Message}");
        }
    }

    public static void DescribeChecks(object? unused, int selectorNumber)
    {
        var service = ArchipelagoPlugin.Instance.Service;
        var (lines, categoryCalls) = SelectorCheckRecorder.End(service.OwnsRelic);
        RelicDiagnostics.Log(
            $"RelicSelector #{selectorNumber} checks per owned relic (ok = passed, NO = rejected, missing = never asked): " +
            (lines.Count == 0 ? "no owned relic was ever checked" : string.Join(" ", lines)));
        RelicDiagnostics.Log(
            $"RelicSelector #{selectorNumber} category check was called {categoryCalls} time(s) during this selector.");
    }

    public static int ReportCount => reports;

    private static string Name(int defId)
    {
        try
        {
            return XRelicDef.Get(defId)?.niceName ?? "?";
        }
        catch (Exception)
        {
            return "?";
        }
    }
}

/// <summary>
/// Records, while a relic selector is being generated, what each of the game's eligibility checks answered for each relic,
/// so the log can say which rule removed a relic instead of only which relic came out. Collects nothing outside a selector
/// and nothing when the diagnostics are off. Read-only: the patches that feed it never change a result.
/// </summary>
internal static class SelectorCheckRecorder
{
    private static bool active;
    private static readonly SortedDictionary<int, SortedDictionary<string, bool>> Verdicts = new();
    private static int categoryCalls;

    public static bool Active => active;

    public static void Begin()
    {
        active = true;
        categoryCalls = 0;
        Verdicts.Clear();
    }

    public static void Note(int relicId, string check, bool verdict)
    {
        if (!active)
            return;
        if (!Verdicts.TryGetValue(relicId, out var checks))
            Verdicts[relicId] = checks = new SortedDictionary<string, bool>();
        // The same check can run more than once per relic (a reroll); keep the last answer and remember a "no".
        checks[check] = checks.TryGetValue(check, out var previous) ? previous && verdict : verdict;
        if (check == "category")
            categoryCalls++;
    }

    /// <summary>
    /// What the game answered for each relic so far, as a snapshot. Used by the selector top-up to find owned relics that
    /// passed every check. Read-only: it copies, so the caller cannot disturb the recording.
    /// </summary>
    public static Dictionary<int, SelectorTopUpPolicy.Verdicts> Snapshot()
    {
        var result = new Dictionary<int, SelectorTopUpPolicy.Verdicts>();
        foreach (var (id, checks) in Verdicts)
        {
            bool? Get(string name) => checks.TryGetValue(name, out var value) ? value : null;
            result[id] = new SelectorTopUpPolicy.Verdicts(Get("maxOwned"), Get("unlocked"), Get("weapon"), Get("category"));
        }
        return result;
    }

    /// <summary>Ends recording and returns one text line per relic that was asked about, with each check's answer.</summary>
    public static (List<string> Lines, int CategoryCalls) End(Func<int, bool> owned)
    {
        active = false;
        var lines = new List<string>();
        foreach (var (id, checks) in Verdicts)
        {
            if (!owned(id))
                continue;
            var parts = checks.Select(entry => $"{entry.Key}={(entry.Value ? "ok" : "NO")}");
            lines.Add($"{id}:{string.Join(",", parts)}");
        }
        var calls = categoryCalls;
        Verdicts.Clear();
        return (lines, calls);
    }
}

[HarmonyPatch(typeof(GameCode.RelicUtils), nameof(GameCode.RelicUtils.NEW_GenerateRelicSelections))]
internal static class RelicDiagSelectorBeginPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix()
    {
        try
        {
            // The recorder feeds both the diagnostics and the short-selector top-up, so it runs if either is on.
            var plugin = ArchipelagoPlugin.Instance;
            if (plugin.Service.IsActive && (RelicDiagnostics.Enabled || plugin.FillShortRelicSelectorsEnabled.Value))
                SelectorCheckRecorder.Begin();
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.NEW_GenerateRelicSelections(pre)", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.RelicUtils), nameof(GameCode.RelicUtils.PassesAllMaxOwnedChecks))]
internal static class RelicDiagMaxOwnedPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(XRelicDef Def, bool __result)
    {
        try
        {
            if (SelectorCheckRecorder.Active && Def is not null)
                SelectorCheckRecorder.Note(Def.id, "maxOwned", __result);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.PassesAllMaxOwnedChecks", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.RelicUtils), nameof(GameCode.RelicUtils.CanDropRelicForWeaponTypes))]
internal static class RelicDiagWeaponTypePatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(XRelicDef Def, bool __result)
    {
        try
        {
            if (SelectorCheckRecorder.Active && Def is not null)
                SelectorCheckRecorder.Note(Def.id, "weapon", __result);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.CanDropRelicForWeaponTypes", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.RelicUtils), nameof(GameCode.RelicUtils.MatchesCategoriesAndCountsForAllPlayers))]
internal static class RelicDiagCategoryPatch
{
    // Runs after the waiver (which is a postfix with default priority) so the logged answer is the one the selector received.
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(XRelicDef Def, bool __result)
    {
        try
        {
            if (SelectorCheckRecorder.Active && Def is not null)
                SelectorCheckRecorder.Note(Def.id, "category", __result);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.MatchesCategoriesAndCountsForAllPlayers", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.IsItemUnlocked))]
internal static class RelicDiagItemUnlockedPatch
{
    // After the mod's own IsItemUnlocked postfix, so this is what the selector actually saw.
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Il2CppSystem.Object item, bool __result)
    {
        try
        {
            if (!SelectorCheckRecorder.Active || item is null)
                return;
            if (item.TryCast<XRelicDef>() is { } relic)
                SelectorCheckRecorder.Note(relic.id, "unlocked", __result);
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.IsItemUnlocked", exception);
        }
    }
}

[HarmonyPatch(typeof(GameCode.RelicUtils), nameof(GameCode.RelicUtils.NEW_GenerateRelicSelections))]
internal static class RelicDiagSelectorPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        GameCode.XWorld World,
        GameCode.SavedPlayerSlot SaveSlot,
        eAreaName Area,
        Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<GameCode.XRelicInst>> OutList)
    {
        try
        {
            if (RelicDiagnostics.Enabled && ArchipelagoPlugin.Instance.Service.IsActive)
            {
                var before = SelectorDiagnostics.ReportCount;
                SelectorDiagnostics.Describe(OutList, SaveSlot, Area);
                // Describe stops counting after a few selectors. Only describe the checks for a selector it described, and
                // always close the recorder so it never keeps collecting.
                if (SelectorDiagnostics.ReportCount > before)
                {
                    SelectorDiagnostics.DescribeChecks(null, SelectorDiagnostics.ReportCount);
                    SelectorDiagnostics.DescribeTarget(World, SelectorDiagnostics.ReportCount);
                    SelectorDiagnostics.DescribeOwnedCategories(World, SelectorDiagnostics.ReportCount);
                }
                else
                    SelectorCheckRecorder.End(_ => false);
            }
        }
        catch (Exception exception)
        {
            SelectorCheckRecorder.End(_ => false);
            PatchSafety.Report("RelicDiag.NEW_GenerateRelicSelections", exception);
        }
    }
}

// AddRelicUnlockId is already guarded by RelicGrantGuardPatch; this prefix only logs and never blocks.
[HarmonyPatch(typeof(GameCode.SavedPlayerSlot), nameof(GameCode.SavedPlayerSlot.AddRelicUnlockId))]
internal static class RelicDiagAddUnlockPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(int __0)
    {
        try
        {
            if (RelicDiagnostics.Enabled)
                RelicDiagnostics.Log(
                    $"AddRelicUnlockId({__0}) suppressedByPlugin={ArchipelagoPlugin.Instance.Service.ShouldSuppressRelicGrant(__0)} " +
                    $"insideSequenceStart={RelicDiagnostics.InsideSequenceStart}");
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicDiag.AddRelicUnlockId", exception);
        }
    }
}
