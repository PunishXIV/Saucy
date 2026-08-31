using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using Saucy.Cactpot;
using Saucy.Framework;
using Saucy.IPC;
using Saucy.TripleTriad;

namespace Saucy.JumboCactpot;

internal static class JumboCactpotBrokerPath
{
    private const string BrokerInteractKey = "Saucy.JumboCactpot.BrokerInteract";

    private const float BrokerPathCloseRange = 2f;
    private const float BrokerInteractRange = 3f;

    private static bool requested;
    private static uint? activePathTargetBaseId;

    internal static bool IsComplete { get; private set; }

    internal static bool IsActive => requested && !IsComplete;

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
        if (IsComplete || !requested)
        {
            return;
        }

        // Without vnavmesh the path never completes and YesAlready stays paused.
        if (!Vnavmesh.IsInstalled)
        {
            Reset();
            return;
        }

        if (Svc.Condition[ConditionFlag.BetweenAreas])
        {
            Reset();
            return;
        }

        if (!Player.Interactable)
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

        if (CanInteractWithBroker(npc))
        {
            activePathTargetBaseId = null;
            if (Vnavmesh.IsPathRunning())
            {
                Vnavmesh.StopPath();
                return;
            }

            if (!TravelMountHelper.TryDismount())
            {
                return;
            }

            ObjectHelper.TryInteractWithObject(npc, BrokerInteractKey);
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

        if (!Vnavmesh.CanStartPathfind())
        {
            Vnavmesh.TryEnsureNavMeshLoading();
            return;
        }

        if (ObjectHelper.TryMoveToBaseId(CactpotNpcs.JumboBrokerBaseId, BrokerPathCloseRange))
        {
            activePathTargetBaseId = CactpotNpcs.JumboBrokerBaseId;
        }
    }

    private static bool CanInteractWithBroker(IGameObject npc) =>
        Vnavmesh.IsWithinHorizontalRange(npc.Position, BrokerInteractRange) ||
        ObjectHelper.GetHorizontalEdgeDistance(npc) <= 2.25f;

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
        CactpotDialogueHelper.IsJumboInputVisible();

    private static bool IsMovementPaused() =>
        TalkHelper.IsVisible() ||
        SelectStringHelper.IsNpcListMenuVisible() ||
        SelectYesnoHelper.IsVisible();
}
