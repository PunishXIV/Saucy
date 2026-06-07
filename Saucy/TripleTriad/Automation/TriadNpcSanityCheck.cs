using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using Saucy.Framework;
using Saucy.IPC;
namespace Saucy.TripleTriad;

internal static class TriadNpcSanityCheck
{
    private const string ThrottleKey = "Saucy.TriadAutomator.SanityCheck";

    public static void Tick()
    {
        if (ShouldSkip())
        {
            return;
        }

        if (!EzThrottler.Throttle(ThrottleKey, 1000))
        {
            return;
        }

        if (TriadNpcProximity.IsRelevantTriadNpcNearby())
        {
            return;
        }

        var npcName = TriadNpcProximity.ResolveTriadNpcForProximityCheck()?.Name;
        TriadRunSession.DisableModule(string.IsNullOrEmpty(npcName)
            ? "No Triple Triad NPC nearby (maybe get closer if in front of one)."
            : $"No Triple Triad NPC nearby ({npcName}). Maybe get closer if you're in front of one.");
    }

    private static bool ShouldSkip()
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

        if (Lifestream.IsBusyNow() || Vnavmesh.IsMoving() || TriadMapNavigation.IsNavigationActive)
        {
            return true;
        }

        return false;
    }
}
