using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using Saucy.IPC;
using System;
using System.Numerics;
namespace Saucy.TripleTriad;

internal static partial class TriadMapNavigation
{
    private static bool TryBeginNavigation(MapLinkPayload location, bool fly = true, TriadNpc? npc = null)
    {
        if (npc != null && TriadNpcUnlockHelper.TryReject(npc, out var _))
        {
            return false;
        }

        if (!Vnavmesh.IsInstalled)
        {
            Svc.Chat.Print("[Saucy] Install vnavmesh to path to NPCs from Saucy.");
            return false;
        }

        var destination = ResolveDestination(location, npc);
        if (destination == null)
        {
            Svc.Chat.PrintError("[Saucy] Could not resolve NPC map coordinates.");
            return false;
        }

        var pointOnFloor = destination.Value;
        var targetTerritoryId = ResolveTargetTerritoryId(location, npc);
        var inTargetTerritory = IsInTargetTerritory(targetTerritoryId);

        if (!Lifestream.IsInstalled)
        {
            if (!inTargetTerritory)
            {
                Svc.Chat.Print(
                    $"[Saucy] {location.PlaceName} is in another zone. Install Lifestream to teleport there.");
                return false;
            }

            return TryStartVnavImmediate(location, pointOnFloor, fly, npc);
        }

        if (Lifestream.IsBusyNow())
        {
            Svc.Chat.Print("[Saucy] Lifestream is busy. Try again in a moment.");
            return false;
        }

        var route = MultiAreaRouteRegistry.FindRoute(location);
        if (route != null && !inTargetTerritory)
        {
            if (route.Name == DomanEnclaveRoute.Route.Name)
            {
                var directAetheryte = DomanEnclaveRoute.ResolveDirectEnclaveAetheryteId();
                if (directAetheryte != 0 &&
                    AetheryteHelper.GetAetheryteTerritoryId(directAetheryte) == DomanEnclaveRoute.DomanEnclaveTerritoryId)
                {
                    if (!AetheryteHelper.IsPlayerInAetheryteTerritory(directAetheryte) &&
                        !Lifestream.TryTeleport(directAetheryte))
                    {
                        Svc.Chat.PrintError("[Saucy] Lifestream could not start teleport.");
                        return false;
                    }

                    StopVnavIfRunning();
                    BeginPending(
                        location,
                        pointOnFloor,
                        fly,
                        targetTerritoryId,
                        npc,
                        expectedPostTeleportTerritoryId: DomanEnclaveRoute.DomanEnclaveTerritoryId);
                    if (!AetheryteHelper.IsPlayerInAetheryteTerritory(directAetheryte))
                    {
                        Svc.Chat.Print(
                            $"[Saucy] Teleporting to {AetheryteHelper.FormatTeleportDestination(directAetheryte)}.");
                    }

                    return true;
                }
            }

            return TryBeginMultiAreaRoute(location, pointOnFloor, fly, targetTerritoryId, route, npc);
        }

        var travelPlan = AetheryteHelper.FindBestTravelPlan(targetTerritoryId, pointOnFloor, inTargetTerritory);
        if (!inTargetTerritory && !travelPlan.HasTeleport && !travelPlan.HasAethernet)
        {
            Svc.Chat.Print(
                $"[Saucy] No unlocked aetheryte found for {location.PlaceName}. Opening map.");
            return false;
        }

        if (inTargetTerritory)
        {
            if (npc != null && IsPlayerWithinNpcPathArrivalRange(npc, pointOnFloor))
            {
                StopVnavIfRunning();
                BeginPending(
                    location,
                    pointOnFloor,
                    fly,
                    targetTerritoryId,
                    npc,
                    startingPhase: NavigationPhase.MovingToNpc);
                return true;
            }

            if (travelPlan.HasAethernet &&
                npc != null &&
                !IsPlayerWithinNpcInteractionRange(npc, pointOnFloor) &&
                TryStartInTerritoryAethernet(location, pointOnFloor, fly, targetTerritoryId, travelPlan, npc))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(travelPlan.AethernetSkipReason))
            {
                Svc.Chat.Print($"[Saucy] Walking: {travelPlan.AethernetSkipReason}.");
            }

            return TryStartVnavImmediate(location, pointOnFloor, fly, npc);
        }

        if (!travelPlan.HasTeleport)
        {
            return TryStartVnavImmediate(location, pointOnFloor, fly, npc);
        }

        var skipTeleport = AetheryteHelper.IsPlayerInAetheryteTerritory(travelPlan.TeleportAetheryteId);
        if (!skipTeleport)
        {
            if (!Lifestream.TryTeleport(travelPlan.TeleportAetheryteId))
            {
                Svc.Chat.PrintError("[Saucy] Lifestream could not start teleport.");
                return false;
            }
        }

        StopVnavIfRunning();
        BeginPending(
            location,
            pointOnFloor,
            fly,
            targetTerritoryId,
            npc,
            aethernetShardId: travelPlan.AethernetShardId,
            aethernetShardName: travelPlan.AethernetShardName,
            hubAetheryteId: travelPlan.HubAetheryteId,
            expectedPostTeleportTerritoryId: AetheryteHelper.GetAetheryteTerritoryId(travelPlan.TeleportAetheryteId));

        var teleportDestination = AetheryteHelper.FormatTeleportDestination(travelPlan.TeleportAetheryteId);
        if (travelPlan.HasAethernet)
        {
            Svc.Chat.Print(
                skipTeleport
                    ? $"[Saucy] Lifestream aethernet to {travelPlan.AethernetShardName}."
                    : $"[Saucy] Teleporting to {teleportDestination}, then Lifestream aethernet to {travelPlan.AethernetShardName}.");
        }
        else if (!skipTeleport)
        {
            Svc.Chat.Print($"[Saucy] Teleporting to {teleportDestination}.");
        }

        return true;
    }

    private static bool TryBeginMultiAreaRoute(
        MapLinkPayload location,
        Vector3 pointOnFloor,
        bool fly,
        uint targetTerritoryId,
        MultiAreaRoute route,
        TriadNpc? npc)
    {
        var context = new MultiAreaRouteExecutor.MultiAreaRouteContext
        {
            Location = location, Destination = pointOnFloor, TargetTerritoryId = targetTerritoryId
        };

        if (!MultiAreaRouteExecutor.TryBeginRoute(route, context, out var execution, out var beginMessage))
        {
            return false;
        }

        var startsWithTeleport = execution.StepIndex < route.Steps.Count &&
                                 route.Steps[execution.StepIndex].Kind == MultiAreaRouteStepKind.Teleport &&
                                 !MultiAreaRouteExecutor.IsTeleportStepComplete(execution, route.Steps[execution.StepIndex]);

        BeginPending(
            location,
            pointOnFloor,
            fly,
            targetTerritoryId,
            npc,
            execution,
            startingPhase: startsWithTeleport
                ? NavigationPhase.WaitingForLifestream
                : NavigationPhase.ExecutingRoute);

        if (!string.IsNullOrEmpty(beginMessage))
        {
            Svc.Chat.Print(beginMessage);
        }

        return true;
    }

    private static void ContinueAfterZoneArrival(PendingNavigation pending)
    {
        pending.RouteExecution = null;
        pending.ArrivedViaMultiAreaRoute = true;
        pending.Fly = false;
        pending.Destination = ResolvePostRouteDestination(pending);
        if (TryBeginMovingToNpcIfAlreadyNearby(pending))
        {
            Svc.Chat.Print($"[Saucy] Arrived in {pending.Location.PlaceName}.");
            return;
        }

        pending.Phase = NavigationPhase.WaitingForNavReady;
        pending.PhaseStartedUtc = DateTime.UtcNow;
        pending.AttemptMountBeforeNav = true;
        Svc.Chat.Print($"[Saucy] Arrived in {pending.Location.PlaceName}. Waiting for vnavmesh...");
    }

    private static bool TryStartInTerritoryAethernet(
        MapLinkPayload location,
        Vector3 pointOnFloor,
        bool fly,
        uint targetTerritoryId,
        AetheryteHelper.TravelPlan travelPlan,
        TriadNpc? npc = null)
    {
        if (!travelPlan.HasAethernet)
        {
            return false;
        }

        if (travelPlan.HubAetheryteId != 0 &&
            AetheryteHelper.IsPlayerNearAetheryte(travelPlan.HubAetheryteId))
        {
            return TryStartAethernetTravel(location, pointOnFloor, fly, targetTerritoryId, travelPlan, npc);
        }

        return TryStartWalkToAethernetHub(location, pointOnFloor, fly, targetTerritoryId, travelPlan, npc);
    }

    private static bool TryStartWalkToAethernetHub(
        MapLinkPayload location,
        Vector3 pointOnFloor,
        bool fly,
        uint targetTerritoryId,
        AetheryteHelper.TravelPlan travelPlan,
        TriadNpc? npc = null)
    {
        if (travelPlan.HubAetheryteId == 0 ||
            AetheryteHelper.GetAetheryteApproachPosition(travelPlan.HubAetheryteId) == null)
        {
            return false;
        }

        StopVnavIfRunning();
        BeginPending(
            location,
            pointOnFloor,
            fly,
            targetTerritoryId,
            npc,
            aethernetShardId: travelPlan.AethernetShardId,
            aethernetShardName: travelPlan.AethernetShardName,
            hubAetheryteId: travelPlan.HubAetheryteId,
            startingPhase: NavigationPhase.ApproachingAethernetHub);
        Svc.Chat.Print(
            $"[Saucy] Walking to aethernet hub, then Lifestream to {travelPlan.AethernetShardName}.");
        return true;
    }

    private static void TickApproachingAethernetHub(PendingNavigation pending)
    {
        if (pending.HubAetheryteId != 0 && AetheryteHelper.IsPlayerNearAetheryte(pending.HubAetheryteId))
        {
            if (TryBeginPendingAethernet(pending))
            {
                return;
            }
        }

        if (!CanBeginLocalNavigation())
        {
            return;
        }

        if (Vnavmesh.IsMoving())
        {
            return;
        }

        if (!Vnavmesh.IsNavReady())
        {
            Vnavmesh.TryEnsureNavMeshLoading();
            return;
        }

        var hubPos = AetheryteHelper.GetAetheryteApproachPosition(pending.HubAetheryteId);
        if (hubPos == null)
        {
            Svc.Chat.PrintError("[Saucy] Could not resolve aethernet hub position. Walking to NPC instead.");
            pending.PendingAethernetShardId = 0;
            pending.PendingAethernetShardName = null;
            pending.Phase = NavigationPhase.WaitingForNavReady;
            pending.PhaseStartedUtc = DateTime.UtcNow;
            pending.AttemptMountBeforeNav = true;
            return;
        }

        if (!EzThrottler.Throttle("SaucyNavHubApproach", 2000))
        {
            return;
        }

        if (!TryEnsureMountedForNav(pending))
        {
            return;
        }

        var useFly = TravelMountHelper.ResolveUseFlying(pending.Fly);
        if (Vnavmesh.TryMoveTo(hubPos.Value, useFly, AetheryteHelper.AethernetHubUseRange) ||
            (useFly && Vnavmesh.TryMoveTo(hubPos.Value, false, AetheryteHelper.AethernetHubUseRange)))
        {
            pending.Fly = false;
            return;
        }

        if (DateTime.UtcNow - pending.PhaseStartedUtc > TimeSpan.FromSeconds(45))
        {
            Svc.Chat.PrintError("[Saucy] Could not reach the aethernet hub. Walking to NPC instead.");
            pending.PendingAethernetShardId = 0;
            pending.PendingAethernetShardName = null;
            pending.Phase = NavigationPhase.WaitingForNavReady;
            pending.PhaseStartedUtc = DateTime.UtcNow;
            pending.AttemptMountBeforeNav = true;
        }
    }

    private static bool TryStartAethernetTravel(
        MapLinkPayload location,
        Vector3 pointOnFloor,
        bool fly,
        uint targetTerritoryId,
        AetheryteHelper.TravelPlan travelPlan,
        TriadNpc? npc = null)
    {
        if (!travelPlan.HasAethernet)
        {
            return false;
        }

        if (!Lifestream.TryAethernetViaLiCommand(
            travelPlan.AethernetShardName ?? AetheryteHelper.GetAethernetShardName(travelPlan.AethernetShardId)))
        {
            Svc.Chat.Print(
                $"[Saucy] Lifestream could not start aethernet to {travelPlan.AethernetShardName}. Walking instead.");
            return false;
        }

        StopVnavIfRunning();
        BeginPending(
            location,
            pointOnFloor,
            fly,
            targetTerritoryId,
            npc,
            startingPhase: NavigationPhase.WaitingForAethernet,
            activeAethernetShardId: travelPlan.AethernetShardId,
            hubAetheryteId: travelPlan.HubAetheryteId);
        Svc.Chat.Print($"[Saucy] Lifestream: aethernet to {travelPlan.AethernetShardName}.");
        return true;
    }

    private static bool TryBeginPendingAethernet(PendingNavigation pending)
    {
        if (pending.PendingAethernetShardId == 0)
        {
            return false;
        }

        var shardId = pending.PendingAethernetShardId;
        var shardName = pending.PendingAethernetShardName;
        var hubId = pending.HubAetheryteId != 0
            ? pending.HubAetheryteId
            : AetheryteHelper.GetAethernetHubAetheryteId(shardId);

        if (hubId != 0)
        {
            pending.HubAetheryteId = hubId;
        }

        if (Lifestream.TryAethernetViaLiCommand(shardName ?? AetheryteHelper.GetAethernetShardName(shardId)))
        {
            pending.PendingAethernetShardId = 0;
            pending.PendingAethernetShardName = null;
            EnterWaitingForAethernet(pending, shardId, hubId);
            Svc.Chat.Print($"[Saucy] Lifestream: aethernet to {shardName}.");
            return true;
        }

        if (!IsInTargetTerritory(pending.TargetTerritoryId))
        {
            if (EzThrottler.Throttle("SaucyNavAethernetRetry", 3000))
            {
                Svc.Chat.Print($"[Saucy] Waiting for Lifestream aethernet to {shardName}...");
            }

            return false;
        }

        pending.PendingAethernetShardId = 0;
        pending.PendingAethernetShardName = null;
        Svc.Chat.Print(
            $"[Saucy] Lifestream could not take aethernet to {shardName}. Walking from here instead.");
        return false;
    }

    private static void EnterWaitingForAethernet(PendingNavigation pending, uint shardId, uint hubAetheryteId = 0)
    {
        StopVnavIfRunning();
        pending.Phase = NavigationPhase.WaitingForAethernet;
        pending.PhaseStartedUtc = DateTime.UtcNow;
        pending.ActiveAethernetShardId = shardId;
        pending.HubAetheryteId = hubAetheryteId != 0
            ? hubAetheryteId
            : AetheryteHelper.GetAethernetHubAetheryteId(shardId);
        pending.AethernetStartPosition = Player.Position;
        pending.AethernetShardPosition = AetheryteHelper.GetAethernetShardWorldPosition(shardId);
        pending.AethernetSeenBusy = false;
        pending.AethernetBusyClearedUtc = null;
    }

    private static bool IsLifestreamTravelComplete(PendingNavigation pending)
    {
        if (pending.Phase == NavigationPhase.WaitingForAethernet)
        {
            return IsAethernetTravelComplete(pending);
        }

        if (Lifestream.IsBusyNow())
        {
            return false;
        }

        if (!Player.Interactable || IsBetweenAreas() || Player.IsAnimationLocked)
        {
            return false;
        }

        if (pending.RouteExecution != null && pending.Phase == NavigationPhase.WaitingForLifestream)
        {
            var step = pending.RouteExecution.Route.Steps[pending.RouteExecution.StepIndex];
            return step.Kind == MultiAreaRouteStepKind.Teleport &&
                   MultiAreaRouteExecutor.IsTeleportStepComplete(pending.RouteExecution, step);
        }

        if (pending.PendingAethernetShardId != 0)
        {
            if (pending.HubAetheryteId != 0)
            {
                var hubTerritoryId = AetheryteHelper.GetAetheryteTerritoryId(pending.HubAetheryteId);
                if (hubTerritoryId != 0 && Svc.ClientState.TerritoryType != hubTerritoryId)
                {
                    return false;
                }
            }

            return true;
        }

        if (pending.ExpectedPostTeleportTerritoryId != 0 &&
            pending.ExpectedPostTeleportTerritoryId != pending.TargetTerritoryId)
        {
            return Svc.ClientState.TerritoryType == pending.ExpectedPostTeleportTerritoryId;
        }

        return Svc.ClientState.TerritoryType == pending.TargetTerritoryId;
    }

    private static bool IsAethernetTravelComplete(PendingNavigation pending)
    {
        if (Lifestream.IsBusyNow() || Vnavmesh.IsMoving())
        {
            pending.AethernetSeenBusy = true;
            pending.AethernetBusyClearedUtc = null;
            return false;
        }

        if (!pending.AethernetSeenBusy)
        {
            if (DateTime.UtcNow - pending.PhaseStartedUtc > AethernetStartupTimeout)
            {
                Svc.Chat.PrintError("[Saucy] Aethernet travel did not start. Walking instead.");
                return true;
            }

            return false;
        }

        if (pending.AethernetBusyClearedUtc == null)
        {
            pending.AethernetBusyClearedUtc = DateTime.UtcNow;
            return false;
        }

        if (DateTime.UtcNow - pending.AethernetBusyClearedUtc.Value < AethernetSettleDelay)
        {
            return false;
        }

        if (!CanBeginLocalNavigation())
        {
            return false;
        }

        if (pending.AethernetStartPosition != null && pending.AethernetShardPosition != null)
        {
            var movedFromStart = Vector3.Distance(pending.AethernetStartPosition.Value, Player.Position);
            var distToShard = Vector3.Distance(Player.Position, pending.AethernetShardPosition.Value);
            if (movedFromStart < AethernetMinMoveDistance && distToShard > AethernetNearShardDistance)
            {
                return false;
            }
        }

        return true;
    }

    private static uint ResolveTargetTerritoryId(MapLinkPayload location, TriadNpc? npc)
    {
        if (npc != null &&
            GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out var info) &&
            info.TerritoryId != 0)
        {
            return info.TerritoryId;
        }

        return location.TerritoryType.RowId;
    }

    private static bool IsInTargetTerritory(uint targetTerritoryId)
    {
        var current = Svc.ClientState.TerritoryType;
        if (current == targetTerritoryId)
        {
            return true;
        }

        var route = MultiAreaRouteRegistry.FindRouteForTerritory(targetTerritoryId);
        return route != null && route.IsInDestinationTerritory(current, targetTerritoryId);
    }

    private static void SuppressVnavDuringLifestream()
    {
        if (Vnavmesh.IsMoving())
        {
            StopVnavIfRunning();
        }
    }
}
