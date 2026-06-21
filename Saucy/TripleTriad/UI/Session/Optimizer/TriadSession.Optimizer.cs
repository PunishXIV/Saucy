#nullable disable
using Saucy.IPC;
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.UI;

public partial class TriadSession
{
    private bool activeTriadBoardWorkSuspended;
    public void EnsureExistingSaucyDeckForPrep()
    {
        if (preGameNpc == null || TriadUiState.IsBoardVisible())
        {
            return;
        }

        if (!C.UseSimmedDeck)
        {
            return;
        }

        lock (_preGameLock)
        {
            var regionMods = ResolveRegionModsForNpc(preGameNpc);
            if (IsOptimizedDeckAppliedForSession(preGameNpc, regionMods))
            {
                return;
            }

            if (ShouldBuildOptimizedDeck())
            {
                TryEnsureOptimizedDeckForPrepLocked();
                return;
            }

            if (OptimizerInProgress)
            {
                CancelDeckOptimizerJob(userCancelled: true);
            }

            if (ShouldUseCachedOptimizedDeckIfAvailable())
            {
                TrySlotCachedDeckIntoProfileLocked(preGameNpc, regionMods, out var _);
            }

            EnsurePreviewEvalForNpc(preGameNpc, regionMods);
        }
    }

    public void ResetDeckOptimizerState()
    {
        lock (_preGameLock)
        {
            ResetDeckOptimizer();
        }
    }

    private void ResetDeckOptimizer()
    {
        if (!OptimizerInProgress && !HasOptimizedDeckApplied && string.IsNullOrEmpty(_cachedDeckSlottedSessionKey))
        {
            return;
        }

        TriadDeckOptimizerJobs.CancelActive();
        _optimizerPassId++;
        _optimizerTimedOut = false;
        HasOptimizedDeckApplied = false;
        _optimizerTargetDeckId = -1;
        _optimizerSessionKey = string.Empty;
        _cachedDeckSlottedSessionKey = string.Empty;
        _navigationOptimizerRetryCount = 0;
        _navigationOptimizerRetrySessionKey = string.Empty;
    }

    private bool IsOptimizedDeckAppliedForSession(TriadNpc npc, List<TriadGameModifier> regionMods)
    {
        if (!HasOptimizedDeckApplied || _optimizerTargetDeckId < 0 || npc == null)
        {
            return false;
        }

        return preGameNpc?.Id == npc.Id &&
               string.Equals(_optimizerSessionKey, BuildOptimizerSessionKey(npc, regionMods), StringComparison.Ordinal);
    }

    private void InvalidateOptimizedDeckForRulesChange(TriadNpc npc, List<TriadGameModifier> newRegionMods)
    {
        if (npc == null)
        {
            return;
        }

        var newKey = BuildOptimizerSessionKey(npc, newRegionMods);
        if (!string.IsNullOrEmpty(_optimizerSessionKey) &&
            string.Equals(_optimizerSessionKey, newKey, StringComparison.Ordinal))
        {
            return;
        }

        if (OptimizerInProgress &&
            TriadDeckOptimizerJobs.TryGetActiveForNpc(npc.Id, out var activeJob) &&
            !string.Equals(activeJob.SessionKey, newKey, StringComparison.Ordinal))
        {
            CancelDeckOptimizerJob(userCancelled: true);
        }

        HasOptimizedDeckApplied = false;
        _optimizerTargetDeckId = -1;
        _optimizerSessionKey = string.Empty;
        _cachedDeckSlottedSessionKey = string.Empty;
        preGameBestId = -1;
    }

    private void TryEnsureOptimizedDeckForPrepLocked()
    {
        var regionMods = ResolveRegionModsForNpc(preGameNpc);
        var sessionKey = BuildOptimizerSessionKey(preGameNpc, regionMods);
        if (IsOptimizedDeckAppliedForSession(preGameNpc, regionMods))
        {
            return;
        }

        if (TrySkipOptimizedDeckRebuildLocked(preGameNpc, regionMods))
        {
            _optimizerTimedOut = false;
            ClearOptimizerStartBlockLocked();
            return;
        }

        if (OptimizerInProgress)
        {
            return;
        }

        if (IsOptimizerStartBlockedForSessionLocked(sessionKey))
        {
            return;
        }

        if (_optimizerTimedOut)
        {
            return;
        }

        StartDeckOptimizer(preGameNpc, regionMods);
    }

    private bool IsOptimizerStartBlockedForSessionLocked(string sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey) ||
            string.IsNullOrEmpty(_optimizerStartBlockedSessionKey) ||
            DateTime.UtcNow >= _optimizerStartBlockedUntilUtc)
        {
            return false;
        }

        return string.Equals(_optimizerStartBlockedSessionKey, sessionKey, StringComparison.Ordinal);
    }

    private void BlockOptimizerStartForSessionLocked(string sessionKey, TimeSpan cooldown)
    {
        if (string.IsNullOrEmpty(sessionKey))
        {
            return;
        }

        _optimizerStartBlockedSessionKey = sessionKey;
        _optimizerStartBlockedUntilUtc = DateTime.UtcNow + cooldown;
    }

    private void ClearOptimizerStartBlockLocked()
    {
        _optimizerStartBlockedSessionKey = string.Empty;
        _optimizerStartBlockedUntilUtc = DateTime.MinValue;
    }

    private void PrepareStaleDeckRebuildLocked(TriadNpc npc, string sessionKey)
    {
        if (npc == null)
        {
            return;
        }

        TriadOptimizedDeckCacheStore.RemoveAllForNpc(npc.Id);

        if (preGameNpc?.Id != npc.Id)
        {
            return;
        }

        if (string.Equals(_cachedDeckSlottedSessionKey, sessionKey, StringComparison.Ordinal))
        {
            _cachedDeckSlottedSessionKey = string.Empty;
        }

        if (HasOptimizedDeckApplied &&
            (string.IsNullOrEmpty(_optimizerSessionKey) ||
             string.Equals(_optimizerSessionKey, sessionKey, StringComparison.Ordinal)))
        {
            HasOptimizedDeckApplied = false;
            _optimizerTargetDeckId = -1;
            _optimizerSessionKey = string.Empty;
            preGameBestId = -1;
        }
    }

    private bool IsDeckEvalDataChanged(TriadNpc testNpc, List<TriadGameModifier> testMods, Dictionary<int, DeckData> testDecks)
    {
        if (testNpc != preGameNpc)
        {
            return true;
        }

        if (testMods.Count != preGameMods.Count)
        {
            return true;
        }

        for (var idx = 0; idx < testMods.Count; idx++)
        {
            if (testMods[idx] != preGameMods[idx])
            {
                return true;
            }
        }

        if (testDecks.Count != preGameDecks.Count)
        {
            return true;
        }

        foreach (var kvp in testDecks)
        {
            if (!preGameDecks.TryGetValue(kvp.Key, out var deckData))
            {
                return true;
            }

            if (!deckData.solverDeck.Equals(kvp.Value.solverDeck))
            {
                return true;
            }
        }

        return false;
    }

    private void SuspendBackgroundDeckWorkForActiveMatch()
    {
        lock (_preGameLock)
        {
            _previewEvalGeneration++;
            _previewEvalInFlight.Clear();
            if (OptimizerInProgress && !HasOptimizedDeckApplied)
            {
                _optimizerTimedOut = true;
            }
        }

        if (OptimizerInProgress)
        {
            CancelDeckOptimizerJob(false, true);
        }
    }

    private void FlushDeferredOptimizerProfileWrite()
    {
        TriadDeck deck;
        float? estWinChance;
        lock (_preGameLock)
        {
            deck = _deferredPostMatchOptimizedDeck;
            estWinChance = _deferredPostMatchEstWinChance;
            _deferredPostMatchOptimizedDeck = null;
            _deferredPostMatchEstWinChance = null;
        }

        if (deck == null)
        {
            return;
        }

        lock (_preGameLock)
        {
            ApplyOptimizedDeckToProfileLocked(deck, estWinChance);
        }
    }

    private void UpdateDeckOptimizerPause() =>
        TriadDeckOptimizerJobs.UpdatePause(
            _pauseOptimizerForSolver ||
            _pauseOptimizerForActiveTriad ||
            _pauseOptimizerForNavmesh);

    public void SyncDeckOptimizerPauseForVnavmesh()
    {
        var shouldPause = Vnavmesh.ShouldDeferDeckOptimizerWork();
        lock (_preGameLock)
        {
            if (_pauseOptimizerForNavmesh == shouldPause)
            {
                return;
            }

            _pauseOptimizerForNavmesh = shouldPause;
            UpdateDeckOptimizerPause();
        }
    }

    private bool IsDeckOptimizerBlockingLocked() =>
        OptimizerInProgress && !HasOptimizedDeckApplied && !_optimizerTimedOut;

    private bool IsDeckOptimizerBlockedByNavmesh() =>
        ShouldBuildOptimizedDeck() &&
        !HasOptimizedDeckApplied &&
        Vnavmesh.ShouldDeferDeckOptimizerWork();

    private static void PrintOptimizerChat(string message, bool force = false)
    {
        if (!force && !C.ShowOptimizerChatSpam)
        {
            return;
        }

        TriadDeckLog.Print(message);
    }

    private void AnnounceOptimizerSkipOnce(string skipKey, string message)
    {
        Svc.Log.Info(message);
        if (_lastOptimizerSkipKey == skipKey)
        {
            return;
        }

        _lastOptimizerSkipKey = skipKey;
        PrintOptimizerChat(message);
    }
}
