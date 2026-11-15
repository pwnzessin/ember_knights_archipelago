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
