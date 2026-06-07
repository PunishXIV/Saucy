using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Saucy.IPC;
using System;
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
    private const float NpcNearHubTeleportRange = 40f;
    public const float AethernetHubUseRange = Vnavmesh.AetheryteCloseRange;
    private const float AethernetHubPlanningRange = 20f;

    private const byte MapMarkerDataTypeAetheryte = 3;
    private const byte MapMarkerDataTypeAethernet = 4;

    // Lumina Aetheryte sheet: IsAetheryte + AethernetGroup distinguish hubs, shards, and standalone crystals.

    private static AetheryteSheet? TryGetAetheryteRow(uint rowId) =>
        Svc.Data.GetExcelSheet<AetheryteSheet>()?.GetRowOrDefault(rowId);

    private static bool IsAethernetHubRow(AetheryteSheet row) =>
        row.IsAetheryte && row.AethernetGroup != 0;

    private static bool IsAethernetShardRow(AetheryteSheet row) =>
        !row.IsAetheryte && row.AethernetGroup != 0;

    private static bool IsAethernetHub(uint rowId)
    {
        var row = TryGetAetheryteRow(rowId);
        return row != null && IsAethernetHubRow(row.Value);
    }

    private static bool IsAethernetShard(uint rowId)
    {
        var row = TryGetAetheryteRow(rowId);
        return row != null && IsAethernetShardRow(row.Value);
    }

    private static bool TerritoryHasAethernetNetwork(uint territoryId)
    {
        var sheet = Svc.Data.GetExcelSheet<AetheryteSheet>();
        if (sheet == null)
        {
            return false;
        }

        foreach (var row in sheet)
        {
            if (row.Territory.RowId != territoryId)
            {
                continue;
            }

            if (IsAethernetHubRow(row) || IsAethernetShardRow(row))
            {
                return true;
            }
        }

        return false;
    }

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

    public static Vector3? GetAethernetShardWorldPosition(uint aethernetShardRowId) =>
        aethernetShardRowId != 0 ? GetWorldPosition(aethernetShardRowId) : null;

    public static Vector3? GetAetheryteWorldPosition(uint aetheryteId) =>
        aetheryteId != 0 ? ResolveAetherytePosition(aetheryteId) : null;

    public static string? GetAethernetShardName(uint aethernetShardRowId)
    {
        if (aethernetShardRowId == 0)
        {
            return null;
        }

        var row = Svc.Data.GetExcelSheet<AetheryteSheet>()?.GetRowOrDefault(aethernetShardRowId);
        return row?.AethernetName.ValueNullable?.Name.ToString();
    }

    public static string FormatTeleportDestination(uint aetheryteId)
    {
        if (aetheryteId == 0)
        {
            return "destination";
        }

        var row = Svc.Data.GetExcelSheet<AetheryteSheet>()?.GetRowOrDefault(aetheryteId);
        if (row == null)
        {
            return "destination";
        }

        var territoryRow = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(row.Value.Territory.RowId);
        var territoryName = territoryRow?.PlaceName.ValueNullable?.Name.ToString()
                            ?? territoryRow?.Name.ToString()
                            ?? string.Empty;

        var aetheryteName = row.Value.IsAetheryte
            ? row.Value.PlaceName.ValueNullable?.Name.ToString()
            : row.Value.AethernetName.ValueNullable?.Name.ToString();

        if (string.IsNullOrWhiteSpace(aetheryteName))
        {
            return string.IsNullOrWhiteSpace(territoryName) ? "destination" : territoryName;
        }

        return string.IsNullOrWhiteSpace(territoryName)
            ? aetheryteName
            : $"{territoryName} {aetheryteName}";
    }

    public static Vector3? TryGetLiveAetherytePosition(uint aetheryteRowId)
    {
        var row = Svc.Data.GetExcelSheet<AetheryteSheet>()?.GetRowOrDefault(aetheryteRowId);
        if (row == null || !row.Value.IsAetheryte)
        {
            return null;
        }

        if (Svc.ClientState.TerritoryType != row.Value.Territory.RowId)
        {
            return null;
        }

        var anchor = TryGetPositionFromLevelSheet(row.Value);
        IGameObject? bestObj = null;
        var bestScore = float.MaxValue;

        foreach (var obj in Svc.Objects)
        {
            if (obj.ObjectKind != ObjectKind.Aetheryte)
            {
                continue;
            }

            var score = anchor != null
                ? Vector3.DistanceSquared(obj.Position, anchor.Value)
                : Vector3.DistanceSquared(obj.Position, Player.Position);

            if (score < bestScore)
            {
                bestScore = score;
                bestObj = obj;
            }
        }

        return bestObj?.Position;
    }

    public static float GetDistanceToAetheryte(uint aetheryteId) => GetHorizontalDistanceToAetheryte(aetheryteId);

    public static float GetHorizontalDistanceToAetheryte(uint aetheryteId)
    {
        var pos = TryGetLiveAetherytePosition(aetheryteId) ?? ResolveAetherytePosition(aetheryteId);
        if (pos == null)
        {
            return float.MaxValue;
        }

        return Vector2.Distance(PlayerPosXZ(), new(pos.Value.X, pos.Value.Z));
    }

    public static uint GetAetheryteTerritoryId(uint aetheryteId)
    {
        if (aetheryteId == 0)
        {
            return 0;
        }

        var row = Svc.Data.GetExcelSheet<AetheryteSheet>()?.GetRowOrDefault(aetheryteId);
        return row?.Territory.RowId ?? 0;
    }

    public static bool IsPlayerInAetheryteTerritory(uint aetheryteId)
    {
        var territoryId = GetAetheryteTerritoryId(aetheryteId);
        return territoryId != 0 && Svc.ClientState.TerritoryType == territoryId;
    }

    public static uint GetAethernetHubAetheryteId(uint aethernetShardRowId)
    {
        if (aethernetShardRowId == 0)
        {
            return 0;
        }

        var shardRow = TryGetAetheryteRow(aethernetShardRowId);
        if (shardRow == null || !IsAethernetShardRow(shardRow.Value))
        {
            return 0;
        }

        return GetAethernetHubAetheryteId(shardRow.Value.AethernetGroup);
    }

    public static bool IsPlayerNearAetheryte(uint aetheryteId) =>
        IsPlayerWithinAetheryteRange(aetheryteId, AethernetHubPlanningRange);

    public static bool IsPlayerInAethernetRange(uint aetheryteId)
    {
        if (aetheryteId == 0)
        {
            return false;
        }

        return GetHorizontalDistanceToAetheryte(aetheryteId) <= AethernetHubUseRange;
    }

    private static bool IsPlayerWithinAetheryteRange(uint aetheryteId, float range)
    {
        if (aetheryteId == 0)
        {
            return false;
        }

        return GetHorizontalDistanceToAetheryte(aetheryteId) <= range;
    }

    public static TravelPlan FindBestTravelPlan(uint territoryId, Vector3 destinationWorld, bool inTargetTerritory)
    {
        var mainAetheryteId = FindClosestUnlockedAetheryte(territoryId, destinationWorld);
        var hasAethernetNetwork = TerritoryHasAethernetNetwork(territoryId);
        var closestShard = hasAethernetNetwork
            ? FindClosestUnlockedAethernetShard(territoryId, destinationWorld)
            : null;
        var hubAetheryteId = closestShard != null ? GetAethernetHubAetheryteId(closestShard.Value.RowId) : 0;

        var mainPos = mainAetheryteId != 0 ? GetWorldPosition(mainAetheryteId) : null;
        var hubPos = hubAetheryteId != 0 ? GetWorldPosition(hubAetheryteId) : null;
        var shardPos = closestShard?.Position;
        var walkFromPos = inTargetTerritory
            ? PlayerPosXZ()
            : hubPos != null
                ? new Vector2(hubPos.Value.X, hubPos.Value.Z)
                : mainPos != null
                    ? new Vector2(mainPos.Value.X, mainPos.Value.Z)
                    : (Vector2?)null;

        (var useAethernet, var skipReason) = EvaluateAethernet(
            walkFromPos,
            shardPos,
            destinationWorld,
            inTargetTerritory,
            hubAetheryteId,
            closestShard == null,
            territoryId,
            hasAethernetNetwork);

        var teleportAetheryteId = inTargetTerritory
            ? 0
            : ChooseBestTeleportAetheryte(mainAetheryteId, hubAetheryteId, destinationWorld);

        var chainAethernet = ShouldChainAethernetAfterTeleport(
            teleportAetheryteId,
            hubAetheryteId,
            destinationWorld,
            closestShard,
            useAethernet,
            territoryId);
        var inTerritoryAethernet = inTargetTerritory && useAethernet && closestShard != null;
        var chainAethernetOk = chainAethernet && HasResolvableAethernetShard(closestShard);
        var inTerritoryAethernetOk = inTerritoryAethernet && HasResolvableAethernetShard(closestShard);
        var includeAethernet = inTerritoryAethernetOk || chainAethernetOk;
        var shard = closestShard;

        return new()
        {
            TeleportAetheryteId = teleportAetheryteId,
            HubAetheryteId = includeAethernet ? hubAetheryteId : 0,
            AethernetShardId = includeAethernet && shard != null ? shard.Value.RowId : 0,
            AethernetShardName = includeAethernet && shard != null ? ResolveAethernetShardName(shard.Value) : null,
            AethernetSkipReason = includeAethernet ? null : skipReason
        };
    }

    private static bool HasResolvableAethernetShard(AethernetShardInfo? shard) =>
        shard != null && !string.IsNullOrWhiteSpace(ResolveAethernetShardName(shard.Value));

    private static string ResolveAethernetShardName(AethernetShardInfo shard)
    {
        if (!string.IsNullOrWhiteSpace(shard.Name))
        {
            return shard.Name;
        }

        return GetAethernetShardName(shard.RowId) ?? string.Empty;
    }

    public static Vector3? GetAetheryteApproachPosition(uint aetheryteId) => GetWorldPosition(aetheryteId);

    private static bool ShouldChainAethernetAfterTeleport(
        uint teleportAetheryteId,
        uint hubAetheryteId,
        Vector3 destinationWorld,
        AethernetShardInfo? closestShard,
        bool useAethernet,
        uint destinationTerritoryId)
    {
        if (!useAethernet || closestShard == null || teleportAetheryteId == 0)
        {
            return false;
        }

        if (!IsAethernetHub(teleportAetheryteId))
        {
            return false;
        }

        // In-game aethernet is only available from the hub crystal for that network, not standalone zone aetherytes.
        if (hubAetheryteId == 0 || teleportAetheryteId != hubAetheryteId)
        {
            return false;
        }

        var teleportTerritoryId = GetAetheryteTerritoryId(teleportAetheryteId);
        if (teleportTerritoryId != 0 &&
            destinationTerritoryId != 0 &&
            teleportTerritoryId != destinationTerritoryId)
        {
            // Hub teleports (e.g. Idyllshire) land in a different zone than the NPC — always aethernet in.
            return true;
        }

        var landDistance = GetPlanarDistanceToDestination(teleportAetheryteId, destinationWorld);
        if (landDistance <= NpcNearHubTeleportRange)
        {
            return false;
        }

        var dest = new Vector2(destinationWorld.X, destinationWorld.Z);
        var shardDistance = Vector2.Distance(closestShard.Value.Position, dest);

        return shardDistance + MinAethernetWalkSavings < landDistance;
    }

    private static float GetPlanarDistanceToDestination(uint aetheryteId, Vector3 destinationWorld)
    {
        var dest = new Vector2(destinationWorld.X, destinationWorld.Z);
        var best = float.MaxValue;

        var sheetPos = GetWorldPosition(aetheryteId);
        if (sheetPos != null)
        {
            best = Vector2.Distance(new(sheetPos.Value.X, sheetPos.Value.Z), dest);
        }

        var livePos = TryGetLiveAetherytePosition(aetheryteId);
        if (livePos != null)
        {
            best = Math.Min(best, Vector2.Distance(new(livePos.Value.X, livePos.Value.Z), dest));
        }

        return best;
    }

    private static uint ChooseBestTeleportAetheryte(uint mainAetheryteId, uint hubAetheryteId, Vector3 destinationWorld)
    {
        if (mainAetheryteId == 0)
        {
            return hubAetheryteId;
        }

        if (hubAetheryteId == 0 || !IsUnlocked(hubAetheryteId) || !IsAethernetHub(hubAetheryteId))
        {
            return mainAetheryteId;
        }

        if (!IsUnlocked(mainAetheryteId))
        {
            return hubAetheryteId;
        }

        var mainDistance = GetPlanarDistanceToDestination(mainAetheryteId, destinationWorld);
        var hubDistance = GetPlanarDistanceToDestination(hubAetheryteId, destinationWorld);
        return hubDistance <= mainDistance ? hubAetheryteId : mainAetheryteId;
    }

    private static (bool Use, string? SkipReason) EvaluateAethernet(
        Vector2? walkFromPos,
        Vector2? shardPos,
        Vector3 destinationWorld,
        bool inTargetTerritory,
        uint hubAetheryteId,
        bool noShard,
        uint destinationTerritoryId,
        bool hasAethernetNetwork)
    {
        if (!hasAethernetNetwork)
        {
            return (false, "territory has no aethernet network in the Aetheryte sheet");
        }

        if (noShard)
        {
            return (false, "no unlocked aethernet shard with a known position");
        }

        if (hubAetheryteId != 0)
        {
            var hubTerritoryId = GetAetheryteTerritoryId(hubAetheryteId);
            if (hubTerritoryId != 0 && hubTerritoryId != destinationTerritoryId)
            {
                return (true, null);
            }
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

        var nearHub = inTargetTerritory
                      && hubAetheryteId != 0
                      && IsAethernetHub(hubAetheryteId)
                      && IsPlayerNearAetheryte(hubAetheryteId);
        if (inTargetTerritory && !nearHub)
        {
            if (savings < MinAethernetWalkSavings)
            {
                return (false,
                    $"not at aethernet hub and shard only saves {savings:F0}y (need {MinAethernetWalkSavings}y)");
            }

            return (true, null);
        }

        if (nearHub)
        {
            if (walkDistance <= NpcNearHubTeleportRange)
            {
                return (false, "NPC is near the aetheryte");
            }

            if (walkDistance >= MinWalkBeforeAethernet || savings >= MinAethernetWalkSavings)
            {
                return (true, null);
            }

            return (false,
                $"near aetheryte but walk {walkDistance:F0}y < {MinWalkBeforeAethernet}y and savings {savings:F0}y < {MinAethernetWalkSavings}y");
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

    private static Vector2 PlayerPosXZ()
    {
        var pos = Player.Position;
        return new(pos.X, pos.Z);
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
            if (row.Territory.RowId != territoryId || !IsAethernetShardRow(row))
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

            var distSq = Vector2.DistanceSquared(dest, new(pos.Value.X, pos.Value.Z));
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
        var hubId = GetAethernetHubAetheryteId(aethernetGroup);
        return hubId != 0 && IsAethernetHub(hubId) && IsUnlocked(hubId);
    }

    private static uint GetAethernetHubAetheryteId(byte aethernetGroup)
    {
        if (aethernetGroup == 0)
        {
            return 0;
        }

        var sheet = Svc.Data.GetExcelSheet<AetheryteSheet>();
        if (sheet == null)
        {
            return 0;
        }

        foreach (var row in sheet)
        {
            if (row.AethernetGroup == aethernetGroup && IsAethernetHubRow(row))
            {
                return row.RowId;
            }
        }

        return 0;
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
                    new(destinationWorld.X, destinationWorld.Z),
                    new(pos.Value.X, pos.Value.Z));

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
                new(destinationWorld.X, destinationWorld.Z),
                new(pos.Value.X, pos.Value.Z));
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
        if (linkedId == 0 || !IsUnlocked(linkedId))
        {
            return 0;
        }

        return GetAetheryteTerritoryId(linkedId) == territoryId ? linkedId : 0;
    }

    public static bool IsUnlockedForTravel(uint aetheryteId) =>
        aetheryteId != 0 && IsUnlocked(aetheryteId);

    public static uint FindDefaultUnlockedAetheryteForTerritory(uint territoryId)
    {
        if (territoryId == 0)
        {
            return 0;
        }

        var linked = FindTerritoryLinkedAetheryte(territoryId);
        if (linked != 0)
        {
            return linked;
        }

        return FindClosestMainAetheryteFromSheet(territoryId, Vector3.Zero);
    }

    private static unsafe bool IsUnlocked(uint aetheryteId) =>
        UIState.Instance()->IsAetheryteUnlocked(aetheryteId);

    private static Vector3? ResolveAetherytePosition(uint aetheryteId)
    {
        var aetheryteRow = Svc.Data.GetExcelSheet<AetheryteSheet>()?.GetRowOrDefault(aetheryteId);
        if (aetheryteRow == null)
        {
            return null;
        }

        var live = TryGetLiveAetherytePosition(aetheryteId);
        if (live != null)
        {
            return live;
        }

        var fromLevel = TryGetPositionFromLevelSheet(aetheryteRow.Value);
        if (fromLevel != null)
        {
            return fromLevel;
        }

        return TryGetPositionFromMapMarker(aetheryteRow.Value);
    }

    private static Vector3? GetWorldPosition(uint aetheryteId) => ResolveAetherytePosition(aetheryteId);

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

    internal struct TravelPlan
    {
        public uint TeleportAetheryteId;
        public uint HubAetheryteId;
        public uint AethernetShardId;
        public string? AethernetShardName;
        public string? AethernetSkipReason;

        public readonly bool HasTeleport => TeleportAetheryteId != 0;
        public readonly bool HasAethernet => AethernetShardId != 0;
    }

    private readonly struct AethernetShardInfo(uint rowId, string name, Vector3 position)
    {
        public uint RowId { get; } = rowId;
        public string Name { get; } = name;
        public Vector2 Position { get; } = new(position.X, position.Z);
    }
}
