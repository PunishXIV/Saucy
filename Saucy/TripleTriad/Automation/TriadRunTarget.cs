using System;
namespace Saucy.TripleTriad;

internal static class TriadRunTarget
{
    public static GameNpcInfo? Resolve()
    {
        TriadRun.EnsureRunTargetNpcSynced();

        var npcId = TriadRun.preGameNpc?.Id ?? TriadRun.currentNpc?.Id ?? TriadRun.lastGameNpc?.Id ?? -1;
        if (npcId >= 0 && GameNpcDB.Get().mapNpcs.TryGetValue(npcId, out var npcInfo))
        {
            if (TriadRunSession.PlayUntilAllCardsDropOnce)
            {
                TriadCardFarmSession.SyncDisplay(npcInfo);
            }

            return npcInfo;
        }

        return null;
    }

    public static void RefreshFromPrep()
    {
        if (!TriadRunSession.PlayUntilAllCardsDropOnce)
        {
            return;
        }

        try
        {
            if (TriadUiState.IsMatchRegistrationVisible())
            {
                uiReaderPrep.SyncMatchRegistrationFromLiveAddon();
            }
            else if (TriadUiState.IsPrepDeckSelectVisible())
            {
                uiReaderPrep.SyncDeckSelectFromLiveAddon();
            }

            TriadRun.EnsureRunTargetNpcSynced(
                deckSelectScreen: uiReaderPrep.HasDeckSelectionUI && !TriadUiState.IsMatchRegistrationVisible());
            TriadCardFarmSession.SyncDisplay(Resolve());
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadRunTarget] RefreshFromPrep failed");
        }
    }
}
