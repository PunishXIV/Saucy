using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.GameHelpers;
using Saucy.IPC;
using System;
using System.Globalization;
using System.Numerics;
using Map = Lumina.Excel.Sheets.Map;
namespace Saucy.TripleTriad;

/// <summary>
///     Map-link navigation: Lifestream teleport/aethernet, optional multi-area routes, then vnavmesh to the NPC.
/// </summary>
internal static class TriadMapNavigation
{
    private static readonly bool NavigationDebug = false;
    private static readonly TimeSpan DefaultNavigationTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan AethernetStartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AethernetSettleDelay = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan PostRouteNavReadyTimeout = TimeSpan.FromSeconds(45);
    private static readonly float AethernetMinMoveDistance = 3f;
    private static readonly float AethernetNearShardDistance = 8f;

    private static PendingNavigation? _pending;
    private static bool _frameworkSubscribed;

    public static void HandleMapClick(MapLinkPayload location, TriadNpc? npc = null)
    {
        if (npc != null)
        {
            TTSolver.OnNpcSelected(npc, [], true);
        }

        if (TryBeginNavigation(location, npc: npc))
        {
            return;
        }

        Svc.GameGui.OpenMapWithMapLink(location);
    }

    public static void Tick()
    {
        var pending = _pending;
        if (pending == null)
        {
            return;
        }

        var timeout = pending.RouteExecution?.Route.Timeout ?? DefaultNavigationTimeout;
        if (DateTime.UtcNow - pending.StartedUtc > timeout)
        {
            Svc.Chat.PrintError("[Saucy] Navigation timed out.");
            ClearPending();
            return;
        }

        switch (pending.Phase)
        {
            case NavigationPhase.WaitingForLifestream:
                SuppressVnavDuringLifestream();
                if (!IsLifestreamTravelComplete(pending))
                {
                    return;
                }

                if (pending.RouteExecution != null)
                {
                    pending.Phase = NavigationPhase.ExecutingRoute;
                    pending.PhaseStartedUtc = DateTime.UtcNow;
                    return;
                }

                if (TryBeginPendingAethernet(pending))
                {
                    return;
                }

                pending.Phase = NavigationPhase.WaitingForNavReady;
                pending.PhaseStartedUtc = DateTime.UtcNow;
                return;

            case NavigationPhase.ExecutingRoute:
                if (pending.RouteExecution == null)
                {
                    ClearPending();
                    return;
                }

                if (pending.RouteExecution.Failed)
                {
                    ClearPending();
                    return;
                }

                if (MultiAreaRouteExecutor.Tick(pending.RouteExecution))
                {
                    ContinueAfterZoneArrival(pending);
                }

                return;

            case NavigationPhase.WaitingForAethernet:
                SuppressVnavDuringLifestream();
                if (!IsLifestreamTravelComplete(pending))
                {
                    return;
                }

                DebugLog(pending, "Aethernet complete, waiting for nav ready.");
                pending.PendingAethernetShardId = 0;
                pending.PendingAethernetShardName = null;
                pending.Phase = NavigationPhase.WaitingForNavReady;
                pending.PhaseStartedUtc = DateTime.UtcNow;
                return;

            case NavigationPhase.WaitingForNavReady:
                if (!pending.ArrivedViaMultiAreaRoute && (Lifestream.IsBusyNow() || Vnavmesh.IsMoving()))
                {
                    return;
                }

                if (!Player.Interactable || IsBetweenAreas())
                {
                    return;
                }

                if (!Vnavmesh.IsNavReady())
                {
                    var navReadyTimeout = pending.ArrivedViaMultiAreaRoute
                        ? PostRouteNavReadyTimeout
                        : TimeSpan.FromSeconds(15);
                    if (DateTime.UtcNow - pending.PhaseStartedUtc > navReadyTimeout)
                    {
                        Svc.Chat.PrintError("[Saucy] vnavmesh is not ready for this zone yet.");
                        ClearPending();
                    }

                    return;
                }

                if (!TryStartVnav(pending))
                {
                    ClearPending();
                }
                else
                {
                    ClearPending();
                }

                return;
        }
    }

    private static bool TryBeginNavigation(MapLinkPayload location, bool fly = false, TriadNpc? npc = null)
    {
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
        var targetTerritoryId = location.TerritoryType.RowId;
        var inTargetTerritory = targetTerritoryId == Svc.ClientState.TerritoryType;

        if (!Lifestream.IsInstalled)
        {
            if (!inTargetTerritory)
            {
                Svc.Chat.Print(
                    $"[Saucy] {location.PlaceName} is in another zone. Install Lifestream to teleport there.");
                return false;
            }

            return TryStartVnavImmediate(location, pointOnFloor, fly);
        }

        if (Lifestream.IsBusyNow())
        {
            Svc.Chat.Print("[Saucy] Lifestream is busy. Try again in a moment.");
            return false;
        }

        var route = MultiAreaRouteRegistry.FindRoute(location);
        if (route != null && !inTargetTerritory)
        {
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
            if (travelPlan.HasAethernet &&
                TryStartAethernetTravel(location, pointOnFloor, fly, targetTerritoryId, travelPlan))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(travelPlan.AethernetSkipReason))
            {
                Svc.Chat.Print($"[Saucy] Walking: {travelPlan.AethernetSkipReason}.");
            }

            return TryStartVnavImmediate(location, pointOnFloor, fly);
        }

        if (!travelPlan.HasTeleport)
        {
            return TryStartVnavImmediate(location, pointOnFloor, fly);
        }

        if (!Lifestream.TryTeleport(travelPlan.TeleportAetheryteId))
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
            aethernetShardId: travelPlan.AethernetShardId,
            aethernetShardName: travelPlan.AethernetShardName);

        if (travelPlan.HasAethernet)
        {
            Svc.Chat.Print(
                $"[Saucy] Teleporting to {location.PlaceName}, then aethernet to {travelPlan.AethernetShardName}, then moving to {FormatMapCoordinates(location)}.");
        }
        else
        {
            Svc.Chat.Print(
                $"[Saucy] Teleporting to {location.PlaceName}, then moving to {FormatMapCoordinates(location)}.");
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
        if (ResolveDestination(pending.Location, pending.Npc) is { } destination)
        {
            pending.Destination = destination;
        }

        pending.Phase = NavigationPhase.WaitingForNavReady;
        pending.PhaseStartedUtc = DateTime.UtcNow;
        Svc.Chat.Print(
            $"[Saucy] Arrived in {pending.Location.PlaceName}. Moving to {FormatDestination(pending.Destination)}...");
        DebugLog(pending, $"Post-route destination: {FormatDestination(pending.Destination)}");
    }

    private static bool TryStartAethernetTravel(
        MapLinkPayload location,
        Vector3 pointOnFloor,
        bool fly,
        uint targetTerritoryId,
        AetheryteHelper.TravelPlan travelPlan)
    {
        if (!travelPlan.HasAethernet)
        {
            return false;
        }

        if (!Lifestream.TryAethernetTeleportById(travelPlan.AethernetShardId))
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
            startingPhase: NavigationPhase.WaitingForAethernet,
            activeAethernetShardId: travelPlan.AethernetShardId);
        Svc.Chat.Print(
            $"[Saucy] Taking aethernet to {travelPlan.AethernetShardName}, then moving to {FormatMapCoordinates(location)}.");
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
        pending.PendingAethernetShardId = 0;
        pending.PendingAethernetShardName = null;

        if (!Lifestream.TryAethernetTeleportById(shardId))
        {
            Svc.Chat.Print(
                $"[Saucy] Could not take aethernet to {shardName}. Walking from here instead.");
            return false;
        }

        EnterWaitingForAethernet(pending, shardId);
        Svc.Chat.Print($"[Saucy] Taking aethernet to {shardName}.");
        return true;
    }

    private static void EnterWaitingForAethernet(PendingNavigation pending, uint shardId)
    {
        StopVnavIfRunning();
        pending.Phase = NavigationPhase.WaitingForAethernet;
        pending.PhaseStartedUtc = DateTime.UtcNow;
        pending.ActiveAethernetShardId = shardId;
        pending.AethernetStartPosition = Player.Position;
        pending.AethernetShardPosition = AetheryteHelper.GetAethernetShardWorldPosition(shardId);
        pending.AethernetSeenBusy = false;
        pending.AethernetBusyClearedUtc = null;
        DebugLog(pending, $"Waiting for aethernet shard {shardId}.");
    }

    private static void BeginPending(
        MapLinkPayload location,
        Vector3 destination,
        bool fly,
        uint targetTerritoryId,
        TriadNpc? npc = null,
        MultiAreaRouteExecutor.RouteExecution? routeExecution = null,
        uint aethernetShardId = 0,
        string? aethernetShardName = null,
        uint activeAethernetShardId = 0,
        NavigationPhase startingPhase = NavigationPhase.WaitingForLifestream)
    {
        if (startingPhase is NavigationPhase.WaitingForLifestream or NavigationPhase.WaitingForAethernet)
        {
            StopVnavIfRunning();
        }

        _pending = new()
        {
            Location = location,
            Destination = destination,
            Fly = fly,
            Npc = npc,
            TargetTerritoryId = targetTerritoryId,
            RouteExecution = routeExecution,
            PendingAethernetShardId = aethernetShardId,
            PendingAethernetShardName = aethernetShardName,
            ActiveAethernetShardId = activeAethernetShardId,
            AethernetStartPosition = activeAethernetShardId != 0 ? Player.Position : null,
            AethernetShardPosition = activeAethernetShardId != 0
                ? AetheryteHelper.GetAethernetShardWorldPosition(activeAethernetShardId)
                : null,
            Phase = startingPhase,
            StartedUtc = DateTime.UtcNow,
            PhaseStartedUtc = DateTime.UtcNow,
            AethernetSeenBusy = false,
            AethernetBusyClearedUtc = null
        };

        EnsureFrameworkSubscription();
    }

    private static bool TryStartVnavImmediate(MapLinkPayload location, Vector3 destination, bool fly)
    {
        if (!Vnavmesh.IsNavReady())
        {
            Svc.Chat.Print("[Saucy] vnavmesh is not ready for this zone yet.");
            return false;
        }

        if (!TryStartVnav(new()
        {
            Location = location,
            Destination = destination,
            Fly = fly,
            TargetTerritoryId = location.TerritoryType.RowId,
            Npc = null
        }))
        {
            return false;
        }

        return true;
    }

    private static bool TryStartVnav(PendingNavigation pending)
    {
        if (_pending != null &&
            ReferenceEquals(_pending, pending) &&
            pending.Phase != NavigationPhase.WaitingForNavReady)
        {
            DebugLog(pending, "Blocked vnav start while travel still active.");
            return false;
        }

        if (Lifestream.IsBusyNow() || Vnavmesh.IsMoving())
        {
            DebugLog(pending, "Blocked vnav start while Lifestream or vnav is busy.");
            return false;
        }

        var pointOnFloor = Vnavmesh.TryGetPointOnFloor(pending.Destination) ?? pending.Destination;

        if (!Vnavmesh.TryPathfindAndMoveTo(pointOnFloor, pending.Fly))
        {
            Svc.Chat.PrintError("[Saucy] vnavmesh could not start movement.");
            return false;
        }

        if (!Vnavmesh.IsMoving())
        {
            Svc.Chat.PrintError("[Saucy] vnavmesh accepted the path but did not start moving.");
            return false;
        }

        Svc.Chat.Print(
            $"[Saucy] Moving to {pending.Location.PlaceName} {FormatDestination(pending.Destination)}.");
        DebugLog(pending, $"vnav started toward {FormatDestination(pointOnFloor)}");
        return true;
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

        return Svc.ClientState.TerritoryType == pending.TargetTerritoryId;
    }

    private static bool IsAethernetTravelComplete(PendingNavigation pending)
    {
        if (Lifestream.IsBusyNow() || Vnavmesh.IsMoving())
        {
            pending.AethernetSeenBusy = true;
            pending.AethernetBusyClearedUtc = null;
            DebugLog(pending, "Aethernet still active (Lifestream or vnav busy).");
            return false;
        }

        if (!pending.AethernetSeenBusy)
        {
            if (DateTime.UtcNow - pending.PhaseStartedUtc > AethernetStartupTimeout)
            {
                Svc.Chat.PrintError("[Saucy] Aethernet travel did not start. Walking instead.");
                return true;
            }

            DebugLog(pending, "Waiting for Lifestream aethernet task to start.");
            return false;
        }

        if (pending.AethernetBusyClearedUtc == null)
        {
            pending.AethernetBusyClearedUtc = DateTime.UtcNow;
            DebugLog(pending, "Lifestream idle; waiting for aethernet settle.");
            return false;
        }

        if (DateTime.UtcNow - pending.AethernetBusyClearedUtc.Value < AethernetSettleDelay)
        {
            return false;
        }

        if (!Player.Interactable || IsBetweenAreas() || Player.IsAnimationLocked)
        {
            return false;
        }

        if (pending.AethernetStartPosition != null && pending.AethernetShardPosition != null)
        {
            var movedFromStart = Vector3.Distance(pending.AethernetStartPosition.Value, Player.Position);
            var distToShard = Vector3.Distance(Player.Position, pending.AethernetShardPosition.Value);
            if (movedFromStart < AethernetMinMoveDistance && distToShard > AethernetNearShardDistance)
            {
                DebugLog(pending,
                    $"Still at aethernet start (moved {movedFromStart:F1}y, shard {distToShard:F1}y away).");
                return false;
            }
        }

        return true;
    }

    private static void SuppressVnavDuringLifestream()
    {
        if (Vnavmesh.IsMoving())
        {
            DebugLog(_pending, "Stopping vnav during Lifestream travel.");
            StopVnavIfRunning();
        }
    }

    private static void StopVnavIfRunning()
    {
        if (!Vnavmesh.IsMoving())
        {
            return;
        }

        Vnavmesh.StopPath();
    }

    private static void DebugLog(PendingNavigation? pending, string message)
    {
        if (!NavigationDebug)
        {
            return;
        }

        var phase = pending?.Phase.ToString() ?? "none";
        Svc.Chat.Print($"[Saucy nav] {phase}: {message}");
    }

    private static bool IsBetweenAreas() => Svc.Condition[ConditionFlag.BetweenAreas];

    private static void EnsureFrameworkSubscription()
    {
        if (_frameworkSubscribed)
        {
            return;
        }

        Svc.Framework.Update += OnFrameworkUpdate;
        _frameworkSubscribed = true;
    }

    private static void OnFrameworkUpdate(IFramework _)
    {
        Tick();

        if (_pending == null && _frameworkSubscribed)
        {
            Svc.Framework.Update -= OnFrameworkUpdate;
            _frameworkSubscribed = false;
        }
    }

    private static void ClearPending() => _pending = null;

    private static string FormatMapCoordinates(MapLinkPayload location) =>
        $"({location.XCoord.ToString("0.0", CultureInfo.InvariantCulture)}, {location.YCoord.ToString("0.0", CultureInfo.InvariantCulture)})";

    internal static Vector3? ResolveDestination(MapLinkPayload location, TriadNpc? npc = null)
    {
        if (npc != null &&
            GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out var npcInfo) &&
            npcInfo.WorldPosition is { } npcWorldPos)
        {
            return npcWorldPos;
        }

        foreach (var kvp in GameNpcDB.Get().mapNpcs)
        {
            var info = kvp.Value;
            if (info.WorldPosition is not { } worldPos || info.Location == null)
            {
                continue;
            }

            if (LocationsMatch(info.Location, location))
            {
                return worldPos;
            }
        }

        return GetWorldPositionFromMapLink(location);
    }

    internal static Vector3? GetWorldPosition(MapLinkPayload location) => GetWorldPositionFromMapLink(location);

    private static Vector3? GetWorldPositionFromMapLink(MapLinkPayload location)
    {
        var map = location.Map.Value;
        var worldXz = MapToWorld(new(location.XCoord, location.YCoord), map);
        return new Vector3(worldXz.X, 0f, worldXz.Y);
    }

    private static bool LocationsMatch(MapLinkPayload a, MapLinkPayload b)
    {
        if (a.Map.RowId != b.Map.RowId ||
            MathF.Abs(a.XCoord - b.XCoord) >= 0.05f ||
            MathF.Abs(a.YCoord - b.YCoord) >= 0.05f)
        {
            return false;
        }

        var territoryA = a.TerritoryType.RowId;
        var territoryB = b.TerritoryType.RowId;
        if (territoryA == territoryB)
        {
            return true;
        }

        var route = MultiAreaRouteRegistry.FindRoute(a) ?? MultiAreaRouteRegistry.FindRoute(b);
        if (route?.ArrivalTerritoryIds is not { Count: > 0 } arrivalIds)
        {
            return false;
        }

        var aListed = false;
        var bListed = false;
        foreach (var id in arrivalIds)
        {
            if (id == territoryA)
            {
                aListed = true;
            }

            if (id == territoryB)
            {
                bListed = true;
            }
        }

        return aListed && bListed;
    }

    private static string FormatDestination(Vector3 destination) =>
        $"({destination.X.ToString("F1", CultureInfo.InvariantCulture)}, {destination.Y.ToString("F1", CultureInfo.InvariantCulture)}, {destination.Z.ToString("F1", CultureInfo.InvariantCulture)})";

    /// <summary>From Henchman GeneralHelpers.MapToWorld.</summary>
    private static Vector2 MapToWorld(Vector2 mapCoordinates, Map map) =>
        MapToWorld(mapCoordinates, map.OffsetX, map.OffsetY, map.SizeFactor);

    private static Vector2 MapToWorld(Vector2 mapCoordinates, int xOffset, int yOffset, uint scale) =>
        new(
            ConvertMapCoordToWorldCoord(mapCoordinates.X, scale, xOffset),
            ConvertMapCoordToWorldCoord(mapCoordinates.Y, scale, yOffset));

    private static float ConvertMapCoordToWorldCoord(float mapCoord, uint scale, int offset) =>
        (mapCoord - 1.0f - (2048f / scale) - (0.02f * offset)) / 0.02f;

    private enum NavigationPhase
    {
        WaitingForLifestream,
        ExecutingRoute,
        WaitingForAethernet,
        WaitingForNavReady
    }

    private sealed class PendingNavigation
    {
        public uint ActiveAethernetShardId;
        public DateTime? AethernetBusyClearedUtc;
        public bool AethernetSeenBusy;
        public Vector3? AethernetShardPosition;
        public Vector3? AethernetStartPosition;
        public bool ArrivedViaMultiAreaRoute;
        public required Vector3 Destination;
        public required bool Fly;
        public required MapLinkPayload Location;
        public TriadNpc? Npc;
        public uint PendingAethernetShardId;
        public string? PendingAethernetShardName;
        public NavigationPhase Phase;
        public DateTime PhaseStartedUtc;
        public MultiAreaRouteExecutor.RouteExecution? RouteExecution;
        public DateTime StartedUtc;
        public required uint TargetTerritoryId;
    }
}
