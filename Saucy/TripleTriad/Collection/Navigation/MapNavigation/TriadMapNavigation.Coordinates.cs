using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.GameHelpers;
using Lumina.Excel.Sheets;
using Saucy.IPC;
using Saucy.TripleTriad.Data;
using System;
using System.Numerics;
using Map = Lumina.Excel.Sheets.Map;

namespace Saucy.TripleTriad;

internal static partial class TriadMapNavigation
{
    private const float MaxTrustedNpcHeightDelta = 10f;
    private const float MaxNpcPathSnapHorizontalDrift = 2f;

    private static void RefreshPendingDestination(PendingNavigation pending) =>
        pending.Destination = ResolvePostRouteDestination(pending);

    internal static Vector3? ResolveDestination(MapLinkPayload location, TriadNpc? npc = null)
    {
        if (npc != null &&
            GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out var npcInfo) &&
            TryResolveNpcWorldPosition(npcInfo) is { } npcWorldPos)
        {
            return ResolveNpcPathPoint(npcWorldPos);
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
                return ResolveNpcPathPoint(worldPos);
            }
        }

        var fromLink = GetWorldPositionFromMapLink(location);
        return fromLink == null ? null : ResolveNpcPathPoint(fromLink.Value);
    }

    private static Vector3 ResolvePostRouteDestination(PendingNavigation pending)
    {
        if (ResolveLiveTriadNpcPosition(pending.Npc) is { } livePos)
        {
            return ResolveNpcPathPoint(livePos);
        }

        if (ResolveDestination(pending.Location, pending.Npc) is { } resolved)
        {
            return resolved;
        }

        var route = MultiAreaRouteRegistry.FindRoute(pending.Location);
        if (route is { InteriorMapId: not 0 })
        {
            var fromMap = GetWorldPositionFromMap(route.InteriorMapId, route.InteriorMapX, route.InteriorMapY);
            if (fromMap != null)
            {
                return fromMap.Value;
            }
        }

        return pending.Destination;
    }

    internal static Vector3 SnapDestinationNearPlayerFloor(Vector3 position)
    {
        var indoor = IsIndoorTerritory(Svc.ClientState.TerritoryType);
        var playerFloor = Vnavmesh.TryGetPointOnFloor(Player.Position, indoor) ?? Player.Position;
        var candidate = new Vector3(position.X, playerFloor.Y, position.Z);
        var snapped = Vnavmesh.TryGetPointOnFloor(candidate, indoor, 2f);
        if (snapped != null && MathF.Abs(snapped.Value.Y - playerFloor.Y) <= 1.5f)
        {
            return snapped.Value;
        }

        return candidate;
    }

    internal static Vector3 ResolveNpcPathPoint(Vector3 position)
    {
        var indoor = IsIndoorTerritory(Svc.ClientState.TerritoryType);

        // Live/baked NPC coords already carry world height — only accept floor snaps that stay near XZ.
        if (position.Y > 1f)
        {
            var snapped = Vnavmesh.TryGetPointOnFloor(position, indoor, 4f);
            if (snapped != null &&
                snapped.Value.Y >= position.Y - 1.5f &&
                HorizontalDistanceXZ(snapped.Value, position) <= MaxNpcPathSnapHorizontalDrift)
            {
                return snapped.Value;
            }

            return position;
        }

        // Map links arrive at Y=0. Snap at destination XZ using current player altitude when flying.
        var playerFloor = Vnavmesh.TryGetPointOnFloor(Player.Position, indoor) ?? Player.Position;
        var referenceY = Svc.Condition[ConditionFlag.Mounted]
            ? Player.Position.Y
            : playerFloor.Y;
        var candidate = new Vector3(position.X, referenceY, position.Z);
        var floor = Vnavmesh.TryGetPointOnFloor(candidate, indoor, 4f);
        if (floor != null)
        {
            if (!Svc.Condition[ConditionFlag.Mounted] && floor.Value.Y < referenceY - 1.5f)
            {
                return candidate;
            }

            if (HorizontalDistanceXZ(floor.Value, position) <= MaxNpcPathSnapHorizontalDrift)
            {
                return floor.Value;
            }

            return candidate;
        }

        if (MathF.Abs(position.Y - referenceY) <= MaxTrustedNpcHeightDelta)
        {
            floor = Vnavmesh.TryGetPointOnFloor(position, indoor, 4f);
            if (floor != null && HorizontalDistanceXZ(floor.Value, position) <= MaxNpcPathSnapHorizontalDrift)
            {
                return floor.Value;
            }
        }

        return candidate;
    }

    private static Vector3? TryResolveNpcWorldPosition(GameNpcInfo npcInfo)
    {
        if (npcInfo.WorldPosition is not { } worldPos)
        {
            return npcInfo.Location == null ? null : GetWorldPositionFromMapLink(npcInfo.Location);
        }

        if (npcInfo.Location == null)
        {
            return worldPos;
        }

        var fromLink = GetWorldPositionFromMapLink(npcInfo.Location);
        if (fromLink == null)
        {
            return worldPos;
        }

        // Keep the map-link XZ shown in Saucy UI; keep Level-sheet Y for aerial platforms.
        return new(fromLink.Value.X, worldPos.Y, fromLink.Value.Z);
    }

    private static float HorizontalDistanceXZ(Vector3 a, Vector3 b) =>
        Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    internal static Vector3? GetWorldPositionFromMap(uint mapId, float mapX, float mapY)
    {
        var map = Svc.Data.GetExcelSheet<Map>()?.GetRowOrDefault(mapId);
        if (map == null)
        {
            return null;
        }

        var worldXz = MapToWorld(new(mapX, mapY), map.Value);
        return new Vector3(worldXz.X, 0f, worldXz.Y);
    }

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

        var route = MultiAreaRouteRegistry.FindRoute(a) ?? MultiAreaRouteRegistry.FindRoute(b) ??
            MultiAreaRouteRegistry.FindRouteForTerritory(territoryA) ??
            MultiAreaRouteRegistry.FindRouteForTerritory(territoryB);
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

    private static bool IsIndoorTerritory(uint territoryId)
    {
        var row = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        return row != null && !row.Value.Mount;
    }
    private static Vector2 MapToWorld(Vector2 mapCoordinates, Map map) =>
        MapToWorld(mapCoordinates, map.OffsetX, map.OffsetY, map.SizeFactor);

    private static Vector2 MapToWorld(Vector2 mapCoordinates, int xOffset, int yOffset, uint scale) =>
        new(
            ConvertMapCoordToWorldCoord(mapCoordinates.X, scale, xOffset),
            ConvertMapCoordToWorldCoord(mapCoordinates.Y, scale, yOffset));

    private static float ConvertMapCoordToWorldCoord(float mapCoord, uint scale, int offset) =>
        (mapCoord - 1.0f - (2048f / scale) - (0.02f * offset)) / 0.02f;
}
