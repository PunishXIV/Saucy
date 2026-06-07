using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Saucy.Framework;
namespace Saucy.TripleTriad;

internal static unsafe class TriadDialogueSkip
{
    private const string TalkThrottleKey = "Saucy.TriadTalk";

    private static bool talkListenerRegistered;

    public static void EnsureTalkListener()
    {
        if (talkListenerRegistered)
        {
            return;
        }

        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnTalkUpdate);
        talkListenerRegistered = true;
    }

    public static bool ShouldRun()
    {
        if (IsGoldSaucerMinigameOccupied())
        {
            return false;
        }

        TriadNpcGate.SyncTrackedNpc();
        TriadNpcGate.RefreshDialogueFlow();

        if (TriadMapNavigation.IsAwaitingTriadStartDialog())
        {
            return true;
        }

        if (!TriadRunSession.ModuleEnabled && !TriadCardFarmSession.SessionActive)
        {
            return false;
        }

        if (TriadUiState.IsAutomationFlowActive() || TriadCardFarmSession.SessionActive)
        {
            return true;
        }

        if (IsTriadSessionUiVisible())
        {
            return true;
        }

        if (CanAutomateTalk())
        {
            return true;
        }

        if (Svc.Condition[ConditionFlag.OccupiedInQuestEvent])
        {
            return false;
        }

        return TriadNpcGate.HasInitiatedDialogue() || TriadNpcGate.IsInDialogueFlow();
    }

    public static bool IsBlockingTriadSessionEnd()
    {
        if (IsGoldSaucerMinigameOccupied())
        {
            return false;
        }

        TriadNpcGate.SyncTrackedNpc();

        if (TalkHelper.IsVisible())
        {
            return TriadNpcGate.IsTargeting() || TriadNpcGate.IsInDialogueFlow() || CanAutomateTalk();
        }

        return SelectYesnoHelper.TryGetVisible(out var yesno) && SelectYesnoHelper.IsTriadYesno(yesno);
    }

    public static void Tick()
    {
        EnsureTalkListener();
        TriadNpcGate.SyncTrackedNpc();
        TriadNpcGate.RefreshDialogueFlow();

        if (TriadMapNavigation.IsAwaitingTriadStartDialog())
        {
            if (TriadMapNavigation.TryAdvanceTriadStartDialog())
            {
                TriadNpcGate.MarkDialogueFlow();
            }
        }

        if (!ShouldRun())
        {
            return;
        }

        RunDialogueAutomation();
    }

    private static void OnTalkUpdate(AddonEvent type, AddonArgs args)
    {
        if (!TriadRunSession.ModuleEnabled && !TriadCardFarmSession.SessionActive)
        {
            return;
        }

        if (IsGoldSaucerMinigameOccupied())
        {
            return;
        }

        TriadNpcGate.SyncTrackedNpc();
        TriadNpcGate.RefreshDialogueFlow();

        if (!TalkHelper.IsVisible() || !CanAutomateTalk())
        {
            return;
        }

        TryAdvanceTalk();
    }

    private static void RunDialogueAutomation()
    {
        // SelectIconString can stay "visible" after picking Triad while Talk is already open.
        if (TalkHelper.IsVisible())
        {
            TryAdvanceTalk();
            TryAdvanceTriadYesno();
            return;
        }

        if (SelectStringHelper.IsNpcListMenuVisible())
        {
            if (!CanAutomateSelectString())
            {
                return;
            }

            if (SelectStringHelper.TrySelectTriadEntry("SaucyTriadSelectString"))
            {
                TriadNpcGate.MarkDialogueFlow();
            }

            return;
        }

        TryAdvanceTriadYesno();
    }

    private static void TryAdvanceTalk()
    {
        if (!CanAutomateTalk())
        {
            return;
        }

        if (QuestDialogueGuard.ShouldBlockTalk(
            TriadNpcGate.IsTargeting() ||
            TriadNpcGate.IsInDialogueFlow() ||
            TriadTargetNpc.FromWorldTarget() != null))
        {
            return;
        }

        if (SelectYesnoHelper.TryGetVisible(out var blockingYesno) &&
            !SelectYesnoHelper.ShouldPressTriadYesno(blockingYesno))
        {
            return;
        }

        if (TalkHelper.TryAdvance(TalkThrottleKey))
        {
            TriadNpcGate.MarkDialogueFlow();
        }
    }

    private static void TryAdvanceTriadYesno()
    {
        if (Svc.Condition[ConditionFlag.OccupiedInQuestEvent] ||
            !SelectYesnoHelper.TryGetVisible(out var yesno) ||
            !SelectYesnoHelper.ShouldPressTriadYesno(yesno))
        {
            return;
        }

        if (!TriadNpcGate.CanAutomateYesno())
        {
            return;
        }

        if (QuestDialogueGuard.ShouldBlockYesno(
            TriadNpcGate.IsTargeting() || TriadNpcGate.IsInDialogueFlow()))
        {
            return;
        }

        if (SelectYesnoHelper.PressYes(yesno))
        {
            TriadNpcGate.MarkDialogueFlow();
        }
    }

    private static bool CanAutomateSelectString() =>
        TriadMapNavigation.IsAwaitingTriadStartDialog() ||
        TriadNpcGate.HasInitiatedDialogue() ||
        TriadNpcGate.IsInDialogueFlow();

    private static bool CanAutomateTalk() =>
        TriadMapNavigation.IsAwaitingTriadStartDialog() ||
        TriadNpcGate.HasInitiatedDialogue() ||
        TriadNpcGate.IsInDialogueFlow() ||
        (TriadRunSession.ModuleEnabled && TriadTargetNpc.FromWorldTarget() != null);

    private static bool IsGoldSaucerMinigameOccupied() =>
        Svc.Condition[ConditionFlag.OccupiedInQuestEvent] &&
        GoldSaucerArcadeMachineHelper.AnyEnabled();

    private static bool IsTriadSessionUiVisible() =>
        uiReaderPrep.HasMatchRequestUI ||
        uiReaderPrep.HasDeckSelectionUI ||
        uiReaderGame.IsVisible ||
        TriadUiState.IsMatchRegistrationVisible() ||
        TriadUiState.IsPrepDeckSelectVisible() ||
        TriadUiState.IsResultVisible();
}
