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

        lock (preGameLock)
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
        lock (preGameLock)
        {
            ResetDeckOptimizer();
        }
    }

    private void ResetDeckOptimizer()
    {
        if (!OptimizerInProgress && !HasOptimizedDeckApplied && string.IsNullOrEmpty(cachedDeckSlottedSessionKey))
        {
            return;
        }

        TriadDeckOptimizerJobs.CancelActive();
        optimizerPassId++;
        optimizerTimedOut = false;
        HasOptimizedDeckApplied = false;
        optimizerTargetDeckId = -1;
        optimizerSessionKey = string.Empty;
        cachedDeckSlottedSessionKey = string.Empty;
        navigationOptimizerRetryCount = 0;
        navigationOptimizerRetrySessionKey = string.Empty;
    }

    private bool IsOptimizedDeckAppliedForSession(TriadNpc npc, List<TriadGameModifier> regionMods)
    {
        if (!HasOptimizedDeckApplied || optimizerTargetDeckId < 0 || npc == null)
        {
            return false;
        }

        return preGameNpc?.Id == npc.Id &&
               string.Equals(optimizerSessionKey, BuildOptimizerSessionKey(npc, regionMods), StringComparison.Ordinal);
    }

    private void InvalidateOptimizedDeckForRulesChange(TriadNpc npc, List<TriadGameModifier> newRegionMods)
    {
        if (npc == null)
        {
            return;
        }

        var newKey = BuildOptimizerSessionKey(npc, newRegionMods);
        if (!string.IsNullOrEmpty(optimizerSessionKey) &&
            string.Equals(optimizerSessionKey, newKey, StringComparison.Ordinal))
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
        optimizerTargetDeckId = -1;
        optimizerSessionKey = string.Empty;
        cachedDeckSlottedSessionKey = string.Empty;
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
            optimizerTimedOut = false;
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

        if (optimizerTimedOut)
        {
            return;
        }

        StartDeckOptimizer(preGameNpc, regionMods);
    }

    private bool IsOptimizerStartBlockedForSessionLocked(string sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey) ||
            string.IsNullOrEmpty(optimizerStartBlockedSessionKey) ||
            DateTime.UtcNow >= optimizerStartBlockedUntilUtc)
        {
            return false;
        }

        return string.Equals(optimizerStartBlockedSessionKey, sessionKey, StringComparison.Ordinal);
    }

    private void BlockOptimizerStartForSessionLocked(string sessionKey, TimeSpan cooldown)
    {
        if (string.IsNullOrEmpty(sessionKey))
        {
            return;
        }

        optimizerStartBlockedSessionKey = sessionKey;
        optimizerStartBlockedUntilUtc = DateTime.UtcNow + cooldown;
    }

    private void ClearOptimizerStartBlockLocked()
    {
        optimizerStartBlockedSessionKey = string.Empty;
        optimizerStartBlockedUntilUtc = DateTime.MinValue;
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

        if (string.Equals(cachedDeckSlottedSessionKey, sessionKey, StringComparison.Ordinal))
        {
            cachedDeckSlottedSessionKey = string.Empty;
        }

        if (HasOptimizedDeckApplied &&
            (string.IsNullOrEmpty(optimizerSessionKey) ||
             string.Equals(optimizerSessionKey, sessionKey, StringComparison.Ordinal)))
        {
            HasOptimizedDeckApplied = false;
            optimizerTargetDeckId = -1;
            optimizerSessionKey = string.Empty;
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
        lock (preGameLock)
        {
            previewEvalGeneration++;
            previewEvalInFlight.Clear();
            if (OptimizerInProgress && !HasOptimizedDeckApplied)
            {
                optimizerTimedOut = true;
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
        lock (preGameLock)
        {
            deck = deferredPostMatchOptimizedDeck;
            estWinChance = deferredPostMatchEstWinChance;
            deferredPostMatchOptimizedDeck = null;
            deferredPostMatchEstWinChance = null;
        }

        if (deck == null)
        {
            return;
        }

        lock (preGameLock)
        {
            ApplyOptimizedDeckToProfileLocked(deck, estWinChance);
        }
    }

    private void UpdateDeckOptimizerPause() =>
        TriadDeckOptimizerJobs.UpdatePause(
            pauseOptimizerForSolver ||
            pauseOptimizerForActiveTriad ||
            pauseOptimizerForNavmesh);

    public void SyncDeckOptimizerPauseForVnavmesh()
    {
        var shouldPause = Vnavmesh.ShouldDeferDeckOptimizerWork();
        lock (preGameLock)
        {
            if (pauseOptimizerForNavmesh == shouldPause)
            {
                return;
            }

            pauseOptimizerForNavmesh = shouldPause;
            UpdateDeckOptimizerPause();
        }
    }

    private bool IsDeckOptimizerBlockingLocked() =>
        OptimizerInProgress && !HasOptimizedDeckApplied && !optimizerTimedOut;

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
        if (lastOptimizerSkipKey == skipKey)
        {
            return;
        }

        lastOptimizerSkipKey = skipKey;
        PrintOptimizerChat(message);
    }
}
