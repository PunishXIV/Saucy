#nullable disable
using Saucy.IPC;
using System;
namespace Saucy.TripleTriad.UI;

public partial class TriadSession
{
    public bool IsNavigationBlockedWaitingForOptimizer(TriadNpc npc)
    {
        if (!ShouldBuildOptimizedDeck() || npc == null)
        {
            return false;
        }

        EnsureNavigationDeckOptimizerStarted(npc);

        lock (preGameLock)
        {
            var sessionKey = BuildOptimizerSessionKey(npc, preGameMods);
            if (HasOptimizedDeckApplied && optimizerSessionKey == sessionKey)
            {
                return false;
            }
        }

        return OptimizerInProgress;
    }

    private void EnsureNavigationDeckOptimizerStarted(TriadNpc npc)
    {
        if (TriadNpcUnlockHelper.TryReject(npc, out var _))
        {
            TriadMapNavigation.CancelActiveNavigation();
            return;
        }

        if (!ShouldBuildOptimizedDeck())
        {
            return;
        }

        if (TriadMapNavigation.IsExecutingMultiAreaRoute ||
            (TriadMapNavigation.IsNavigationActive &&
             !TriadMapNavigation.IsInNavigationTargetTerritory()) ||
            Vnavmesh.ShouldDeferDeckOptimizerWork())
        {
            return;
        }

        if (preGameNpc?.Id != npc.Id)
        {
            OnNpcSelected(npc, [], true, true);
            return;
        }

        lock (preGameLock)
        {
            if (OptimizerInProgress)
            {
                return;
            }
            var sessionKey = BuildOptimizerSessionKey(npc, preGameMods);
            if (HasOptimizedDeckApplied && optimizerSessionKey == sessionKey)
            {
                return;
            }

            StartDeckOptimizer(npc, preGameMods, navigationRequest: true);
        }
    }

    private bool TryRestartNavigationDeckOptimizer(TriadDeckOptimizerResult result)
    {
        if (!result.NavigationRequest ||
            !TriadMapNavigation.IsNavigationActive ||
            result.Npc == null ||
            !ShouldBuildOptimizedDeck())
        {
            return false;
        }

        var sessionKey = BuildOptimizerSessionKey(result.Npc, preGameMods);
        if (!string.Equals(navigationOptimizerRetrySessionKey, sessionKey, StringComparison.Ordinal))
        {
            navigationOptimizerRetrySessionKey = sessionKey;
            navigationOptimizerRetryCount = 0;
        }

        if (navigationOptimizerRetryCount >= MaxNavigationOptimizerRetries)
        {
            return false;
        }

        navigationOptimizerRetryCount++;
        PrintOptimizerChat(
            $"[Saucy] Deck optimization interrupted for {result.Npc.Name}; retry {navigationOptimizerRetryCount}/{MaxNavigationOptimizerRetries}…");
        optimizerTimedOut = false;
        StartDeckOptimizer(result.Npc, preGameMods, navigationRequest: true);
        return true;
    }
}
