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
                TrySlotCachedDeckIntoProfileLocked(preGameNpc, preGameMods, out var _);
            }

            EnsurePreviewEvalForNpc(preGameNpc, preGameMods);
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

    private void TryEnsureOptimizedDeckForPrepLocked()
    {
        var sessionKey = BuildOptimizerSessionKey(preGameNpc, preGameMods);
        if (HasOptimizedDeckApplied &&
            optimizerTargetDeckId >= 0 &&
            string.Equals(optimizerSessionKey, sessionKey, StringComparison.Ordinal))
        {
            return;
        }

        if (TrySkipOptimizedDeckRebuildLocked(preGameNpc, preGameMods))
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

        StartDeckOptimizer(preGameNpc, preGameMods);
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

    public void SetNavigationOptimizerPause(bool paused)
    {
        lock (preGameLock)
        {
            if (pauseOptimizerForNavmesh == paused)
            {
                return;
            }

            pauseOptimizerForNavmesh = paused;
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
