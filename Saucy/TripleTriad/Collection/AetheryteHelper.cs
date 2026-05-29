using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System.Numerics;
namespace Saucy.TripleTriad;

internal static class AetheryteHelper
{
    private const uint GoldSaucerTerritoryId = 144;
    private const uint GoldSaucerAetheryteId = 62;

    /// <summary>Finds the closest unlocked aetheryte in the territory to a world destination.</summary>
    public static uint FindClosestUnlockedAetheryte(uint territoryId, Vector3 destinationWorld)
    {
        var bestId = FindClosestFromTeleportList(territoryId, destinationWorld);
        if (bestId != 0)
        {
            return bestId;
        }

        bestId = FindClosestFromSheet(territoryId, destinationWorld);
        if (bestId != 0)
        {
            return bestId;
        }

        return FindTerritoryLinkedAetheryte(territoryId);
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

    private static uint FindClosestFromSheet(uint territoryId, Vector3 destinationWorld)
    {
        var sheet = Svc.Data.GetExcelSheet<Aetheryte>();
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

    private static Vector3? GetWorldPosition(uint aetheryteId)
    {
        var aetheryteRow = Svc.Data.GetExcelSheet<Aetheryte>()?.GetRowOrDefault(aetheryteId);
        if (aetheryteRow == null)
        {
            return null;
        }

        foreach (var level in aetheryteRow.Value.Level)
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

        foreach (var levelRef in aetheryteRow.Value.Level)
        {
            var levelRow = levelSheet.GetRowOrDefault(levelRef.RowId);
            if (levelRow != null)
            {
                return new Vector3(levelRow.Value.X, levelRow.Value.Y, levelRow.Value.Z);
            }
        }

        return null;
    }
}
