using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using Saucy.Framework;
using Saucy.OtherGames;
using System;
namespace Saucy.TripleTriad;

internal static class TriadRunSession
{
    public static bool ModuleEnabled;

    private static bool stopAfterCurrentForDutyFinder;

    public static int NumberOfTimes = 1;

    public static int MatchesCompletedThisSession;

    public static int SessionInitialPlayCount = 1;

    public static bool PlayXTimes => C.TriadRunMode == TriadRunMode.PlayXTimes;

    public static bool PlayUntilCardDrops => C.TriadRunMode == TriadRunMode.PlayUntilAnyCard;

    public static bool PlayUntilAllCardsDropOnce => C.TriadRunMode == TriadRunMode.PlayUntilAllCards;

    public static bool NoRunModeSelected => C.TriadRunMode == TriadRunMode.None;

    public static bool NavigationRequiresOptimizedDeckBuild { get; private set; }

    public static bool NavigationInfiniteRematch { get; private set; }

    internal static bool PlayUntilAnyCardDropped => TriadRewardDropTracker.PlayUntilAnyCardDropped;

    public static void ClearNavigationDeckBuildOverride()
    {
        NavigationRequiresOptimizedDeckBuild = false;
        NavigationInfiniteRematch = false;
    }

    public static void ResetRunModeForPluginLoad() =>
        ApplyRunMode(TriadRunMode.None, persist: false);

    public static void ApplyRunMode(
        TriadRunMode mode,
        GameNpcInfo? runTargetNpc = null,
        int? matchCount = null,
        bool persist = true,
        bool fromMapNavigation = false)
    {
        if (!fromMapNavigation)
        {
            NavigationRequiresOptimizedDeckBuild = false;
        }

        C.TriadRunMode = mode;

        switch (mode)
        {
            case TriadRunMode.PlayXTimes:
            case TriadRunMode.PlayUntilAnyCard:
                TriadCardFarmSession.DeactivateSession(clearProgress: true);
                if (mode == TriadRunMode.PlayXTimes)
                {
                    NumberOfTimes = Math.Max(1, matchCount ?? C.TriadMatchCount);
                }
                else if (NumberOfTimes <= 0)
                {
                    NumberOfTimes = 1;
                }

                break;
            case TriadRunMode.PlayUntilAllCards:
                NumberOfTimes = 1;
                if (fromMapNavigation)
                {
                    TriadCardFarmSession.DeactivateSession(clearProgress: true);
                }
                else if (ModuleEnabled)
                {
                    TriadCardFarmSession.ActivateSession(runTargetNpc ?? TriadRunTarget.Resolve(), true);
                }
                else
                {
                    TriadCardFarmSession.ClearProgress();
                }

                break;
            case TriadRunMode.None:
                TriadCardFarmSession.DeactivateSession(clearProgress: true);
                TriadRewardDropTracker.ResetSessionFlag();
                break;
        }

        if (persist && mode == TriadRunMode.PlayXTimes)
        {
            C.TriadMatchCount = NumberOfTimes;
            C.Save();
        }

        OnRunModeSettingsChanged();
    }

    public static void EnableFromNavigation()
    {
        GoldSaucerArcadeMachineHelper.DisableConflictingModules();
        ModuleEnabled = true;
        BeginAutomationSession();
    }

    public static void PrepareNavigationRunMode(TriadNpc npc, TriadNavigationGoal goal)
    {
        ModuleEnabled = false;
        NavigationRequiresOptimizedDeckBuild = false;
        NavigationInfiniteRematch = false;
        if (TriadNpcUnlockHelper.TryReject(npc, out var _))
        {
            TriadMapNavigation.CancelActiveNavigation();
            return;
        }

        GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out var npcInfo);

        if (goal == TriadNavigationGoal.FarmCards &&
            npcInfo != null &&
            TriadCardFarmSession.HasAllNpcRewardsOwned(npcInfo))
        {
            Svc.Chat.Print($"[Saucy] You already have every card from {npc.Name}. Farming MGP instead.");
            goal = TriadNavigationGoal.FarmMgp;
        }

        switch (goal)
        {
            case TriadNavigationGoal.FarmCards:
                ApplyRunMode(TriadRunMode.PlayUntilAllCards, npcInfo, fromMapNavigation: true);
                C.OnlyUnobtainedCards = true;
                C.Save();
                NavigationRequiresOptimizedDeckBuild = true;
                break;
            case TriadNavigationGoal.FarmMgp:
                TriadCardFarmSession.DeactivateSession(clearProgress: true);
                TriadRewardDropTracker.ResetSessionFlag();
                NavigationInfiniteRematch = true;
                // Left-click starts as card farm; MGP fallback must drop PlayUntilAllCards so farm mode/UI don't stick.
                ApplyRunMode(TriadRunMode.None, persist: false, fromMapNavigation: true);
                break;
        }
    }

    public static void BeginAutomationSession()
    {
        MatchesCompletedThisSession = 0;
        TriadRewardDropTracker.ResetSessionFlag();
        if (NavigationInfiniteRematch)
        {
            TriadCardFarmSession.DeactivateSession();
        }
        else if (PlayXTimes)
        {
            SyncPlayXTimesSession(Math.Max(1, C.TriadMatchCount));
        }
        else if (PlayUntilAllCardsDropOnce)
        {
            if (NumberOfTimes <= 0)
            {
                NumberOfTimes = 1;
            }

            TriadRun.EnsureRunTargetNpcSynced();
            TriadRunTarget.RefreshFromPrep();
            TriadCardFarmSession.ActivateSession(TriadRunTarget.Resolve(), true);
        }
        else
        {
            TriadCardFarmSession.DeactivateSession();
        }

        TriadRematchAutomation.ResetSessionFlags();
        TriadRewardDropTracker.ResetSnapshot();
        TriadDeckSelectAutomation.ResetSession();
        TriadMatchRegistrationAutomation.ResetSession();

        if (ModuleEnabled)
        {
            TriadRun.KickAutomationDeckOptimizer();
        }
    }

    public static void SyncPlayXTimesSession(int desiredPlayCount, bool persist = false)
    {
        if (!PlayXTimes || PlayUntilAllCardsDropOnce || TriadCardFarmSession.SessionActive)
        {
            return;
        }

        SessionInitialPlayCount = Math.Max(1, desiredPlayCount);

        if (TriadUiState.IsResultVisible())
        {
            MatchesCompletedThisSession = Math.Max(MatchesCompletedThisSession, 1);
        }

        NumberOfTimes = Math.Max(0, SessionInitialPlayCount - MatchesCompletedThisSession);

        if (persist)
        {
            C.TriadMatchCount = SessionInitialPlayCount;
            C.Save();
        }

        if (!ShouldContinue())
        {
            TriadRematchAutomation.RequestSessionEndDismiss();
        }
    }

    public static void OnRunModeSettingsChanged()
    {
        if (PlayXTimes)
        {
            SyncPlayXTimesSession(NumberOfTimes);
        }
        else if (PlayUntilCardDrops)
        {
            TriadRewardDropTracker.ResetSessionFlag();
            if (NumberOfTimes <= 0)
            {
                NumberOfTimes = 1;
            }
        }
        else
        {
            TriadRewardDropTracker.ResetSessionFlag();
        }

        if (PlayUntilAllCardsDropOnce)
        {
            if (TriadCardFarmSession.HasPendingDrops())
            {
                TriadRematchAutomation.CancelSessionEndDismissRequest();
            }
            else if (TriadUiState.IsResultVisible() && TriadCardFarmSession.IsComplete())
            {
                TriadCardFarmSession.DeactivateSession();
                TriadRematchAutomation.RequestSessionEndDismiss();
            }

            return;
        }

        if (TriadCardFarmSession.SessionActive)
        {
            TriadCardFarmSession.DeactivateSession();
        }

        if (!ShouldContinue())
        {
            TriadRematchAutomation.RequestSessionEndDismiss();
        }
    }

    public static void EnsureAutomationSessionForMatchPrep()
    {
        if (!ModuleEnabled || !PlayXTimes)
        {
            return;
        }

        if (NumberOfTimes > 0 && MatchesCompletedThisSession < SessionInitialPlayCount)
        {
            return;
        }

        var playCount = SessionInitialPlayCount > 0 ? SessionInitialPlayCount : 1;
        SessionInitialPlayCount = playCount;
        NumberOfTimes = playCount;
        MatchesCompletedThisSession = 0;
        TriadRematchAutomation.ClearRematchPending();
        TriadDeckSelectAutomation.ResetSession();
    }

    public static void DisableModule(string warning)
    {
        DuoLog.Warning(warning);
        StopAllAutomation(announce: false);
    }

    public static void StopAllAutomation(bool announce = true)
    {
        GateShared.StopAll();
        TriadMapNavigation.CancelActiveNavigation();
        ModuleEnabled = false;
        ClearDutyFinderDefer();
        ClearNavigationDeckBuildOverride();
        TriadCardFarmSession.DeactivateSession(clearProgress: true);
        TriadRematchAutomation.ClearRematchPending();
        TriadRematchAutomation.ClearPendingRegistrationDismiss();
        TriadRematchAutomation.ResetSessionFlags();
        TriadDeckSelectAutomation.ResetSession();
        TriadMatchRegistrationAutomation.ResetSession();
        TriadRewardDropTracker.ResetSessionFlag();
        TriadDeckOptimizerJobs.CancelActive(userCancelled: true);
        if (announce)
        {
            Svc.Chat.Print("[Saucy] Stopped navigation, travel, and triad automation.");
        }
    }

    public static void ApplyDutyFinderDeferIfNeeded()
    {
        if (!Svc.Condition[ConditionFlag.WaitingForDutyFinder] || !ModuleEnabled)
        {
            return;
        }

        stopAfterCurrentForDutyFinder = true;

        if (PlayXTimes)
        {
            NumberOfTimes = 0;
        }

        if (TriadMapNavigation.IsNavigationActive)
        {
            TriadMapNavigation.CancelActiveNavigation();
        }

        if (TriadUiState.IsResultVisible())
        {
            TriadRematchAutomation.RequestSessionEndDismiss();
        }
    }

    public static void ClearDutyFinderDefer() => stopAfterCurrentForDutyFinder = false;

    public static bool ShouldContinue()
    {
        if (stopAfterCurrentForDutyFinder)
        {
            return false;
        }

        if (NavigationInfiniteRematch)
        {
            return ModuleEnabled;
        }

        if (TriadCardFarmSession.IsModeActive())
        {
            return !TriadCardFarmSession.IsComplete();
        }

        if (NoRunModeSelected)
        {
            return ModuleEnabled;
        }

        if (PlayUntilCardDrops)
        {
            return ModuleEnabled && !TriadRewardDropTracker.PlayUntilAnyCardDropped;
        }

        if (PlayXTimes && NumberOfTimes <= 0)
        {
            return false;
        }

        if (PlayXTimes && MatchesCompletedThisSession >= SessionInitialPlayCount)
        {
            return false;
        }

        return ModuleEnabled;
    }

    public static bool CanFinalize() =>
        !ShouldContinue() &&
        !TriadMapNavigation.IsNavigationActive &&
        !TriadUiState.IsBoardVisible() &&
        !TriadUiState.IsResultVisible() &&
        !TriadUiState.IsMatchRegistrationVisible() &&
        !TriadUiState.IsPrepDeckSelectVisible();

    public static bool ShouldPlayRunCompleteSound() =>
        MatchesCompletedThisSession > 0 ||
        TriadCardFarmSession.GetCompletedCount() > 0 ||
        PlayUntilAnyCardDropped;

    public static bool Logout()
    {
        if (!Svc.Condition.Any())
        {
            return true;
        }

        Chat.SendMessage("/logout");
        return true;
    }

    public static bool SelectYesLogout() => SelectYesnoHelper.TryPressArmedYes();
}
