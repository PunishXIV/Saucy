#nullable disable
using Saucy.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Saucy.TripleTriad.UI;

public partial class TriadSession
{
    private static string FormatOptimizerTimedOutFallbackMessage() =>
        $"[Saucy] Deck optimizer timed out after {Math.Clamp(C.DeckOptimizerTimeoutMinutes, 1, 15)} min; using best deck found so far.";

    private static string FormatOptimizerAbortedFallbackMessage() =>
        "[Saucy] Deck optimizer aborted; using best deck found so far.";

    public void CancelDeckOptimizerJob(bool userCancelled = true, bool markTimedOut = false) =>
        TriadDeckOptimizerJobs.CancelActive(userCancelled, markTimedOut || userCancelled);

    private void StartDeckOptimizer(
        TriadNpc npc,
        List<TriadGameModifier> regionMods,
        bool premadeRequest = false,
        bool forceRebuild = false,
        bool navigationRequest = false)
    {
        if (npc == null)
        {
            return;
        }

        if (!premadeRequest &&
            !forceRebuild &&
            HasOptimizedDeckApplied &&
            optimizerTargetDeckId >= 0 &&
            preGameNpc?.Id == npc.Id &&
            (TriadUiState.IsPrepDeckSelectVisible() || TriadUiState.IsMatchRegistrationVisible()))
        {
            return;
        }

        if (!premadeRequest && TriadMapNavigation.TryRejectLockedNpcDuringNavigation(npc))
        {
            return;
        }

        if (!premadeRequest && !ShouldBuildOptimizedDeck())
        {
            return;
        }

        if (!premadeRequest &&
            !forceRebuild &&
            ShouldDeferOptimizerStartUntilRules(npc, regionMods))
        {
            return;
        }

        if (!premadeRequest &&
            !forceRebuild &&
            !TriadRunSession.NavigationRequiresOptimizedDeckBuild &&
            TriadCardFarmSession.IsModeActive() &&
            C.OnlyUnobtainedCards &&
            GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out var farmNpcInfo) &&
            TriadCardFarmSession.HasAllNpcRewardsOwned(farmNpcInfo))
        {
            return;
        }

        var optimizerKey = BuildOptimizerSessionKey(npc, regionMods);

        if (!premadeRequest &&
            !forceRebuild &&
            TriadDeckOptimizerJobs.TryGetActivePassIdForNpc(npc.Id, out var activePassId))
        {
            optimizerPassId = activePassId;
            return;
        }

        if (!premadeRequest &&
            HasOptimizedDeckApplied &&
            optimizerKey == optimizerSessionKey &&
            !TriadOptimizedDeckCacheValidator.ShouldRebuildDeckForNewCards(optimizerKey, npc.Id))
        {
            return;
        }

        lock (preGameLock)
        {
            if (forceRebuild)
            {
                PrepareStaleDeckRebuildLocked(npc, optimizerKey);
            }
            else if (!premadeRequest && TrySkipOptimizedDeckRebuildLocked(npc, regionMods))
            {
                return;
            }
            else if (premadeRequest && TrySkipPremadeDeckLocked(npc, regionMods))
            {
                return;
            }
        }

        if (TriadDeckOptimizerJobs.IsRunningForSessionKey(optimizerKey))
        {
            AnnounceOptimizerSkipOnce($"{optimizerKey}:running", $"[Saucy] Still optimizing deck for {npc.Name}...");
            return;
        }

        if (profileGS == null || profileGS.HasErrors)
        {
            AnnounceOptimizerSkipOnce($"{optimizerKey}:profile",
                $"[Saucy] Profile reader unavailable; optimizing deck for {npc.Name} (cannot save to profile).");
        }

        GameCardDB.Get().Refresh();

        if (PlayerSettingsDB.Get().ownedCards.Count == 0)
        {
            AnnounceOptimizerSkipOnce($"{optimizerKey}:no_cards",
                "[Saucy] Deck optimizer skipped: no owned cards in collection cache.");
            return;
        }

        if (Vnavmesh.ShouldDeferDeckOptimizerWork())
        {
            AnnounceOptimizerSkipOnce($"{optimizerKey}:vnav",
                "[Saucy] Waiting for vnavmesh before building deck…");
            return;
        }

        if (TriadMapNavigation.IsExecutingMultiAreaRoute)
        {
            AnnounceOptimizerSkipOnce($"{optimizerKey}:route",
                "[Saucy] Waiting for zone route before building deck…");
            return;
        }

        lastOptimizerSkipKey = string.Empty;

        PrintOptimizerChat($"[Saucy] Optimizing deck for {npc.Name}...");

        var regionModsForOptimizer = BuildRegionModsForOptimizer(npc, regionMods);
        var request = new TriadDeckOptimizerStartRequest(
            optimizerKey,
            npc,
            regionModsForOptimizer,
            UnlockedDeckSlots,
            navigationRequest,
            C.DeckOptimizerTimeoutMinutes);

        if (!TriadDeckOptimizerJobs.TryStart(request, out var passId, out var _))
        {
            return;
        }

        optimizerTimedOut = false;
        HasOptimizedDeckApplied = false;
        optimizerTargetDeckId = -1;
        optimizerSessionKey = optimizerKey;
        optimizerPassId = passId;
    }

    internal void OnDeckOptimizerJobFinished(TriadDeckOptimizerResult result)
    {
        if (result.PassId != optimizerPassId)
        {
            return;
        }

        lock (preGameLock)
        {
            if (result.PassId != optimizerPassId)
            {
                return;
            }

            if (result.UserCancelled)
            {
                MarkOptimizerPassFailedLocked(result);
                PrintOptimizerChat($"[Saucy] Deck optimization cancelled for {result.Npc?.Name ?? "NPC"}.");
                return;
            }

            if (result.Deck != null)
            {
                if (result.TimedOut || result.Aborted)
                {
                    PrintOptimizerChat(result.TimedOut
                        ? FormatOptimizerTimedOutFallbackMessage()
                        : FormatOptimizerAbortedFallbackMessage(), true);
                }

                if (result.Npc != null)
                {
                    preGameNpc = result.Npc;
                    lastGameNpc = result.Npc;
                }

                if (TriadUiState.IsBoardVisible() && !result.NavigationRequest)
                {
                    deferredPostMatchOptimizedDeck = result.Deck;
                    deferredPostMatchEstWinChance = result.BestEstWinChance;
                    return;
                }

                ApplyOptimizedDeckToProfileLocked(result.Deck, result.BestEstWinChance);
                return;
            }

            if (result.TimedOut || result.Aborted || result.Deck == null)
            {
                if (TryRestartNavigationDeckOptimizer(result))
                {
                    return;
                }

                MarkOptimizerPassFailedLocked(result);
                PrintOptimizerChat(result.TimedOut
                    ? FormatOptimizerTimedOutFallbackMessage()
                    : FormatOptimizerAbortedFallbackMessage(), true);
            }
        }
    }

    private static string BuildOptimizerSessionKey(TriadNpc npc, List<TriadGameModifier> regionMods) =>
        TriadOptimizerSessionKey.Build(npc, regionMods);

    private static TriadGameModifier[] BuildRegionModsForOptimizer(TriadNpc npc, List<TriadGameModifier> regionMods) =>
        TriadNpcSimulationRules.BuildRegionMods(npc, regionMods);

    private static bool ShouldDeferOptimizerStartUntilRules(TriadNpc npc, List<TriadGameModifier> regionMods)
    {
        if (regionMods.Count > 0 || npc?.Rules == null || npc.Rules.Count == 0)
        {
            return false;
        }

        return TriadUiState.IsMatchRegistrationVisible() || TriadUiState.IsPrepDeckSelectVisible();
    }

    private bool TrySkipOptimizedDeckRebuildLocked(TriadNpc npc, List<TriadGameModifier> regionMods)
    {
        if (npc == null)
        {
            return false;
        }

        var sessionKey = BuildOptimizerSessionKey(npc, regionMods);
        if (TriadOptimizedDeckCacheValidator.ShouldRebuildDeckForNewCards(sessionKey, npc.Id))
        {
            var newCards = TriadOptimizedDeckCacheValidator.CountNewOwnedCardsSinceBuild(sessionKey, npc.Id);
            PrepareStaleDeckRebuildLocked(npc, sessionKey);
            AnnounceOptimizerSkipOnce(
                $"{sessionKey}:new_cards",
                $"[Saucy] Rebuilding deck for {npc.Name} ({newCards} new cards since last build).");
            return false;
        }

        if (TryAdoptCachedDeckLocked(npc, regionMods, out var cachedMessage))
        {
            CancelOptimizerIfRunningAfterSkip();
            var cacheSkipKey = $"{BuildOptimizerSessionKey(npc, regionMods)}:cache";
            AnnounceOptimizerSkipOnce(cacheSkipKey, cachedMessage);
            return true;
        }

        if (TryAdoptExistingSaucyDeckLocked(npc, regionMods))
        {
            CancelOptimizerIfRunningAfterSkip();
            var skipKey = $"{BuildOptimizerSessionKey(npc, regionMods)}:profile";
            var message =
                $"[Saucy] Using existing optimized deck for {npc.Name} in profile slot {optimizerTargetDeckId + 1}.";
            AnnounceOptimizerSkipOnce(skipKey, message);
            return true;
        }

        // No cached or profile deck for this NPC/session — run the optimizer (and save to cache on success).
        return false;
    }

    private void CancelOptimizerIfRunningAfterSkip()
    {
        if (!OptimizerInProgress)
        {
            return;
        }

        CancelDeckOptimizerJob(userCancelled: true);
        optimizerTimedOut = false;
    }

    private bool TrySkipPremadeDeckLocked(TriadNpc npc, List<TriadGameModifier> regionMods)
    {
        var sessionKey = BuildOptimizerSessionKey(npc, regionMods);
        if (TriadOptimizedDeckCacheValidator.ShouldRebuildDeckForNewCards(sessionKey, npc.Id))
        {
            PrepareStaleDeckRebuildLocked(npc, sessionKey);
            return false;
        }

        if (TryAdoptCachedDeckLocked(npc, regionMods, out var cachedMessage))
        {
            TriadDeckLog.Print(cachedMessage);
            return true;
        }

        if (!TryAdoptExistingSaucyDeckLocked(npc, regionMods))
        {
            return false;
        }

        TriadDeckLog.Print(
            $"[Saucy] Using existing optimized deck for {npc.Name} in profile slot {optimizerTargetDeckId + 1}.");
        return true;
    }

    private bool TrySlotCachedDeckIntoProfileLocked(
        TriadNpc npc,
        List<TriadGameModifier> regionMods,
        out string message)
    {
        message = null;
        if (npc == null)
        {
            return false;
        }

        var sessionKey = BuildOptimizerSessionKey(npc, regionMods);
        if (string.Equals(cachedDeckSlottedSessionKey, sessionKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (TriadOptimizerSessionKey.ShouldSkipDeckCache(npc, regionMods))
        {
            return false;
        }

        if (TriadOptimizedDeckCacheValidator.ShouldRebuildDeckForNewCards(sessionKey, npc.Id))
        {
            PrepareStaleDeckRebuildLocked(npc, sessionKey);
            return false;
        }

        if (!TriadOptimizedDeckCacheValidator.TryGetUsableEntry(sessionKey, out var entry))
        {
            return false;
        }

        if (!TryApplyOptimizedDeckFromCardsLocked(
            npc,
            regionMods,
            entry.CardIds,
            out var solverDeck,
            out var targetDeckId,
            false))
        {
            TriadOptimizedDeckCacheStore.Remove(sessionKey);
            return false;
        }

        cachedDeckSlottedSessionKey = sessionKey;
        preGameNpc = npc;
        lastGameNpc = npc;

        var deckName = $"{npc.Name} (Saucy)";
        preGameDecks[targetDeckId] = new()
        {
            id = targetDeckId, name = deckName, solverDeck = solverDeck
        };
        DebugScreenMemory.UpdatePlayerDeck(solverDeck);

        message =
            $"[Saucy] Loaded cached deck into profile slot {targetDeckId + 1} for {npc.Name}.";
        Svc.Log.Info(message);
        return true;
    }

    private bool TryAdoptCachedDeckLocked(
        TriadNpc npc,
        List<TriadGameModifier> regionMods,
        out string message)
    {
        message = null;
        if (npc == null || !TrySlotCachedDeckIntoProfileLocked(npc, regionMods, out message))
        {
            return false;
        }

        var sessionKey = BuildOptimizerSessionKey(npc, regionMods);
        var targetDeckId = ResolveOptimizedDeckTargetSlotLocked();
        if (!preGameDecks.TryGetValue(targetDeckId, out var deckData) || deckData.solverDeck == null)
        {
            return false;
        }

        optimizerTargetDeckId = targetDeckId;
        HasOptimizedDeckApplied = true;
        preGameBestId = targetDeckId;
        optimizerSessionKey = sessionKey;
        optimizerTimedOut = false;
        ClearOptimizerStartBlockLocked();
        ScheduleOptimizedDeckPreviewEval(targetDeckId, deckData.solverDeck, npc, regionMods);

        message ??=
            $"[Saucy] Using cached deck for {npc.Name} in profile slot {targetDeckId + 1}.";
        Svc.Log.Info(message);

        return true;
    }

    private void MarkOptimizerPassFailedLocked(TriadDeckOptimizerResult result)
    {
        optimizerTimedOut = true;
        var npc = result.Npc ?? preGameNpc;
        if (npc == null)
        {
            return;
        }

        BlockOptimizerStartForSessionLocked(
            BuildOptimizerSessionKey(npc, preGameMods),
            OptimizerStartFailureCooldown);
    }

    private void RecordOptimizedDeckBuiltUtc(TriadNpc npc)
    {
        if (npc == null)
        {
            return;
        }

        C.TriadOptimizedDeckBuiltUtcTicksByNpcId[npc.Id] = DateTime.UtcNow.Ticks;
        PruneLegacyOptimizedDeckBuildTimestamps();
        C.Save();
    }

    private static void PruneLegacyOptimizedDeckBuildTimestamps()
    {
        if (C.TriadOptimizedDeckBuiltUtcTicksByNpcId.Count == 0)
        {
            return;
        }

        var cutoffTicks = DateTime.UtcNow.Subtract(OptimizedDeckRebuildCooldown).Ticks;
        var staleNpcIds = C.TriadOptimizedDeckBuiltUtcTicksByNpcId
            .Where(kvp => kvp.Value < cutoffTicks)
            .Select(kvp => kvp.Key)
            .ToList();

        if (staleNpcIds.Count == 0)
        {
            return;
        }

        foreach (var npcId in staleNpcIds)
        {
            C.TriadOptimizedDeckBuiltUtcTicksByNpcId.Remove(npcId);
        }

        C.Save();
    }

    private bool TryAdoptExistingSaucyDeckLocked(TriadNpc npc, List<TriadGameModifier> regionMods)
    {
        if (profileGS == null || profileGS.HasErrors || npc == null)
        {
            return false;
        }

        if (!TryFindSaucyDeckProfileSlot(npc, out var deckIdx))
        {
            return false;
        }

        var profileDecks = profileGS.GetPlayerDecks();
        if (profileDecks == null || deckIdx < 0 || deckIdx >= profileDecks.Length)
        {
            return false;
        }

        var profileDeck = profileDecks[deckIdx];
        if (profileDeck == null)
        {
            return false;
        }

        var parseCtx = new GameUIParser();
        var deckData = ParseDeckDataFromProfile(profileDeck, parseCtx);
        if (deckData?.solverDeck == null || deckData.solverDeck.GetDeckState() != ETriadDeckState.Valid)
        {
            return false;
        }

        optimizerTargetDeckId = deckIdx;
        HasOptimizedDeckApplied = true;
        preGameBestId = deckIdx;
        optimizerSessionKey = BuildOptimizerSessionKey(npc, regionMods);
        preGameDecks[deckIdx] = deckData;

        DebugScreenMemory.UpdatePlayerDeck(deckData.solverDeck);
        Svc.Log.Info($"[Saucy] Using existing profile deck \"{profileDeck.name}\" in slot {deckIdx + 1}.");
        if (TryExtractCardIdsFromProfileDeck(profileDeck, out var cardIds))
        {
            PersistGeneratedDeckCache(
                npc,
                regionMods,
                cardIds,
                TryGetEstWinChanceForDeck(npc, regionMods, deckIdx));
        }

        ScheduleOptimizedDeckPreviewEval(deckIdx, deckData.solverDeck, npc, regionMods);
        return true;
    }

    private bool TryFindSaucyDeckProfileSlot(TriadNpc npc, out int deckIdx)
    {
        deckIdx = -1;

        if (npc == null)
        {
            return false;
        }

        var expectedName = $"{npc.Name} (Saucy)";
        var profileDecks = profileGS?.GetPlayerDecks();
        if (profileDecks == null)
        {
            return false;
        }

        if (SaucyProfileDeckSlotIndex >= 0 &&
            SaucyProfileDeckSlotIndex < profileDecks.Length &&
            MatchesSaucyDeckName(profileDecks[SaucyProfileDeckSlotIndex], expectedName))
        {
            deckIdx = SaucyProfileDeckSlotIndex;
            return true;
        }

        for (var idx = 0; idx < profileDecks.Length; idx++)
        {
            if (idx == SaucyProfileDeckSlotIndex)
            {
                continue;
            }

            if (MatchesSaucyDeckName(profileDecks[idx], expectedName))
            {
                deckIdx = idx;
                return true;
            }
        }

        return false;
    }

    private static bool MatchesSaucyDeckName(TriadProfileDeckReader.PlayerDeck deck, string expectedName) =>
        deck != null &&
        !string.IsNullOrWhiteSpace(deck.name) &&
        deck.name.Equals(expectedName, StringComparison.OrdinalIgnoreCase);

    private void ApplyOptimizedDeckToProfileLocked(TriadDeck optimizedDeck, float? estWinChance = null)
    {
        if (preGameNpc == null)
        {
            return;
        }

        if (!TryExtractOptimizedCardIds(optimizedDeck, out var cardIds))
        {
            return;
        }

        if (!TryApplyOptimizedDeckFromCardsLocked(
            preGameNpc,
            preGameMods,
            cardIds,
            out var appliedDeck,
            out var targetDeckId,
            estWinChance: estWinChance))
        {
            PrintOptimizerChat("[Saucy] Failed to write optimized deck to profile.", true);
            return;
        }

        optimizerTargetDeckId = targetDeckId;
        HasOptimizedDeckApplied = true;
        preGameBestId = targetDeckId;
        optimizerSessionKey = BuildOptimizerSessionKey(preGameNpc, preGameMods);
        optimizerTimedOut = false;
        ClearOptimizerStartBlockLocked();
        navigationOptimizerRetryCount = 0;
        navigationOptimizerRetrySessionKey = string.Empty;
        RecordOptimizedDeckBuiltUtc(preGameNpc);

        var deckName = $"{preGameNpc.Name} (Saucy)";
        var deckData = new DeckData
        {
            id = targetDeckId, name = deckName, solverDeck = appliedDeck
        };
        preGameDecks[targetDeckId] = deckData;
        DebugScreenMemory.UpdatePlayerDeck(appliedDeck);

        PrintOptimizerChat(
            $"[Saucy] Optimized deck written to slot {targetDeckId + 1} for {preGameNpc.Name}.");

        ScheduleOptimizedDeckPreviewEval(targetDeckId, appliedDeck, preGameNpc, preGameMods);
        BeginDeckSelectPostWriteCooldown();
        Svc.Framework.Run(() => TriadDeckSelectAutomation.PrepareRetryWithOptimizedDeck(targetDeckId));
    }

    private static bool TryExtractCardIdsFromProfileDeck(TriadProfileDeckReader.PlayerDeck deck, out ushort[] cardIds)
    {
        cardIds = new ushort[TriadOptimizedDeckCacheEntry.DeckSize];
        if (deck?.cardIds == null || deck.cardIds.Length != TriadOptimizedDeckCacheEntry.DeckSize)
        {
            return false;
        }

        for (var idx = 0; idx < TriadOptimizedDeckCacheEntry.DeckSize; idx++)
        {
            if (deck.cardIds[idx] <= 0)
            {
                return false;
            }

            cardIds[idx] = deck.cardIds[idx];
        }

        return true;
    }

    private float? TryGetEstWinChanceForDeck(TriadNpc npc, List<TriadGameModifier> regionMods, int deckId)
    {
        var cacheKey = TriadEvalCacheKey.Build(npc, regionMods);
        if (string.IsNullOrEmpty(cacheKey) ||
            !npcEvalSnapshots.TryGetValue(cacheKey, out var deckMap) ||
            !deckMap.TryGetValue(deckId, out var deckData) ||
            deckData.chance.numGames <= 0)
        {
            return null;
        }

        return deckData.chance.winChance;
    }

    private static bool HasValidGeneratedDeckCardIds(ushort[] cardIds) =>
        cardIds != null &&
        cardIds.Length == TriadOptimizedDeckCacheEntry.DeckSize &&
        cardIds.All(id => id > 0);

    private bool TryExtractOptimizedCardIds(TriadDeck optimizedDeck, out ushort[] cardIds)
    {
        cardIds = new ushort[TriadOptimizedDeckCacheEntry.DeckSize];
        for (var idx = 0; idx < TriadOptimizedDeckCacheEntry.DeckSize; idx++)
        {
            var card = optimizedDeck?.knownCards?[idx];
            if (card == null || card.Id <= 0)
            {
                PrintOptimizerChat($"[Saucy] Deck optimizer deck has invalid card at slot {idx + 1}.", true);
                return false;
            }

            cardIds[idx] = (ushort)card.Id;
        }

        return true;
    }

    private bool TryApplyOptimizedDeckFromCardsLocked(
        TriadNpc npc,
        List<TriadGameModifier> regionMods,
        ushort[] cardIds,
        out TriadDeck solverDeck,
        out int targetDeckId,
        bool persistGeneratedDeckToCache = true,
        float? estWinChance = null)
    {
        solverDeck = null;
        targetDeckId = -1;

        if (npc == null || !TriadOptimizedDeckCacheValidator.TryBuildSolverDeck(cardIds, out solverDeck))
        {
            return false;
        }

        targetDeckId = ResolveOptimizedDeckTargetSlotLocked();
        if (targetDeckId < 0)
        {
            return false;
        }

        var deckName = $"{npc.Name} (Saucy)";
        if (profileGS == null || profileGS.HasErrors)
        {
            Svc.Log.Warning("[Saucy] Profile reader unavailable; using optimized deck in memory only.");
            if (persistGeneratedDeckToCache)
            {
                PersistGeneratedDeckCache(npc, regionMods, cardIds, estWinChance);
            }

            return true;
        }

        if (!profileGS.TryWritePlayerDeck(targetDeckId, cardIds, deckName))
        {
            if (persistGeneratedDeckToCache)
            {
                PersistGeneratedDeckCache(npc, regionMods, cardIds, estWinChance);
            }

            solverDeck = null;
            targetDeckId = -1;
            return false;
        }

        if (persistGeneratedDeckToCache)
        {
            PersistGeneratedDeckCache(npc, regionMods, cardIds, estWinChance);
        }

        return true;
    }

    private void TryRefreshGeneratedDeckCacheEntryLocked(
        TriadNpc npc,
        List<TriadGameModifier> regionMods,
        int deckId,
        TriadDeck deck,
        float winChance)
    {
        if (npc == null || deck == null || winChance <= 0f)
        {
            return;
        }

        lock (preGameLock)
        {
            if (!HasOptimizedDeckApplied ||
                optimizerTargetDeckId != deckId ||
                preGameNpc?.Id != npc.Id)
            {
                return;
            }
        }

        var sessionKey = BuildOptimizerSessionKey(npc, regionMods);
        if (TriadOptimizedDeckCacheStore.TryUpdateEstWinChance(sessionKey, winChance))
        {
            return;
        }

        if (!TryExtractOptimizedCardIds(deck, out var cardIds))
        {
            return;
        }

        PersistGeneratedDeckCache(npc, regionMods, cardIds, winChance);
    }

    private static void PersistGeneratedDeckCache(
        TriadNpc npc,
        List<TriadGameModifier> regionMods,
        ushort[] cardIds,
        float? estWinChance = null)
    {
        if (npc == null || !HasValidGeneratedDeckCardIds(cardIds))
        {
            return;
        }

        TriadOptimizedDeckCacheStore.Upsert(new()
        {
            SessionKey = BuildOptimizerSessionKey(npc, regionMods),
            NpcId = npc.Id,
            NpcName = npc.Name ?? string.Empty,
            CardIds = cardIds,
            BuiltUtcTicks = DateTime.UtcNow.Ticks,
            OwnedCardIdsAtBuild = TriadOptimizedDeckCacheValidator.CaptureOwnedCardSnapshot(),
            EstWinChance = estWinChance ?? 0f
        });
    }

    private void ScheduleOptimizedDeckPreviewEval(
        int deckId,
        TriadDeck deck,
        TriadNpc npc,
        List<TriadGameModifier> regionMods,
        bool forceResim = true)
    {
        if (deck == null || npc == null || deckId < 0)
        {
            return;
        }

        var cacheKey = TriadEvalCacheKey.Build(npc, regionMods);
        if (string.IsNullOrEmpty(cacheKey))
        {
            return;
        }

        DeckData deckData;
        lock (preGameLock)
        {
            if (!preGameDecks.TryGetValue(deckId, out deckData) || deckData == null)
            {
                deckData = new()
                {
                    id = deckId, name = $"{npc.Name} (Saucy)", solverDeck = deck
                };
                preGameDecks[deckId] = deckData;
            }
            else
            {
                deckData.solverDeck = deck;
            }
        }

        SchedulePreviewEvalForDeck(cacheKey, deckData, npc, regionMods, true, forceResim);
    }
}
