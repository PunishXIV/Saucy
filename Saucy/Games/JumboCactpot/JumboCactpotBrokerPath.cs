using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using Saucy.Cactpot;
using Saucy.Framework;
using Saucy.IPC;
using AgentId = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId;

namespace Saucy.JumboCactpot;

internal static class JumboCactpotBrokerPath
{
    private const string BrokerInteractKey = "Saucy.JumboCactpot.BrokerInteract";

    private static bool requested;
    private static uint? activePathTargetBaseId;

    internal static bool IsComplete { get; private set; }

    internal static void Reset()
    {
        requested = false;
        IsComplete = false;
        activePathTargetBaseId = null;
        if (Vnavmesh.IsInstalled)
        {
            Vnavmesh.StopPath();
        }
    }

    internal static void Request()
    {
        if (IsComplete)
        {
            return;
        }

        requested = true;
    }

    internal static void Tick()
    {
        if (IsComplete || !requested || !Vnavmesh.IsInstalled)
        {
            return;
        }

        if (!Player.Interactable || Svc.Condition[ConditionFlag.BetweenAreas])
        {
            return;
        }

        if (IsBrokerFlowActive())
        {
            Finish();
            return;
        }

        if (IsMovementPaused())
        {
            return;
        }

        var npc = ObjectHelper.FindNearestByBaseId(CactpotNpcs.JumboBrokerBaseId);
        if (npc == null)
        {
            return;
        }

        if (ObjectHelper.HasArrivedAt(npc))
        {
            activePathTargetBaseId = null;
            if (Vnavmesh.IsMoving())
            {
                Vnavmesh.StopPath();
            }

            ObjectHelper.TryInteractWithBaseId(CactpotNpcs.JumboBrokerBaseId, throttleKey: BrokerInteractKey);
            return;
        }

        if (Vnavmesh.IsMoving() && activePathTargetBaseId == CactpotNpcs.JumboBrokerBaseId)
        {
            return;
        }

        if (Vnavmesh.IsMoving())
        {
            Vnavmesh.StopPath();
        }

        if (ObjectHelper.TryMoveToBaseId(CactpotNpcs.JumboBrokerBaseId))
        {
            activePathTargetBaseId = CactpotNpcs.JumboBrokerBaseId;
        }
    }

    private static void Finish()
    {
        activePathTargetBaseId = null;
        requested = false;
        IsComplete = true;
        if (Vnavmesh.IsMoving())
        {
            Vnavmesh.StopPath();
        }
    }

    private static bool IsBrokerFlowActive() =>
        ObjectHelper.HasInitiatedDialogue(CactpotNpcs.JumboBrokerScope) ||
        AgentHelper.IsActive(AgentId.LotteryWeekly);

    private static bool IsMovementPaused() =>
        TalkHelper.IsVisible() ||
        SelectStringHelper.IsNpcListMenuVisible() ||
        SelectYesnoHelper.IsVisible();
}
