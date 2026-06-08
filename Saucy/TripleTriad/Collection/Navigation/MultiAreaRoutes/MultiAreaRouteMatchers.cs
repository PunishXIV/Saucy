using Dalamud.Game.Text.SeStringHandling.Payloads;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
namespace Saucy.TripleTriad;

internal static class MultiAreaRouteMatchers
{
    private static readonly Dictionary<string, HashSet<uint>> PlaceNameRowCache = [];

    internal static bool MatchesArrivalTerritory(MapLinkPayload location, uint[] territoryIds)
    {
        if (ContainsTerritory(location.TerritoryType.RowId, territoryIds))
        {
            return true;
        }

        return ContainsTerritory(location.Map.Value.TerritoryType.RowId, territoryIds);
    }

    internal static bool MatchesMapId(MapLinkPayload location, uint mapId) =>
        mapId != 0 && location.Map.RowId == mapId;

    internal static bool MatchesPlaceNameRow(MapLinkPayload location, uint[] territoryIds)
    {
        var placeRowId = TryGetMapPlaceNameRowId(location.Map.RowId);
        if (placeRowId == 0)
        {
            return false;
        }

        return ResolvePlaceNameRowIdsForTerritories(territoryIds).Contains(placeRowId);
    }

    internal static bool MatchesMapPlaceNameLinkedToTerritories(MapLinkPayload location, uint[] territoryIds)
    {
        var mapSheet = Svc.Data.GetExcelSheet<Map>();
        if (mapSheet == null)
        {
            return false;
        }

        var sourceMap = mapSheet.GetRowOrDefault(location.Map.RowId);
        if (sourceMap == null)
        {
            return false;
        }

        var placeRowId = sourceMap.Value.PlaceName.RowId;
        if (placeRowId == 0)
        {
            return false;
        }

        foreach (var row in mapSheet)
        {
            if (!ContainsTerritory(row.TerritoryType.RowId, territoryIds))
            {
                continue;
            }

            if (row.PlaceName.RowId == placeRowId)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool MatchesDestination(
        MapLinkPayload location,
        uint[] territoryIds,
        uint interiorMapId = 0) =>
        MatchesArrivalTerritory(location, territoryIds) ||
        (interiorMapId != 0 && MatchesMapId(location, interiorMapId)) ||
        MatchesPlaceNameRow(location, territoryIds) ||
        MatchesMapPlaceNameLinkedToTerritories(location, territoryIds);

    private static uint TryGetMapPlaceNameRowId(uint mapId)
    {
        var mapRow = Svc.Data.GetExcelSheet<Map>()?.GetRowOrDefault(mapId);
        return mapRow?.PlaceName.RowId ?? 0;
    }

    private static HashSet<uint> ResolvePlaceNameRowIdsForTerritories(uint[] territoryIds)
    {
        var cacheKey = string.Join(',', territoryIds);
        if (PlaceNameRowCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var ids = new HashSet<uint>();
        var mapSheet = Svc.Data.GetExcelSheet<Map>();
        if (mapSheet != null)
        {
            foreach (var row in mapSheet)
            {
                if (!ContainsTerritory(row.TerritoryType.RowId, territoryIds))
                {
                    continue;
                }

                var placeRowId = row.PlaceName.RowId;
                if (placeRowId != 0)
                {
                    ids.Add(placeRowId);
                }
            }
        }

        PlaceNameRowCache[cacheKey] = ids;
        return ids;
    }

    private static bool ContainsTerritory(uint territoryId, uint[] territoryIds)
    {
        foreach (var id in territoryIds)
        {
            if (territoryId == id)
            {
                return true;
            }
        }

        return false;
    }
}
