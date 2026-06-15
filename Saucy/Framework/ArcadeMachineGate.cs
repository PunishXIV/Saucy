using Dalamud.Game.ClientState.Conditions;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
namespace Saucy.Framework;

public static unsafe class ArcadeMachineGate
{
    private static readonly TimedFlowWindow LimbFlow = new(TimeSpan.FromSeconds(45));
    private static readonly TimedFlowWindow CuffFlow = new(TimeSpan.FromSeconds(45));

    public static bool IsInFlow(string scope) =>
        scope switch
        {
            ArcadeMachineScopes.Limb => LimbFlow.IsActive,
            ArcadeMachineScopes.Cuff => CuffFlow.IsActive,
            var _ => false
        };

    public static void MarkFlow(string scope)
    {
        switch (scope)
        {
            case ArcadeMachineScopes.Limb:
                LimbFlow.Mark();
                break;
            case ArcadeMachineScopes.Cuff:
                CuffFlow.Mark();
                break;
        }
    }

    public static void ClearFlow(string scope)
    {
        switch (scope)
        {
            case ArcadeMachineScopes.Limb:
                LimbFlow.Clear();
                break;
            case ArcadeMachineScopes.Cuff:
                CuffFlow.Clear();
                break;
        }
    }

    public static bool HasVisibleArcadeStartMenu() =>
        SelectStringHelper.TryGetArcadeMenu(out var menu) &&
        SelectStringHelper.IsArcadeYesnoMenu(menu);

    public static bool CanAutomateArcadeMenu(string scope) =>
        ObjectHelper.HasInitiatedArcadeMenu(scope) ||
        (IsInFlow(scope) && HasVisibleArcadeStartMenu());

    public static bool CanAutomateArcadeYesno(string scope) =>
        ObjectHelper.HasInitiatedArcadeMenu(scope) ||
        (IsInFlow(scope) && ObjectHelper.IsTargeting(scope));

    internal static bool TryConfirmStartMenu(
        GoldSaucerArcadeMachine machine,
        string throttleKey,
        int throttleMs = 400,
        AddonSelectString* menu = null)
    {
        if (!GoldSaucerArcadeMachineHelper.IsEnabled(machine) ||
            ArcadeMachineSession.BlocksAnotherStart(machine) ||
            !GoldSaucerArcadeRunSession.ShouldContinue(machine) ||
            GoldSaucerArcadeFakeBreak.IsActive(machine) ||
            AutoRetainerPause.BlocksArcadeSessions(machine))
        {
            return false;
        }

        var scope = GoldSaucerArcadeMachineHelper.GetScope(machine);
        if (!CanAutomateArcadeMenu(scope))
        {
            return false;
        }

        if (!EzThrottler.Throttle(throttleKey, throttleMs))
        {
            return false;
        }

        if (menu == null && !SelectStringHelper.TryGetArcadeMenu(out menu))
        {
            return false;
        }

        if (!SelectStringHelper.IsArcadeYesnoMenu(menu) ||
            !SelectStringHelper.TrySelectYesEntry(menu))
        {
            return false;
        }

        if (GoldSaucerArcadeRunSession.PlayXTimes(machine) &&
            GoldSaucerArcadeRunSession.GetRemaining(machine) == 1)
        {
            ArcadeMachineSession.MarkPlayingFinalRound(machine);
        }

        ArcadeMachineSession.ClearInteractPending(machine);
        MarkFlow(scope);
        return true;
    }

    public static void TryReclaimSession(string scope, Func<bool> hasMinigameUi, Func<bool> isNearMachine)
    {
        if (IsInFlow(scope))
        {
            return;
        }

        if (hasMinigameUi() ||
            ObjectHelper.HasInitiatedArcadeMenu(scope) ||
            ObjectHelper.IsTargeting(scope) ||
            isNearMachine())
        {
            MarkFlow(scope);
        }
    }

    public static bool IsUnrelatedQuestOccupancy(
        string scope,
        Func<bool> hasMinigameUi,
        Func<bool> isNearMachine) =>
        Svc.Condition[ConditionFlag.OccupiedInQuestEvent] &&
        !hasMinigameUi() &&
        !ObjectHelper.HasInitiatedArcadeMenu(scope) &&
        !ObjectHelper.IsTargeting(scope) &&
        !isNearMachine();

    public static void RefreshFlow(
        string scope,
        bool inTimedFlow,
        Action markFlow,
        Func<bool> hasModuleUi)
    {
        if (!ObjectHelper.IsTargeting(scope) && !inTimedFlow)
        {
            return;
        }

        if (!inTimedFlow && !ObjectHelper.HasInitiatedArcadeMenu(scope))
        {
            return;
        }

        if (hasModuleUi())
        {
            markFlow();
        }
    }
}
