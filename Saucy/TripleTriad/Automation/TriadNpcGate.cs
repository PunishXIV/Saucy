using Dalamud.Game.ClientState.Objects.Types;
using Saucy.Framework;
using System;
namespace Saucy.TripleTriad;

internal static unsafe class TriadNpcGate
{
    public const string Scope = "Triad";

    private static readonly TimedFlowWindow dialogueFlow = new(TimeSpan.FromSeconds(30));

    private static TriadNpc? trackedNpc;

    public static bool IsInDialogueFlow() => dialogueFlow.IsActive;

    public static void MarkDialogueFlow() => dialogueFlow.Mark();

    public static void ClearDialogueFlow() => dialogueFlow.Clear();

    public static void SyncTrackedNpc()
    {
        var npc = ResolveActiveTriadNpc();
        if (trackedNpc?.Id == npc?.Id)
        {
            return;
        }

        var inNpcDialogue = TalkHelper.IsVisible() || SelectStringHelper.IsNpcListMenuVisible();
        trackedNpc = npc;
        if (!inNpcDialogue || !IsInDialogueFlow())
        {
            ClearDialogueFlow();
        }

        if (npc != null &&
            GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out var npcInfo) &&
            npcInfo.ENpcBaseId != 0)
        {
            NpcHelper.SetTrackedNpcs(Scope, [npcInfo.ENpcBaseId]);
            return;
        }

        NpcHelper.ClearTrackedNpcs(Scope);
    }

    public static bool IsTargeting()
    {
        if (trackedNpc == null)
        {
            return false;
        }

        if (NpcHelper.IsTargeting(Scope))
        {
            return true;
        }

        return IsTargetMatchingNpc(Svc.Targets.Target, trackedNpc) ||
               IsTargetMatchingNpc(Svc.Targets.SoftTarget, trackedNpc);
    }

    public static bool HasInitiatedDialogue() =>
        IsTargeting() &&
        (TalkHelper.IsVisible() || SelectStringHelper.IsNpcListMenuVisible());

    public static bool CanAutomateYesno() =>
        HasInitiatedDialogue() ||
        IsInDialogueFlow();

    public static void RefreshDialogueFlow()
    {
        if (!IsTargeting())
        {
            return;
        }

        if (!IsInDialogueFlow() && !HasInitiatedDialogue())
        {
            return;
        }

        if (HasTriadFlowUi() || IsNpcDialogueOpen())
        {
            MarkDialogueFlow();
        }
    }

    private static TriadNpc? ResolveActiveTriadNpc()
    {
        if (IsNpcDialogueOpen() && TriadTargetNpc.FromWorldTarget() is { } dialogueNpc)
        {
            return dialogueNpc;
        }

        if (TriadMapNavigation.TryGetPendingNpc(out var navNpc))
        {
            return navNpc;
        }

        if (TriadRunSession.ModuleEnabled || TriadCardFarmSession.SessionActive)
        {
            return TriadTargetNpc.FromRunContext(TriadRunTarget.Resolve()) ??
                   TriadTargetNpc.FromSolverContext() ??
                   TriadTargetNpc.FromWorldTarget();
        }

        return TriadTargetNpc.FromWorldTarget();
    }

    private static bool IsNpcDialogueOpen() =>
        TalkHelper.IsVisible() || SelectStringHelper.IsNpcListMenuVisible();

    private static bool HasTriadFlowUi() =>
        TriadUiState.IsAutomationFlowActive() ||
        TriadMapNavigation.IsAwaitingTriadStartDialog() ||
        SelectYesnoHelper.TryGetTriadYesno(out var _);

    private static bool IsTargetMatchingNpc(IGameObject? obj, TriadNpc npc) =>
        obj != null && npc.IsMatchingName(obj.Name.TextValue);
}
