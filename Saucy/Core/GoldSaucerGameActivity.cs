using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.GoldSaucer;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.AirForce;
using Saucy.Framework;
using Saucy.OtherGames;
using static ECommons.GenericHelpers;

namespace Saucy;

internal static unsafe class GoldSaucerGameActivity
{
    public static bool IsAnyGamePlaying()
    {
        if (IsCuffSessionActive() ||
            IsLimbSessionActive() ||
            IsTriadSessionActive() ||
            IsMiniCactpotSessionActive() ||
            IsAirForceSessionActive() ||
            IsGateAutoMovementActive())
        {
            return true;
        }

        return false;
    }

    private static bool IsArcadeMachineSessionActive(GoldSaucerArcadeMachine machine) =>
        GoldSaucerArcadeMachineHelper.IsEnabled(machine) &&
        (ArcadeMachineSession.IsInteractPending(machine) ||
         Svc.Condition[ConditionFlag.OccupiedInQuestEvent] ||
         GoldSaucerRewardHelper.IsVisible());

    private static bool IsCuffSessionActive() =>
        IsArcadeMachineSessionActive(GoldSaucerArcadeMachine.Cuff);

    private static bool IsLimbSessionActive() =>
        IsArcadeMachineSessionActive(GoldSaucerArcadeMachine.Limb);

    private static bool IsTriadSessionActive() =>
        TriadRunSession.ModuleEnabled &&
        (TriadUiState.IsAutomationFlowActive() ||
         uiReaderGame.IsVisible ||
         TriadMapNavigation.IsNavigationActive ||
         TriadCardFarmSession.SessionActive);

    private static bool IsMiniCactpotSessionActive() =>
        C.IsModuleEnabled(ModuleNames.MiniCactpot) &&
        TryGetAddonByName<AtkUnitBase>("LotteryDaily", out var addon) &&
        addon->IsVisible &&
        IsAddonReady(addon);

    private static bool IsAirForceSessionActive() =>
        C.IsModuleEnabled(ModuleNames.AirForceOne) &&
        (Svc.Condition[ConditionFlag.BoundByDuty95] || AirForceAutomation.ShouldTrackReward);

    private static bool IsGateAutoMovementActive()
    {
        if (!IsPlayerOnGateStage())
        {
            return false;
        }

        var gate = GetCurrentGate();
        if (gate == Module.GateType.SliceIsRight &&
            C.IsModuleEnabled(ModuleNames.SliceIsRight) &&
            C.GoldSaucerGates.SliceIsRightAutoMovement)
        {
            return true;
        }

        if (gate == Module.GateType.AnyWayTheWindBlows &&
            C.IsModuleEnabled(ModuleNames.AnyWayTheWindBlows) &&
            C.GoldSaucerGates.WindBlowsAutoMovement &&
            !AnyWayTheWindBlows.Stage.SafeSpot.On)
        {
            return true;
        }

        return false;
    }

    private static bool IsPlayerOnGateStage()
    {
        var mgr = GoldSaucerManager.Instance();
        if (mgr is null)
        {
            return false;
        }

        var dir = mgr->CurrentGFateDirector;
        return dir is not null &&
               dir->Flags.HasFlag(GFateDirectorFlag.IsJoined) &&
               !dir->Flags.HasFlag(GFateDirectorFlag.IsFinished);
    }

    private static Module.GateType GetCurrentGate()
    {
        var mgr = GoldSaucerManager.Instance();
        if (mgr is null)
        {
            return Module.GateType.None;
        }

        var dir = mgr->CurrentGFateDirector;
        return dir is not null ? (Module.GateType)dir->GateType : Module.GateType.None;
    }
}
