using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using Saucy.Framework;
using System;
using System.Numerics;
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
        var id = Svc.Targets.Target?.BaseId ?? 0;
        var npc = TriadNpcDB.Get().FindByBaseID(id);
        return npc;
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

internal static class TriadNpcProximity
{
    public const float DefaultRange = 6f;

    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    public static bool IsPlayerNearCurrentTarget(float maxDistance = DefaultRange)
    {
        var target = Svc.Targets.Target;
        if (target == null)
        {
            return false;
        }

        var triadNpc = TriadNpcDB.Get().FindByBaseID(target.BaseId);
        return triadNpc != null &&
               HorizontalDistance(Player.Position, target.Position) <= maxDistance;
    }

    public static bool IsPlayerNear(TriadNpc npc, float maxDistance = DefaultRange) =>
        FindNearbyObject(npc, maxDistance) != null;

    public static IGameObject? FindNearbyObject(TriadNpc npc, float maxDistance = DefaultRange)
    {
        if (npc == null)
        {
            return null;
        }

        var target = Svc.Targets.Target;
        if (target != null &&
            npc.IsMatchingObject(target) &&
            HorizontalDistance(Player.Position, target.Position) <= maxDistance)
        {
            return target;
        }

        IGameObject? closest = null;
        var closestDist = float.MaxValue;

        foreach (var obj in Svc.Objects)
        {
            if (obj.ObjectKind != ObjectKind.EventNpc)
            {
                continue;
            }

            if (!npc.IsMatchingObject(obj))
            {
                continue;
            }

            var dist = HorizontalDistance(Player.Position, obj.Position);
            if (dist <= maxDistance && dist < closestDist)
            {
                closestDist = dist;
                closest = obj;
            }
        }

        return closest;
    }

    public static TriadNpc? ResolveTriadNpcForProximityCheck()
    {
        var fromTarget = TriadTargetNpc.FromWorldTarget();
        if (fromTarget != null)
        {
            return fromTarget;
        }

        return TriadTargetNpc.FromRunContext(TriadRunTarget.Resolve());
    }

    public static bool IsRelevantTriadNpcNearby(float maxDistance = DefaultRange) =>
        IsPlayerNearCurrentTarget(maxDistance) ||
        (ResolveTriadNpcForProximityCheck() is { } npc && IsPlayerNear(npc, maxDistance));
}

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
            ObjectHelper.SetTrackedObjects(Scope, [npcInfo.ENpcBaseId]);
            return;
        }

        ObjectHelper.ClearTrackedObjects(Scope);
    }

    public static bool IsTargeting()
    {
        if (trackedNpc == null)
        {
            return false;
        }

        if (ObjectHelper.IsTargeting(Scope))
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
        npc.IsMatchingObject(obj);
}
