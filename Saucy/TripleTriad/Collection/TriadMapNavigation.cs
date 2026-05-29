using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons;
using ECommons.GameHelpers;
using Lumina.Excel.Sheets;
using Saucy.IPC;
using System;
using System.Numerics;

namespace Saucy.TripleTriad;

/// <summary>
/// Map-link navigation: Lifestream to nearest aetheryte, then vnavmesh to the NPC.
/// </summary>
internal static class TriadMapNavigation
{
    private const float SkipTeleportDistance = 25f;
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(90);

    private static PendingNavigation? _pending;
    private static bool _frameworkSubscribed;

    public static void HandleMapClick(MapLinkPayload location)
    {
        if (TryBeginNavigation(location))
            return;

        Svc.GameGui.OpenMapWithMapLink(location);
    }

    public static void Tick()
    {
        var pending = _pending;
        if (pending == null)
            return;

        if (DateTime.UtcNow - pending.StartedUtc > NavigationTimeout)
        {
            Svc.Chat.PrintError("[Saucy] Navigation timed out.");
            ClearPending();
            return;
        }

        switch (pending.Phase)
        {
            case NavigationPhase.WaitingForTeleport:
                if (!IsTravelComplete(pending.TargetTerritoryId))
                    return;

                pending.Phase = NavigationPhase.WaitingForNavReady;
                pending.PhaseStartedUtc = DateTime.UtcNow;
                return;

            case NavigationPhase.WaitingForNavReady:
                if (!Player.Interactable || IsBetweenAreas())
                    return;

                VnavmeshInterop.Refresh();
                if (!VnavmeshInterop.IsNavReady())
                {
                    if (DateTime.UtcNow - pending.PhaseStartedUtc > TimeSpan.FromSeconds(15))
                    {
                        Svc.Chat.PrintError("[Saucy] vnavmesh is not ready for this zone yet.");
                        ClearPending();
                    }

                    return;
                }

                if (!TryStartVnav(pending))
                    ClearPending();
                else
                    ClearPending();

                return;
        }
    }

    private static bool TryBeginNavigation(MapLinkPayload location, bool fly = false)
    {
        VnavmeshInterop.Refresh();
        LifestreamInterop.Refresh();

        if (!VnavmeshInterop.IsInstalled)
        {
            Svc.Chat.Print("[Saucy] Install vnavmesh to path to NPCs from Saucy.");
            return false;
        }

        var destination = GetWorldPosition(location);
        if (destination == null)
        {
            Svc.Chat.PrintError("[Saucy] Could not resolve NPC map coordinates.");
            return false;
        }

        var pointOnFloor = destination.Value;
        var targetTerritoryId = location.TerritoryType.RowId;
        var inTargetTerritory = targetTerritoryId == Svc.ClientState.TerritoryType;
        var closeEnough = inTargetTerritory &&
                          Vector3.Distance(Player.Position, pointOnFloor) <= SkipTeleportDistance;

        if (closeEnough)
            return TryStartVnavImmediate(location, pointOnFloor, fly);

        if (!LifestreamInterop.IsInstalled)
        {
            if (!inTargetTerritory)
            {
                Svc.Chat.Print(
                    $"[Saucy] {location.PlaceName} is in another zone. Install Lifestream to teleport there.");
                return false;
            }

            return TryStartVnavImmediate(location, pointOnFloor, fly);
        }

        var aetheryteId = AetheryteHelper.FindClosestUnlockedAetheryte(targetTerritoryId, pointOnFloor);
        if (aetheryteId == 0)
        {
            if (!inTargetTerritory)
            {
                Svc.Chat.Print(
                    $"[Saucy] No unlocked aetheryte found for {location.PlaceName}. Opening map.");
                return false;
            }

            return TryStartVnavImmediate(location, pointOnFloor, fly);
        }

        if (LifestreamInterop.IsBusy())
        {
            Svc.Chat.Print("[Saucy] Lifestream is busy. Try again in a moment.");
            return false;
        }

        if (!LifestreamInterop.TryTeleport(aetheryteId))
        {
            Svc.Chat.PrintError("[Saucy] Lifestream could not start teleport.");
            return false;
        }

        _pending = new PendingNavigation
        {
            Location = location,
            Destination = pointOnFloor,
            Fly = fly,
            TargetTerritoryId = targetTerritoryId,
            Phase = NavigationPhase.WaitingForTeleport,
            StartedUtc = DateTime.UtcNow,
            PhaseStartedUtc = DateTime.UtcNow,
        };

        EnsureFrameworkSubscription();
        Svc.Chat.Print(
            $"[Saucy] Teleporting to {location.PlaceName}, then moving to {location.CoordinateString}.");
        return true;
    }

    private static bool TryStartVnavImmediate(MapLinkPayload location, Vector3 destination, bool fly)
    {
        if (!VnavmeshInterop.IsNavReady())
        {
            Svc.Chat.Print("[Saucy] vnavmesh is not ready for this zone yet.");
            return false;
        }

        if (!TryStartVnav(new PendingNavigation
            {
                Location = location,
                Destination = destination,
                Fly = fly,
                TargetTerritoryId = location.TerritoryType.RowId,
            }))
            return false;

        return true;
    }

    private static bool TryStartVnav(PendingNavigation pending)
    {
        var pointOnFloor = VnavmeshInterop.TryGetPointOnFloor(pending.Destination) ?? pending.Destination;

        if (!VnavmeshInterop.TryPathfindAndMoveTo(pointOnFloor, pending.Fly))
        {
            Svc.Chat.PrintError("[Saucy] vnavmesh could not start movement.");
            return false;
        }

        Svc.Chat.Print(
            $"[Saucy] Moving to {pending.Location.PlaceName} {pending.Location.CoordinateString}.");
        return true;
    }

    private static bool IsTravelComplete(uint targetTerritoryId)
    {
        if (LifestreamInterop.IsBusy())
            return false;

        if (Svc.ClientState.TerritoryType != targetTerritoryId)
            return false;

        return Player.Interactable && !IsBetweenAreas() && !Player.IsAnimationLocked;
    }

    private static bool IsBetweenAreas() => Svc.Condition[ConditionFlag.BetweenAreas];

    private static void EnsureFrameworkSubscription()
    {
        if (_frameworkSubscribed)
            return;

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

    internal static Vector3? GetWorldPosition(MapLinkPayload location)
    {
        var map = location.Map.Value;
        var worldXz = MapToWorld(new Vector2(location.XCoord, location.YCoord), map);
        return new Vector3(worldXz.X, 0f, worldXz.Y);
    }

    /// <summary>From Henchman GeneralHelpers.MapToWorld.</summary>
    private static Vector2 MapToWorld(Vector2 mapCoordinates, Lumina.Excel.Sheets.Map map) =>
        MapToWorld(mapCoordinates, map.OffsetX, map.OffsetY, map.SizeFactor);

    private static Vector2 MapToWorld(Vector2 mapCoordinates, int xOffset, int yOffset, uint scale) =>
        new(
            ConvertMapCoordToWorldCoord(mapCoordinates.X, scale, xOffset),
            ConvertMapCoordToWorldCoord(mapCoordinates.Y, scale, yOffset));

    private static float ConvertMapCoordToWorldCoord(float mapCoord, uint scale, int offset) =>
        (mapCoord - 1.0f - (2048f / scale) - (0.02f * offset)) / 0.02f;

    private enum NavigationPhase
    {
        WaitingForTeleport,
        WaitingForNavReady,
    }

    private sealed class PendingNavigation
    {
        public required MapLinkPayload Location;
        public required Vector3 Destination;
        public required bool Fly;
        public required uint TargetTerritoryId;
        public NavigationPhase Phase;
        public DateTime StartedUtc;
        public DateTime PhaseStartedUtc;
    }
}
