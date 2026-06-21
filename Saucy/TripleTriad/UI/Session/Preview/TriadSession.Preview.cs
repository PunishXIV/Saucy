#nullable disable
using Saucy.IPC;
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.UI;

public partial class TriadSession
{
    private int lastWorldTargetOptimizerNpcId = -1;

    public void ResetWorldTargetOptimizerTracking() => lastWorldTargetOptimizerNpcId = -1;

    private List<TriadGameModifier> ResolveRegionModsForNpc(TriadNpc npc)
    {
        if (npc == null)
        {
            return [];
        }

        SyncLivePrepNpcAndRulesIfVisible(npc);

        if (preGameNpc != null && preGameNpc.Id == npc.Id && preGameMods.Count > 0)
        {
            return preGameMods;
        }

        if (_rememberedRegionalModsByNpcId.TryGetValue(npc.Id, out var remembered) && remembered.Count > 0)
        {
            return remembered;
        }

        if (TriadOptimizedDeckCacheStore.TryGetRegionalMods(npc.Id, out var persisted) && persisted.Count > 0)
        {
            _rememberedRegionalModsByNpcId[npc.Id] = persisted;
            return persisted;
        }

        return [];
    }

    private IEnumerable<TriadGameModifier> ResolvePreviewRulesForNpc(TriadNpc npc) =>
        ResolveRegionModsForNpc(npc);

    private void RememberRegionalModsForNpc(TriadNpc npc, List<TriadGameModifier> regionMods)
    {
        if (npc == null || regionMods == null)
        {
            return;
        }

        var cloned = new List<TriadGameModifier>();
        foreach (var mod in regionMods)
        {
            if (mod is not null and not TriadGameModifierNone)
            {
                var clone = mod.Clone();
                if (clone != null)
                {
                    cloned.Add(clone);
                }
            }
        }

        if (cloned.Count == 0)
        {
            _rememberedRegionalModsByNpcId.Remove(npc.Id);
            TriadOptimizedDeckCacheStore.UpsertRegionalMods(npc.Id, cloned);
            return;
        }

        if (_rememberedRegionalModsByNpcId.TryGetValue(npc.Id, out var existing) &&
            TriadOptimizerSessionKey.RegionModsEqual(existing, cloned))
        {
            return;
        }

        _rememberedRegionalModsByNpcId[npc.Id] = cloned;
        TriadOptimizedDeckCacheStore.UpsertRegionalMods(npc.Id, cloned);
    }

    private void SyncLivePrepNpcAndRulesIfVisible(TriadNpc npc)
    {
        if (!TriadUiState.IsMatchRegistrationVisible() && !TriadUiState.IsPrepDeckSelectVisible())
        {
            return;
        }

        RefreshPrepRulesFromLive();

        if (string.IsNullOrWhiteSpace(uiReaderPrep.cachedState.npc))
        {
            return;
        }

        var parseCtx = new GameUIParser();
        var cachedNpc = parseCtx.ParseNpc(uiReaderPrep.cachedState.npc, false) ??
                        parseCtx.ParseNpcNameStart(uiReaderPrep.cachedState.npc, false);
        if (cachedNpc == null || cachedNpc.Id != npc.Id)
        {
            return;
        }

        TrySyncNpcFromPrepState(uiReaderPrep.cachedState, out var _);
    }

    public int CountSimmableProfileDecks()
    {
        EnsureCardOwnershipCache();

        if (profileGS == null || profileGS.HasErrors)
        {
            return 0;
        }

        var profileDecks = profileGS.GetPlayerDecks();
        if (profileDecks == null)
        {
            return 0;
        }

        var parseCtx = new GameUIParser();
        var count = 0;
        foreach (var _ in EnumerateSimmableProfileDecks(profileDecks, parseCtx))
        {
            count++;
        }

        return count;
    }

    public void TickWorldTargetDeckOptimizer()
    {
        if (!ShouldBuildOptimizedDeck() ||
            TriadMapNavigation.IsNavigationActive)
        {
            lastWorldTargetOptimizerNpcId = -1;
            return;
        }

        if (TriadUiState.IsBoardVisible() ||
            TriadUiState.IsPrepDeckSelectVisible() ||
            TriadUiState.IsMatchRegistrationVisible())
        {
            return;
        }

        var npc = TriadTargetNpc.FromWorldTarget();
        if (npc == null)
        {
            lastWorldTargetOptimizerNpcId = -1;
            return;
        }

        if (npc.Id == lastWorldTargetOptimizerNpcId)
        {
            return;
        }

        if (Vnavmesh.ShouldDeferHeavyWork())
        {
            return;
        }

        lastWorldTargetOptimizerNpcId = npc.Id;
        OnNpcSelected(npc, ResolveRegionModsForNpc(npc), true);
    }

    public DeckData GetDeckPreviewData(TriadNpc npc, int deckId)
    {
        if (npc == null)
        {
            return null;
        }

        lock (_preGameLock)
        {
            return TryGetDeckPreviewDataLocked(npc, deckId, ResolvePreviewRulesForNpc(npc));
        }
    }

    private DeckData TryGetDeckPreviewDataLocked(TriadNpc npc, int deckId, IEnumerable<TriadGameModifier> regionMods = null)
    {
        var cacheKey = TriadEvalCacheKey.Build(npc, regionMods ?? ResolvePreviewRulesForNpc(npc));
        if (npcEvalSnapshots.TryGetValue(cacheKey, out var deckMap) &&
            deckMap.TryGetValue(deckId, out var cached))
        {
            return cached;
        }

        return null;
    }

    private int FindBestSimDeckIdLocked(TriadNpc npc, IEnumerable<TriadGameModifier> regionMods)
    {
        var cacheKey = TriadEvalCacheKey.Build(npc, regionMods);
        if (string.IsNullOrEmpty(cacheKey) ||
            !npcEvalSnapshots.TryGetValue(cacheKey, out var deckMap))
        {
            return -1;
        }

        var bestId = -1;
        var bestScore = 0f;
        foreach (var kvp in deckMap)
        {
            if (kvp.Value.chance.numGames <= 0)
            {
                continue;
            }

            if (bestId < 0 || kvp.Value.chance.score > bestScore)
            {
                bestId = kvp.Key;
                bestScore = kvp.Value.chance.score;
            }
        }

        return bestId;
    }

    private void RefreshPreGameBestFromPreviewEval(TriadNpc npc, IEnumerable<TriadGameModifier> regionMods)
    {
        lock (_preGameLock)
        {
            if (ShouldBuildOptimizedDeck())
            {
                return;
            }

            var mods = regionMods as List<TriadGameModifier> ?? preGameMods;
            var bestId = FindBestSimDeckIdLocked(npc, mods);
            if (bestId >= 0)
            {
                preGameBestId = bestId;
            }
        }
    }
    public void EnsureOptimizedDeckPreviewEval(TriadNpc npc)
    {
        if (npc == null)
        {
            return;
        }

        lock (_preGameLock)
        {
            if (!HasOptimizedDeckApplied || _optimizerTargetDeckId < 0)
            {
                return;
            }

            if (!preGameDecks.TryGetValue(_optimizerTargetDeckId, out var deckData) ||
                deckData?.solverDeck == null)
            {
                return;
            }

            var forceResim = deckData.chance.numGames <= 0 ||
                             IsStaleZeroPreview(deckData.chance);

            ScheduleOptimizedDeckPreviewEval(
                _optimizerTargetDeckId,
                deckData.solverDeck,
                npc,
                preGameMods,
                forceResim);
        }
    }

    public void EnsurePreviewEvalForNpc(TriadNpc npc, IEnumerable<TriadGameModifier> regionMods = null)
    {
        if (npc == null || profileGS == null || profileGS.HasErrors || TriadUiState.IsBoardVisible())
        {
            return;
        }

        if (TriadMapNavigation.IsNavigationActive || Vnavmesh.ShouldDeferHeavyWork())
        {
            return;
        }

        EnsureCardOwnershipCache();

        var npcName = npc.Name;
        if (string.IsNullOrEmpty(npcName))
        {
            return;
        }

        var rules = regionMods ?? ResolvePreviewRulesForNpc(npc);
        var cacheKey = TriadEvalCacheKey.Build(npc, rules);
        var profileDecks = profileGS.GetPlayerDecks();
        if (profileDecks == null)
        {
            return;
        }

        var parseCtx = new GameUIParser();
        List<DeckData> decksToEval;
        lock (_preGameLock)
        {
            decksToEval = [.. EnumerateSimmableProfileDecks(profileDecks, parseCtx)];
        }

        foreach (var deckData in decksToEval)
        {
            SchedulePreviewEvalForDeck(cacheKey, deckData, npc, rules, true);
        }
    }

    public bool IsPreviewEvalPendingForNpc(TriadNpc npc, IEnumerable<TriadGameModifier> regionMods = null)
    {
        if (npc == null || profileGS == null || profileGS.HasErrors)
        {
            return false;
        }

        if (!dataLoader.IsDataReady)
        {
            return true;
        }

        var rules = regionMods ?? ResolvePreviewRulesForNpc(npc);
        var cacheKey = TriadEvalCacheKey.Build(npc, rules);
        if (string.IsNullOrEmpty(cacheKey))
        {
            return false;
        }

        lock (_preGameLock)
        {
            foreach (var flightKey in _previewEvalInFlight)
            {
                if (flightKey.StartsWith($"{cacheKey}:", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            var profileDecks = profileGS.GetPlayerDecks();
            if (profileDecks == null)
            {
                return false;
            }

            var parseCtx = new GameUIParser();
            foreach (var deckData in EnumerateSimmableProfileDecks(profileDecks, parseCtx))
            {
                npcEvalSnapshots.TryGetValue(cacheKey, out var deckMap);
                if (deckMap == null ||
                    !deckMap.TryGetValue(deckData.id, out var data) ||
                    data.chance.numGames <= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private IEnumerable<DeckData> EnumerateSimmableProfileDecks(
        TriadProfileDeckReader.PlayerDeck[] profileDecks,
        GameUIParser parseCtx)
    {
        if (profileDecks == null)
        {
            yield break;
        }

        foreach (var profileDeck in profileDecks)
        {
            if (profileDeck == null)
            {
                continue;
            }

            var deckData = ParseDeckDataFromProfile(profileDeck, parseCtx);
            parseCtx.Reset();
            if (!IsDeckSimmableForPreview(deckData))
            {
                continue;
            }

            yield return deckData;
        }
    }

    private static void EnsureCardOwnershipCache()
    {
        if (TriadMapNavigation.IsNavigationActive || Vnavmesh.ShouldDeferHeavyWork())
        {
            return;
        }

        GameCardDB.Get().Refresh();
    }

    private static bool IsDeckSimmableForPreview(DeckData deckData)
    {
        var deck = deckData?.solverDeck;
        if (deck?.knownCards == null || deck.knownCards.Count != 5)
        {
            return false;
        }

        for (var idx = 0; idx < 5; idx++)
        {
            var card = deck.knownCards[idx];
            if (card == null || !card.IsValid())
            {
                return false;
            }
        }

        return true;
    }

    private string DescribeMissingSimmableDecks()
    {
        if (profileGS == null || profileGS.HasErrors)
        {
            return "No usable decks";
        }

        var profileDecks = profileGS.GetPlayerDecks();
        if (profileDecks == null)
        {
            return "No usable decks";
        }

        var hasNamedDeck = false;
        var hasCompleteCardIds = false;
        foreach (var profileDeck in profileDecks)
        {
            if (profileDeck == null)
            {
                continue;
            }

            hasNamedDeck = true;
            var filledSlots = 0;
            for (var idx = 0; idx < 5; idx++)
            {
                if (profileDeck.cardIds[idx] > 0)
                {
                    filledSlots++;
                }
            }

            if (filledSlots == 5)
            {
                hasCompleteCardIds = true;
                break;
            }
        }

        if (!hasNamedDeck)
        {
            return "No usable decks";
        }

        return hasCompleteCardIds
            ? "No decks with 5 cards"
            : "Could not read profile decks";
    }
}
