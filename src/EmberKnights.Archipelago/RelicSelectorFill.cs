using HarmonyLib;

namespace EmberKnights.Archipelago;

/// <summary>
/// Fills a relic selector that came out short. The game rolls each card's rarity (about 84% Common) and then picks a relic of
/// that rarity the player may be offered, so a player who owns few relics, as with a seed that starts with a handful drawn from
/// the whole pool, can run out of Commons and see one or two cards instead of three. After the game has built the selector,
/// this adds relics the player owns that every one of the game's own checks allowed, until there are three.
///
/// It only ever adds. A selector that already has three cards is left exactly as the game made it, and a relic the game
/// rejected, or stopped asking about, is never added.
/// </summary>
[HarmonyPatch(typeof(GameCode.RelicUtils), nameof(GameCode.RelicUtils.NEW_GenerateRelicSelections))]
internal static class RelicSelectorFillPatch
{
    // Runs before the diagnostic postfix, which ends the recorder. The diagnostic then logs the selector as it now stands.
    [HarmonyPostfix]
    [HarmonyPriority(Priority.High)]
    private static void Postfix(
        GameCode.XWorld World,
        Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<GameCode.XRelicInst>> OutList)
    {
        try
        {
            var plugin = ArchipelagoPlugin.Instance;
            if (!plugin.Service.IsActive || !plugin.FillShortRelicSelectorsEnabled.Value || !SelectorCheckRecorder.Active)
                return;
            if (OutList is null)
                return;

            var service = plugin.Service;
            var verdicts = SelectorCheckRecorder.Snapshot();

            for (var player = 0; player < OutList.Count; player++)
            {
                var cards = OutList[player];
                if (cards is null)
                    continue;

                var offered = new List<int>();
                for (var i = 0; i < cards.Count; i++)
                {
                    if (cards[i] is { } card)
                        offered.Add(card.DefId);
                }
                if (offered.Count >= SelectorTopUpPolicy.TargetCards)
                    continue;

                var candidates = SelectorTopUpPolicy.Candidates(verdicts, service.OwnsRelic, offered);
                var additions = SelectorTopUpPolicy.ChooseAdditions(offered, candidates, System.Random.Shared.Next);
                var added = new List<int>();
                foreach (var defId in additions)
                {
                    var def = XRelicDef.Get(defId);
                    if (def is null)
                        continue;
                    // CreateRelicInst only builds the card. GenerateRelic(XEntity, DropItemInfo_t) also marks the relic as
                    // cleansed in the save, which a card the player has not chosen must not do.
                    var card = GameCode.RelicUtils.CreateRelicInst(def, 1);
                    if (card is null)
                        continue;
                    cards.Add(card);
                    added.Add(defId);
                }

                if (added.Count > 0)
                {
                    ArchipelagoPlugin.LogSource.LogInfo(
                        $"[RelicFill] Selector for player {player} had {offered.Count} card(s); added {added.Count} owned relic(s): " +
                        string.Join(", ", added.Select(id => $"{id} ({XRelicDef.Get(id)?.niceName ?? "?"})")) + ".");
                }
                else
                {
                    ArchipelagoPlugin.LogSource.LogInfo(
                        $"[RelicFill] Selector for player {player} had {offered.Count} card(s) and no owned relic that passed every check was left to add.");
                }
            }
        }
        catch (Exception exception)
        {
            PatchSafety.Report("RelicSelectorFill", exception);
        }
    }
}
