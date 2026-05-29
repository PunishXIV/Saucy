using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System.Numerics;
using AetheryteSheet = Lumina.Excel.Sheets.Aetheryte;
using LuminaMap = Lumina.Excel.Sheets.Map;
namespace Saucy.TripleTriad;

internal static class AetheryteHelper
{
    private const uint GoldSaucerTerritoryId = 144;
    private const uint GoldSaucerAetheryteId = 62;
    private const float MinAethernetWalkSavings = 15f;
    private const float MinWalkBeforeAethernet = 10f;
    private const float MainAetheryteInteractionRange = 20f;

    private const byte MapMarkerDataTypeAetheryte = 3;
    private const byte MapMarkerDataTypeAethernet = 4;

    internal struct TravelPlan
    {
        public uint TeleportAetheryteId;
        public uint AethernetShardId;
        public string? AethernetShardName;
        public string? AethernetSkipReason;

        public readonly bool HasTeleport => TeleportAetheryteId != 0;
        public readonly bool HasAethernet => AethernetShardId != 0;
    }

    /// <summary>Finds the closest unlocked aetheryte in the territory to a world destination.</summary>
    public static uint FindClosestUnlockedAetheryte(uint territoryId, Vector3 destinationWorld)
    {
        var bestId = FindClosestFromTeleportList(territoryId, destinationWorld);
        if (bestId != 0)
        {
            return bestId;
        }

        bestId = FindClosestMainAetheryteFromSheet(territoryId, destinationWorld);
        if (bestId != 0)
        {
            return bestId;
        }

        return FindTerritoryLinkedAetheryte(territoryId);
    }

    /// <summary>World position for an aethernet shard row, if known.</summary>
    public static Vector3? GetAethernetShardWorldPosition(uint aethernetShardRowId) =>
        aethernetShardRowId != 0 ? GetWorldPosition(aethernetShardRowId) : null;

    /// <summary>Plans zone travel: optional aetheryte teleport, optional aethernet shard, then walking.</summary>
    public static TravelPlan FindBestTravelPlan(uint territoryId, Vector3 destinationWorld, bool inTargetTerritory)
    {
        var mainAetheryteId = FindClosestUnlockedAetheryte(territoryId, destinationWorld);
        var closestShard = FindClosestUnlockedAethernetShard(territoryId, destinationWorld);

        var mainPos = mainAetheryteId != 0 ? GetWorldPosition(mainAetheryteId) : null;
        var shardPos = closestShard?.Position;
        var walkFromPos = inTargetTerritory
            ? PlayerPosXZ()
            : mainPos != null
                ? new Vector2(mainPos.Value.X, mainPos.Value.Z)
                : (Vector2?)null;

        var (useAethernet, skipReason) = EvaluateAethernet(
            walkFromPos,
            shardPos,
            destinationWorld,
            inTargetTerritory,
            territoryId,
            closestShard == null);

        return new()
        {
            TeleportAetheryteId = inTargetTerritory ? 0 : mainAetheryteId,
            AethernetShardId = useAethernet ? closestShard?.RowId ?? 0 : 0,
            AethernetShardName = useAethernet ? closestShard?.Name : null,
            AethernetSkipReason = useAethernet ? null : skipReason
        };
    }

    private static (bool Use, string? SkipReason) EvaluateAethernet(
        Vector2? walkFromPos,
        Vector2? shardPos,
        Vector3 destinationWorld,
        bool inTargetTerritory,
        uint territoryId,
        bool noShard)
    {
        if (noShard)
        {
            return (false, "no unlocked aethernet shard with a known position");
        }

        if (walkFromPos == null || shardPos == null)
        {
            return (false, "missing player or shard coordinates");
        }

        var dest = new Vector2(destinationWorld.X, destinationWorld.Z);
        var walkDistance = Vector2.Distance(walkFromPos.Value, dest);
        var shardDistance = Vector2.Distance(shardPos.Value, dest);
        var savings = walkDistance - shardDistance;

        if (savings <= 0f)
        {
            return (false,
                $"walking is not longer than via shard (walk {walkDistance:F0}y, via shard {shardDistance:F0}y)");
        }

        var nearMain = inTargetTerritory && IsPlayerNearUnlockedMainAetheryte(territoryId);
        if (nearMain)
        {
            if (walkDistance >= MinWalkBeforeAethernet || savings >= MinAethernetWalkSavings)
            {
                return (true, null);
            }

            return (false,
                $"near main aetheryte but walk {walkDistance:F0}y < {MinWalkBeforeAethernet}y and savings {savings:F0}y < {MinAethernetWalkSavings}y");
        }

        if (walkDistance < MinWalkBeforeAethernet)
        {
            return (false, $"walk distance {walkDistance:F0}y < {MinWalkBeforeAethernet}y");
        }

        if (savings < MinAethernetWalkSavings)
        {
            return (false, $"shard only saves {savings:F0}y (need {MinAethernetWalkSavings}y)");
        }

        return (true, null);
    }

    private static bool IsPlayerNearUnlockedMainAetheryte(uint territoryId)
    {
        var player = PlayerPosXZ();
        var sheet = Svc.Data.GetExcelSheet<AetheryteSheet>();
        if (sheet == null)
        {
            return false;
        }

        foreach (var row in sheet)
        {
            if (!row.IsAetheryte || row.Territory.RowId != territoryId || !IsUnlocked(row.RowId))
            {
                continue;
            }

            var pos = GetWorldPosition(row.RowId);
            if (pos == null)
            {
                continue;
            }

            if (Vector2.Distance(player, new Vector2(pos.Value.X, pos.Value.Z)) <= MainAetheryteInteractionRange)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector2 PlayerPosXZ()
    {
        var pos = ECommons.GameHelpers.Player.Position;
        return new Vector2(pos.X, pos.Z);
    }

    private static AethernetShardInfo? FindClosestUnlockedAethernetShard(uint territoryId, Vector3 destinationWorld)
    {
        var sheet = Svc.Data.GetExcelSheet<AetheryteSheet>();
        if (sheet == null)
        {
            return null;
        }

        AethernetShardInfo? best = null;
        var bestDistSq = float.MaxValue;
        var dest = new Vector2(destinationWorld.X, destinationWorld.Z);

        foreach (var row in sheet)
        {
            if (row.IsAetheryte || row.AethernetGroup == 0 || row.Territory.RowId != territoryId)
            {
                continue;
            }

            if (!IsAethernetNetworkUnlocked(row.AethernetGroup))
            {
                continue;
            }

            var pos = GetWorldPosition(row.RowId);
            if (pos == null)
            {
                continue;
            }

            var distSq = Vector2.DistanceSquared(dest, new Vector2(pos.Value.X, pos.Value.Z));
            if (best == null || distSq < bestDistSq)
            {
                bestDistSq = distSq;
                var shardName = row.AethernetName.ValueNullable?.Name.ToString() ?? string.Empty;
                best = new(row.RowId, shardName, pos.Value);
            }
        }

        return best;
    }

    private static bool IsAethernetNetworkUnlocked(byte aethernetGroup)
    {
        var sheet = Svc.Data.GetExcelSheet<AetheryteSheet>();
        if (sheet == null)
        {
            return false;
        }

        foreach (var row in sheet)
        {
            if (row.IsAetheryte && row.AethernetGroup == aethernetGroup)
            {
                return IsUnlocked(row.RowId);
            }
        }

        return false;
    }

    private static uint FindClosestFromTeleportList(uint territoryId, Vector3 destinationWorld)
    {
        uint bestId = 0;
        var bestDistSq = float.MaxValue;

        foreach (var entry in Svc.AetheryteList)
        {
            if (entry.TerritoryId != territoryId)
            {
                continue;
            }

            var row = Svc.Data.GetExcelSheet<AetheryteSheet>()?.GetRowOrDefault(entry.AetheryteId);
            if (row == null || !row.Value.IsAetheryte)
            {
                continue;
            }

            var pos = GetWorldPosition(entry.AetheryteId);
            var distSq = pos == null
                ? 0f
                : Vector2.DistanceSquared(
                    new Vector2(destinationWorld.X, destinationWorld.Z),
                    new Vector2(pos.Value.X, pos.Value.Z));

            if (bestId == 0 || distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestId = entry.AetheryteId;
            }
        }

        return bestId;
    }

    private static uint FindClosestMainAetheryteFromSheet(uint territoryId, Vector3 destinationWorld)
    {
        var sheet = Svc.Data.GetExcelSheet<AetheryteSheet>();
        if (sheet == null)
        {
            return 0;
        }

        uint bestId = 0;
        var bestDistSq = float.MaxValue;

        foreach (var row in sheet)
        {
            if (!row.IsAetheryte || row.Territory.RowId != territoryId)
            {
                continue;
            }

            if (!IsUnlocked(row.RowId))
            {
                continue;
            }

            var pos = GetWorldPosition(row.RowId);
            if (pos == null)
            {
                if (bestId == 0)
                {
                    bestId = row.RowId;
                }

                continue;
            }

            var distSq = Vector2.DistanceSquared(
                new Vector2(destinationWorld.X, destinationWorld.Z),
                new Vector2(pos.Value.X, pos.Value.Z));
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestId = row.RowId;
            }
        }

        return bestId;
    }

    private static uint FindTerritoryLinkedAetheryte(uint territoryId)
    {
        if (territoryId == GoldSaucerTerritoryId && IsUnlocked(GoldSaucerAetheryteId))
        {
            return GoldSaucerAetheryteId;
        }

        var territoryRow = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        if (territoryRow == null)
        {
            return 0;
        }

        var linkedId = territoryRow.Value.Aetheryte.RowId;
        return linkedId != 0 && IsUnlocked(linkedId) ? linkedId : 0;
    }

    private static unsafe bool IsUnlocked(uint aetheryteId) =>
        UIState.Instance()->IsAetheryteUnlocked(aetheryteId);

    /// <summary>World position from map markers (Lifestream-compatible), then Level sheet.</summary>
    private static Vector3? GetWorldPosition(uint aetheryteId)
    {
        var aetheryteRow = Svc.Data.GetExcelSheet<AetheryteSheet>()?.GetRowOrDefault(aetheryteId);
        if (aetheryteRow == null)
        {
            return null;
        }

        var fromMarker = TryGetPositionFromMapMarker(aetheryteRow.Value);
        if (fromMarker != null)
        {
            return fromMarker;
        }

        return TryGetPositionFromLevelSheet(aetheryteRow.Value);
    }

    private static Vector3? TryGetPositionFromMapMarker(AetheryteSheet row)
    {
        var territoryId = row.Territory.RowId;
        LuminaMap? territoryMap = null;
        var mapSheet = Svc.Data.GetExcelSheet<LuminaMap>();
        if (mapSheet != null)
        {
            foreach (var map in mapSheet)
            {
                if (map.TerritoryType.RowId != territoryId)
                {
                    continue;
                }

                territoryMap = map;
                break;
            }
        }

        if (territoryMap == null)
        {
            return null;
        }

        var scale = territoryMap.Value.SizeFactor;
        var dataType = row.IsAetheryte ? MapMarkerDataTypeAetheryte : MapMarkerDataTypeAethernet;
        var dataKey = row.IsAetheryte ? row.RowId : row.AethernetName.RowId;

        var markerSheet = Svc.Data.GetSubrowExcelSheet<MapMarker>();
        if (markerSheet == null)
        {
            return null;
        }

        foreach (var markerRow in markerSheet)
        {
            foreach (var marker in markerRow)
            {
                if (marker.DataType != dataType || marker.DataKey.RowId != dataKey)
                {
                    continue;
                }

                var x = ConvertMapMarkerToRawPosition(marker.X, scale);
                var z = ConvertMapMarkerToRawPosition(marker.Y, scale);
                return new Vector3(x, 0f, z);
            }
        }

        return null;
    }

    private static float ConvertMapMarkerToRawPosition(int pos, uint scale)
    {
        var num = scale / 100f;
        return (pos - 1024f) / num;
    }

    private static Vector3? TryGetPositionFromLevelSheet(AetheryteSheet row)
    {
        foreach (var level in row.Level)
        {
            if (level.ValueNullable is { } lv)
            {
                return new Vector3(lv.X, lv.Y, lv.Z);
            }
        }

        var levelSheet = Svc.Data.GetExcelSheet<Level>();
        if (levelSheet == null)
        {
            return null;
        }

        foreach (var levelRef in row.Level)
        {
            var levelRow = levelSheet.GetRowOrDefault(levelRef.RowId);
            if (levelRow != null)
            {
                return new Vector3(levelRow.Value.X, levelRow.Value.Y, levelRow.Value.Z);
            }
        }

        return null;
    }

    private readonly struct AethernetShardInfo(uint rowId, string name, Vector3 position)
    {
        public uint RowId { get; } = rowId;
        public string Name { get; } = name;
        public Vector2 Position { get; } = new(position.X, position.Z);
    }
}
