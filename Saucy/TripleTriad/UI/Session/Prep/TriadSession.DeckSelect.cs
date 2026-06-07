#nullable disable
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.UI;

public partial class TriadSession
{
    private bool ShouldUseOptimizerDeckSelectFallbackLocked()
    {
        if (!ShouldBuildOptimizedDeck() || HasOptimizedDeckApplied)
        {
            return false;
        }

        return optimizerTimedOut;
    }

    public bool ShouldTryVisibleSaucyDeckRowSelect()
    {
        lock (preGameLock)
        {
            return ShouldBuildOptimizedDeck() &&
                   !ShouldUseOptimizerDeckSelectFallbackLocked() &&
                   !HasOptimizedDeckApplied;
        }
    }

    private void RefreshPreGameBestForFallbackLocked()
    {
        if (preGameNpc == null)
        {
            return;
        }

        var bestId = FindBestSimDeckIdLocked(preGameNpc, ResolvePreviewRulesForNpc(preGameNpc));
        if (bestId >= 0)
        {
            preGameBestId = bestId;
        }
    }

    private int ResolveAutoPickDeckIdLocked(TriadNpc npc)
    {
        if (ShouldBuildOptimizedDeck())
        {
            if (HasOptimizedDeckApplied && optimizerTargetDeckId >= 0)
            {
                return optimizerTargetDeckId;
            }

            if (ShouldUseOptimizerDeckSelectFallbackLocked())
            {
                RefreshPreGameBestForFallbackLocked();
                if (preGameBestId >= 0)
                {
                    return preGameBestId;
                }

                return FindBestSimDeckIdLocked(npc, ResolvePreviewRulesForNpc(npc));
            }

            return -1;
        }

        return FindBestSimDeckIdLocked(npc, ResolvePreviewRulesForNpc(npc));
    }

    public bool TryResolveAutoPickProfileDeckId(out int profileDeckId)
    {
        profileDeckId = -1;
        if (!C.UseSimmedDeck)
        {
            return false;
        }

        lock (preGameLock)
        {
            if (preGameNpc == null)
            {
                return false;
            }

            profileDeckId = ResolveAutoPickDeckIdLocked(preGameNpc);
            if (profileDeckId < 0 && ShouldUseOptimizerDeckSelectFallbackLocked())
            {
                RefreshPreGameBestForFallbackLocked();
            }

            if (profileDeckId < 0)
            {
                profileDeckId = ResolveAutoPickDeckIdLocked(preGameNpc);
            }

            if (profileDeckId < 0)
            {
                if (ShouldBuildOptimizedDeck() && !ShouldUseOptimizerDeckSelectFallbackLocked())
                {
                    return false;
                }

                profileDeckId = ResolveSelectableDeckIndexLocked(-1, true);
            }

            return profileDeckId >= 0;
        }
    }

    public bool TryResolveDeckListIndex(int profileDeckId, out int listIndex)
    {
        listIndex = -1;
        if (profileDeckId is < 0 or > 4)
        {
            return false;
        }

        uiReaderPrep.SyncDeckSelectFromLiveAddon();

        if (TryResolveDeckListIndexFromCache(profileDeckId, uiReaderPrep.cachedState.decks, out listIndex))
        {
            return true;
        }

        return false;
    }

    private bool TryResolveDeckListIndexFromCache(int profileDeckId, List<UIStateTriadPrepDeck> uiDecks, out int listIndex)
    {
        listIndex = -1;
        if (uiDecks.Count == 0)
        {
            return false;
        }

        string targetName = null;
        if (profileGS != null && !profileGS.HasErrors)
        {
            var profileDecks = profileGS.GetPlayerDecks();
            if (profileDecks != null && profileDeckId >= 0 && profileDeckId < profileDecks.Length)
            {
                targetName = profileDecks[profileDeckId]?.name;
            }
        }

        targetName ??= GetExpectedSaucyDeckName();

        if (!string.IsNullOrWhiteSpace(targetName))
        {
            var npcName = preGameNpc?.Name ?? string.Empty;
            for (var idx = 0; idx < uiDecks.Count; idx++)
            {
                if (TriadDeckNameHelper.RowMatchesNpc(uiDecks[idx].name, targetName, npcName))
                {
                    listIndex = idx;
                    return true;
                }
            }
        }

        for (var idx = 0; idx < uiDecks.Count; idx++)
        {
            if (uiDecks[idx].name.Contains("(Saucy)", StringComparison.OrdinalIgnoreCase))
            {
                listIndex = idx;
                return true;
            }
        }

        if (profileDeckId >= 0 && profileDeckId < uiDecks.Count)
        {
            listIndex = profileDeckId;
            return true;
        }

        return false;
    }

    public void TickDeckSelectPostWriteCooldown()
    {
        if (deckSelectPostWriteCooldownFrames > 0)
        {
            deckSelectPostWriteCooldownFrames--;
        }
    }

    public void BeginDeckSelectPostWriteCooldown() =>
        deckSelectPostWriteCooldownFrames = DeckSelectPostProfileWriteFrames;

    public bool IsDeckSelectPrepBlocking(bool autoPickDeck)
    {
        if (!autoPickDeck || TriadUiState.IsBoardVisible())
        {
            return false;
        }

        if (deckSelectPostWriteCooldownFrames > 0)
        {
            return true;
        }

        if (C.UseSimmedDeck && !ShouldBuildOptimizedDeck())
        {
            if (preGameNpc != null && IsPreviewEvalPendingForNpc(preGameNpc, preGameMods))
            {
                return true;
            }

            return false;
        }

        var kickPreviewEval = false;
        TriadNpc previewNpc = null;
        lock (preGameLock)
        {
            if (HasOptimizedDeckApplied && optimizerTargetDeckId >= 0)
            {
                return false;
            }

            if (IsDeckOptimizerBlockedByNavmesh())
            {
                return true;
            }

            if (ShouldUseOptimizerDeckSelectFallbackLocked())
            {
                kickPreviewEval = preGameNpc != null;
                previewNpc = preGameNpc;
            }
            else
            {
                return IsDeckOptimizerBlockingLocked();
            }
        }

        if (kickPreviewEval && previewNpc != null)
        {
            EnsurePreviewEvalForNpc(previewNpc, preGameMods);
            if (IsPreviewEvalPendingForNpc(previewNpc, preGameMods))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetDeckSelectCandidate(bool autoPickDeck, int manualDeckIndex, HashSet<int> excluded, out int deckIndex)
    {
        deckIndex = -1;
        if (IsDeckSelectPrepBlocking(autoPickDeck))
        {
            return false;
        }

        lock (preGameLock)
        {
            foreach (var candidate in GetOrderedDeckCandidatesLocked(autoPickDeck, manualDeckIndex))
            {
                if (excluded is { Count: > 0 } && excluded.Contains(candidate))
                {
                    continue;
                }

                if (IsDeckSelectableLocked(candidate))
                {
                    deckIndex = candidate;
                    return true;
                }
            }
        }

        return false;
    }
    private int ResolveOptimizedDeckTargetSlotLocked() => SaucyProfileDeckSlotIndex;

    private int ResolveSelectableDeckIndexLocked(int preferredDeckId, bool autoPickDeck)
    {
        foreach (var candidate in GetOrderedDeckCandidatesLocked(preferredDeckId, autoPickDeck))
        {
            if (IsDeckSelectableLocked(candidate))
            {
                return candidate;
            }
        }

        return -1;
    }

    private IEnumerable<int> GetOrderedDeckCandidatesLocked(bool autoPickDeck, int manualDeckIndex)
    {
        if (!autoPickDeck)
        {
            return GetOrderedDeckCandidatesLocked(manualDeckIndex, false);
        }

        return GetOrderedDeckCandidatesLocked(preGameBestId, true);
    }

    private IEnumerable<int> GetOrderedDeckCandidatesLocked(int preferredDeckId, bool autoPickDeck)
    {
        if (!autoPickDeck)
        {
            if (preferredDeckId >= 0)
            {
                yield return preferredDeckId;
            }

            yield break;
        }

        if (ShouldBuildOptimizedDeck())
        {
            if (HasOptimizedDeckApplied && optimizerTargetDeckId >= 0)
            {
                yield return optimizerTargetDeckId;
                yield break;
            }

            if (!ShouldUseOptimizerDeckSelectFallbackLocked())
            {
                yield break;
            }

            RefreshPreGameBestForFallbackLocked();
            preferredDeckId = preGameBestId;
        }

        if (preferredDeckId >= 0)
        {
            yield return preferredDeckId;
        }

        var rankedDecks = new List<KeyValuePair<int, DeckData>>();
        foreach (var kvp in preGameDecks)
        {
            if (IsDeckSimmableForPreview(kvp.Value))
            {
                rankedDecks.Add(kvp);
            }
        }

        rankedDecks.Sort((a, b) => b.Value.chance.score.CompareTo(a.Value.chance.score));
        foreach (var kvp in rankedDecks)
        {
            if (kvp.Key == preferredDeckId)
            {
                continue;
            }

            yield return kvp.Key;
        }

        for (var deckIdx = 0; deckIdx <= 4; deckIdx++)
        {
            if (deckIdx == preferredDeckId)
            {
                continue;
            }

            if (preGameDecks.ContainsKey(deckIdx))
            {
                continue;
            }

            if (IsProfileDeckComplete(deckIdx))
            {
                yield return deckIdx;
            }
        }
    }

    private bool IsProfileDeckComplete(int deckId)
    {
        if (profileGS == null || profileGS.HasErrors)
        {
            return false;
        }

        var profileDecks = profileGS.GetPlayerDecks();
        if (profileDecks == null || deckId < 0 || deckId >= profileDecks.Length)
        {
            return false;
        }

        var deck = profileDecks[deckId];
        if (deck == null)
        {
            return false;
        }

        for (var idx = 0; idx < 5; idx++)
        {
            if (deck.cardIds[idx] <= 0)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsDeckSelectableLocked(int deckId)
    {
        if (deckId is < 0 or > 4)
        {
            return false;
        }

        if (HasOptimizedDeckApplied && deckId == optimizerTargetDeckId)
        {
            return IsProfileDeckSelectable(deckId);
        }

        if (preGameDecks.TryGetValue(deckId, out var deckData))
        {
            if (IsDeckSimmableForPreview(deckData))
            {
                return true;
            }
        }

        if (IsProfileDeckComplete(deckId))
        {
            return true;
        }

        if (IsProfileDeckSelectable(deckId))
        {
            return true;
        }

        return false;
    }

    private bool IsProfileDeckSelectable(int deckId)
    {
        if (profileGS == null || profileGS.HasErrors)
        {
            return false;
        }

        var profileDecks = profileGS.GetPlayerDecks();
        return profileDecks != null && deckId >= 0 && deckId < profileDecks.Length && profileDecks[deckId] != null;
    }
}
