using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Saucy.Framework;
using Saucy.IPC;
using System;
using System.Linq;
using System.Numerics;
using DalamudObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace Saucy.TripleTriad;

internal static unsafe partial class TriadMapNavigation
{
    private const int NpcInteractionAttemptsAbortLimit = 6;
    private static readonly TimeSpan NpcInteractionPhaseTimeout = TimeSpan.FromSeconds(10);
    private static bool IsPlayerWithinNpcInteractionRange(TriadNpc npc, Vector3 fallbackDestination)
    {
        if (!IsPlayerInNpcTerritory(npc))
        {
            return false;
        }

        if (FindTriadNpcObject(npc) is { } obj)
        {
            return HorizontalDistance(Player.Position, obj.Position) <= NpcInteractionRange;
        }

        var npcPos = ResolveLiveTriadNpcPosition(npc) ?? fallbackDestination;
        return HorizontalDistance(Player.Position, npcPos) <= NpcInteractionRange;
    }

    private static bool IsPlayerWithinNpcInteractionRange(PendingNavigation pending) =>
        pending.Npc != null && IsPlayerWithinNpcInteractionRange(pending.Npc, pending.Destination);

    private static bool IsPlayerWithinNpcPathArrivalRange(TriadNpc npc, Vector3 fallbackDestination)
    {
        if (!IsPlayerInNpcTerritory(npc))
        {
            return false;
        }

        if (FindTriadNpcObject(npc) is { } obj)
        {
            return HorizontalDistance(Player.Position, obj.Position) <= NpcPathArrivalRange + 0.5f;
        }

        var npcPos = ResolveLiveTriadNpcPosition(npc) ?? fallbackDestination;
        return HorizontalDistance(Player.Position, npcPos) <= NpcPathArrivalRange + 0.5f;
    }

    private static bool IsPlayerInNpcTerritory(TriadNpc npc)
    {
        if (_pending?.TargetTerritoryId is uint pendingTerritory and not 0)
        {
            return Svc.ClientState.TerritoryType == pendingTerritory;
        }

        if (GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out var npcInfo) && npcInfo.TerritoryId != 0)
        {
            return Svc.ClientState.TerritoryType == npcInfo.TerritoryId;
        }

        return true;
    }

    private static bool IsPlayerWithinNpcPathArrivalRange(PendingNavigation pending) =>
        pending.Npc != null && IsPlayerWithinNpcPathArrivalRange(pending.Npc, pending.Destination);

    private static bool TryBeginMovingToNpcIfAlreadyNearby(PendingNavigation pending)
    {
        if (!IsPlayerWithinNpcPathArrivalRange(pending))
        {
            return false;
        }

        StopVnavIfRunning();
        if (ResolveLiveTriadNpcPosition(pending.Npc) is { } livePos)
        {
            pending.Destination = ResolveNpcPathPoint(livePos);
        }

        pending.Phase = NavigationPhase.MovingToNpc;
        pending.PhaseStartedUtc = DateTime.UtcNow;
        pending.VnavRetryCount = 0;
        pending.AttemptMountBeforeNav = false;
        pending.NavMeshWaitAnnounced = false;
        return true;
    }

    private static void TickMovingToNpc(PendingNavigation pending)
    {
        if (TriadUiState.IsAutomationFlowActive())
        {
            ClearPending();
            return;
        }

        ResolveNpcNavigationTarget(pending, out var npcPos, out var horizDistToNpc);
        var withinInteractionRange = horizDistToNpc <= NpcInteractionRange;
        var tooCloseToInteract = horizDistToNpc < NpcMinStandoffDistance;

        if (withinInteractionRange)
        {
            StopVnavIfRunning();
        }

        if (!withinInteractionRange)
        {
            if (Vnavmesh.TickArrival(npcPos, NpcPathArrivalRange))
            {
                return;
            }

            if (!CanBeginLocalNavigation())
            {
                return;
            }

            if (Vnavmesh.IsMoving())
            {
                return;
            }

            if (pending.VnavRetryCount < 3 &&
                EzThrottler.Throttle("SaucyNavVnavRetry", 2000))
            {
                if (!TryEnsureMountedForNav(pending))
                {
                    return;
                }

                pending.VnavRetryCount++;
                if (TryMoveToNpc(pending, npcPos))
                {
                    return;
                }
            }

            if (DateTime.UtcNow - pending.PhaseStartedUtc > TimeSpan.FromSeconds(45))
            {
                Svc.Chat.PrintError("[Saucy] Could not reach the Triple Triad NPC.");
                ClearPending();
            }

            return;
        }

        if (tooCloseToInteract)
        {
            if (!IsReadyForNpcInteraction())
            {
                return;
            }

            if (!Vnavmesh.IsMoving())
            {
                TryBackoffFromNpc(npcPos, pending.Fly);
            }

            return;
        }

        if (SelectStringHelper.IsNpcListMenuVisible())
        {
            if (SelectStringHelper.TrySelectTriadEntry())
            {
                TriadNpcGate.MarkDialogueFlow();
                return;
            }
        }

        if (!IsReadyForNpcInteraction())
        {
            return;
        }

        if (SelectStringHelper.IsNpcListMenuVisible())
        {
            TryBeginTriadMatchAfterDeckOptimizer(pending);
            return;
        }

        if (TryBeginTriadMatchAfterDeckOptimizer(pending) && !pending.AnnouncedTriadStart)
        {
            pending.AnnouncedTriadStart = true;
            Svc.Chat.Print($"[Saucy] Arrived at {pending.Npc!.Name}. Starting Triple Triad...");
        }

        if (DateTime.UtcNow - pending.PhaseStartedUtc > TimeSpan.FromSeconds(45))
        {
            Svc.Chat.PrintError("[Saucy] Could not reach the Triple Triad NPC.");
            ClearPending();
        }
    }

    private static void ResolveNpcNavigationTarget(
        PendingNavigation pending,
        out Vector3 npcPos,
        out float horizDistToNpc)
    {
        var source = ResolveLiveTriadNpcPosition(pending.Npc) ?? pending.Destination;
        npcPos = ResolveNpcPathPoint(source);
        pending.Destination = npcPos;
        horizDistToNpc = HorizontalDistance(Player.Position, npcPos);
    }

    private static bool TryBeginTriadMatchAfterDeckOptimizer(PendingNavigation pending)
    {
        if (!IsStableForNpcCommands())
        {
            return false;
        }

        if (pending.Npc != null && TriadRun.IsNavigationBlockedWaitingForOptimizer(pending.Npc))
        {
            return false;
        }

        pending.Phase = NavigationPhase.StartingTriadMatch;
        pending.PhaseStartedUtc = DateTime.UtcNow;
        TriadRunSession.EnableFromNavigation();
        return true;
    }

    private static void TickStartingTriadMatch(PendingNavigation pending)
    {
        if (TriadUiState.IsBoardVisible() || TriadUiState.IsResultVisible())
        {
            ClearPending();
            return;
        }

        if (pending.Npc != null && TriadRun.IsNavigationBlockedWaitingForOptimizer(pending.Npc))
        {
            return;
        }

        if (IsBetweenAreas())
        {
            return;
        }

        if (TriadUiState.IsMatchRegistrationVisible() || TriadUiState.IsPrepDeckSelectVisible())
        {
            ClearPending();
            return;
        }

        if (SelectStringHelper.IsNpcListMenuVisible() ||
            SelectYesnoHelper.TryGetTriadYesno(out var _) ||
            TalkHelper.IsVisible())
        {
            if (TryAdvanceTriadStartDialog())
            {
                return;
            }
        }

        if (!IsReadyForNpcInteraction())
        {
            return;
        }

        if (TryAdvanceTriadStartDialog())
        {
            return;
        }

        var npcPos = ResolveLiveTriadNpcPosition(pending.Npc) ?? pending.Destination;
        var horizDistToNpc = HorizontalDistance(Player.Position, npcPos);
        if (horizDistToNpc <= NpcInteractionRange)
        {
            StopVnavIfRunning();
        }

        if (horizDistToNpc < NpcMinStandoffDistance)
        {
            TryBackoffFromNpc(npcPos, pending.Fly);
            return;
        }

        if (TryInteractWithTriadNpc(pending.Npc))
        {
            pending.NpcInteractionAttempts++;

            // Abort fast if we've fired several interactions without ever reaching the Triple Triad menu —
            // most often the NPC's Triple Triad isn't unlocked yet (quest prerequisite), and each interaction
            // just spawns a Talk dialog that we keep dismissing. Six interactions ≈ 6 seconds (1s throttle).
            if (pending.NpcInteractionAttempts >= NpcInteractionAttemptsAbortLimit)
            {
                TriadNpcUnlockHelper.Announce(TriadNpcUnlockHelper.FormatNavigationInteractAbortMessage(pending.Npc));
                ClearPending();
            }

            return;
        }

        if (DateTime.UtcNow - pending.PhaseStartedUtc > NpcInteractionPhaseTimeout)
        {
            Svc.Chat.PrintError("[Saucy] Could not open Triple Triad with this NPC.");
            ClearPending();
        }
    }

    private static bool TryBackoffFromNpc(Vector3 npcPos, bool fly)
    {
        StopVnavIfRunning();
        if (Vnavmesh.IsMoving())
        {
            return true;
        }

        return TryPathToNpcBackoff(npcPos, fly);
    }

    private static bool EnsureDismountedForNpcInteraction()
    {
        if (Svc.Condition[ConditionFlag.Jumping] || Svc.Condition[ConditionFlag.MountOrOrnamentTransition])
        {
            return false;
        }

        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        StopVnavIfRunning();
        return TravelMountHelper.TryDismount();
    }

    private static Vector3 GetNpcBackoffPoint(Vector3 npcPos, Vector3? fromPos = null)
    {
        var reference = fromPos ?? Player.Position;
        var awayFromNpc = reference - npcPos;
        awayFromNpc.Y = 0f;

        if (awayFromNpc.LengthSquared() < 0.25f)
        {
            var facing = Player.Rotation;
            awayFromNpc = new(MathF.Sin(facing), 0f, MathF.Cos(facing));
        }

        awayFromNpc = Vector3.Normalize(awayFromNpc);
        return npcPos + awayFromNpc * NpcBackoffDistance;
    }

    private static Vector3 ResolveNpcBackoffPoint(Vector3 npcPos) =>
        SnapDestinationNearPlayerFloor(GetNpcBackoffPoint(npcPos));

    private static float HorizontalDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    private static bool TryInteractWithTriadNpc(TriadNpc? npc)
    {
        if (npc == null)
        {
            return false;
        }

        if (!IsReadyForNpcInteraction())
        {
            return false;
        }

        if (SelectStringHelper.IsNpcListMenuVisible())
        {
            return false;
        }

        var target = FindTriadNpcObject(npc);
        if (target == null)
        {
            return false;
        }

        var distToTarget = HorizontalDistance(Player.Position, target.Position);
        if (distToTarget is > NpcInteractionRange or < NpcMinStandoffDistance)
        {
            return false;
        }

        if (Svc.Targets.Target?.GameObjectId != target.GameObjectId)
        {
            if (!EzThrottler.Throttle("SaucyNavTargetNpc", 400))
            {
                return false;
            }

            Svc.Targets.Target = target;
            return true;
        }

        if (!EzThrottler.Throttle("SaucyNavInteractNpc", 1000))
        {
            return false;
        }

        TargetSystem.Instance()->InteractWithObject((GameObject*)target.Address, false);
        return true;
    }

    private static IGameObject? FindTriadNpcObject(TriadNpc npc)
    {
        if (GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out var npcInfo) && npcInfo.ENpcBaseId != 0)
        {
            if (FindWorldObjectByBaseId(npcInfo.ENpcBaseId, NpcInteractionRange + 5f) is { } byBaseId)
            {
                return byBaseId;
            }
        }

        return TriadNpcProximity.FindNearbyObject(npc, NpcInteractionRange + 2f) ??
               FindTriadNpcObjectByScan(npc);
    }

    private static IGameObject? FindWorldObjectByBaseId(uint baseId, float maxDistance = float.MaxValue)
    {
        IGameObject? closest = null;
        var closestDist = float.MaxValue;

        foreach (var obj in Svc.Objects)
        {
            if (obj.BaseId != baseId)
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

    private static IGameObject? FindTriadNpcObjectByScan(TriadNpc npc)
    {
        if (GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out var npcInfo) && npcInfo.ENpcBaseId != 0)
        {
            if (FindWorldObjectByBaseId(npcInfo.ENpcBaseId) is { } byBaseId)
            {
                return byBaseId;
            }
        }

        return Svc.Objects
            .Where(obj => obj.ObjectKind == DalamudObjectKind.EventNpc && npc.IsMatchingName(obj.Name.ToString()))
            .OrderBy(obj => HorizontalDistance(Player.Position, obj.Position))
            .FirstOrDefault();
    }

    private static Vector3? ResolveLiveTriadNpcPosition(TriadNpc? npc) =>
        npc == null ? null : FindTriadNpcObject(npc)?.Position;
}
