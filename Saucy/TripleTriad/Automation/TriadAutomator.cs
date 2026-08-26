using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using Saucy.Framework;
using Saucy.IPC;
namespace Saucy.TripleTriad;

internal static class TriadAutomator
{
    private const string SanityThrottleKey = "Saucy.TriadAutomator.SanityCheck";

    public static void RunModule()
    {
        TriadCardFarmSession.TickDisplaySync();

        TickNpcSanityCheck();
        if (!TriadRunSession.ModuleEnabled)
        {
            return;
        }

        TriadCardFarmSession.TickVerificationCycle();

        if (TriadRun.preGameDecks.Count > 0 && C.UseSimmedDeck)
        {
            var selectedDeck = C.SelectedDeckIndex;
            if (selectedDeck >= 0 && !TriadRun.preGameDecks.ContainsKey(selectedDeck))
            {
                C.SelectedDeckIndex = -1;
            }
        }

        if (TriadDeckSelectAutomation.TickIfOpen())
        {
            return;
        }

        if (TriadUiState.IsMatchRegistrationVisible())
        {
            TriadRematchAutomation.ClearRematchPending();
            TriadRunSession.EnsureAutomationSessionForMatchPrep();

            if (!TriadRunSession.ShouldContinue())
            {
                if (TriadRematchAutomation.PendingRegistrationDismiss)
                {
                    if (TriadMatchRegistrationAutomation.TryDismiss())
                    {
                        TriadRematchAutomation.ClearPendingRegistrationDismiss();
                    }
                }

                return;
            }

            TriadMatchRegistrationAutomation.Tick();
            return;
        }

        if (TriadBoardAutomation.Tick())
        {
            return;
        }

        if (TriadUiState.IsResultVisible())
        {
            TriadRematchAutomation.Tick();
            return;
        }

        if (TriadUiState.IsPrepDeckSelectVisible())
        {
            TriadRematchAutomation.ClearRematchPending();
            TriadRunSession.EnsureAutomationSessionForMatchPrep();
        }

        if (!TriadRunSession.ShouldContinue())
        {
            if (!TriadRunSession.ModuleEnabled)
            {
                return;
            }

            if (TriadRematchAutomation.PendingRegistrationDismiss && TriadUiState.IsMatchRegistrationVisible())
            {
                if (TriadMatchRegistrationAutomation.TryDismiss())
                {
                    TriadRematchAutomation.ClearPendingRegistrationDismiss();
                }
            }
        }
    }

    private static void TickNpcSanityCheck()
    {
        if (ShouldSkipNpcSanityCheck())
        {
            return;
        }

        if (!EzThrottler.Throttle(SanityThrottleKey, 1000))
        {
            return;
        }

        if (TriadNpcProximity.IsRelevantTriadNpcNearby())
        {
            return;
        }

        var npcName = TriadNpcProximity.ResolveTriadNpcForProximityCheck()?.Name;
        TriadRunSession.DisableModule(string.IsNullOrEmpty(npcName)
            ? "No Triple Triad NPC nearby. Move closer to the NPC you want to play."
            : $"No Triple Triad NPC nearby ({npcName}). Move closer to the NPC you want to play.");
    }

    private static bool ShouldSkipNpcSanityCheck()
    {
        if (!TriadRunSession.ModuleEnabled || !TriadRunSession.ShouldContinue())
        {
            return true;
        }

        if (TriadUiState.IsAutomationFlowActive())
        {
            return true;
        }

        if (TalkHelper.IsVisible() || SelectStringHelper.IsNpcListMenuVisible())
        {
            return true;
        }

        if (TriadCardFarmSession.HasPendingDrops() &&
            (TriadCardFarmSession.IsModeActive() || TriadCardFarmSession.SessionActive))
        {
            return true;
        }

        if (TriadRematchAutomation.RematchPending ||
            TriadRematchAutomation.PendingRegistrationDismiss ||
            TriadCardFarmSession.IsDropVerificationPending())
        {
            return true;
        }

        if (!Player.Available || Svc.Condition[ConditionFlag.BetweenAreas])
        {
            return true;
        }

        return Lifestream.IsBusyNow() || Vnavmesh.IsMoving() || TriadMapNavigation.IsNavigationActive;
    }
}
