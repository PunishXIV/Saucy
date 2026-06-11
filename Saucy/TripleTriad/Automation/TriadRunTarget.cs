using System;
namespace Saucy.TripleTriad;

public enum TriadRunMode
{
    None = -1,
    PlayXTimes = 0,
    PlayUntilAnyCard = 1,
    PlayUntilAllCards = 2
}

public enum TriadNavigationGoal
{
    FarmCards = 0,
    FarmMgp = 1
}

internal static class TriadTargetNpc
{
    public static TriadNpc? FromWorldTarget()
    {
        var name = Svc.Targets.Target?.Name.TextValue;
        return TriadNpcDB.Get().FindMatchingName(name);
    }

    public static TriadNpc? FromSolverContext() =>
        TriadRun.preGameNpc ?? TriadRun.currentNpc ?? TriadRun.lastGameNpc;

    public static TriadNpc? FromRunContext(GameNpcInfo? runTargetNpc)
    {
        if (runTargetNpc != null)
        {
            return TriadNpcDB.Get().FindByID(runTargetNpc.npcId);
        }

        return FromSolverContext();
    }
}

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
