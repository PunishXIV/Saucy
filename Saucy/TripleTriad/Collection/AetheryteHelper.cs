using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System.Linq;
using System.Numerics;

namespace Saucy.TripleTriad;

internal static class AetheryteHelper
{
    /// <summary>Finds the closest unlocked aetheryte in the territory to a world destination.</summary>
    public static uint FindClosestUnlockedAetheryte(uint territoryId, Vector3 destinationWorld)
    {
        if (territoryId == 399)
            return IsUnlocked(75) ? 75u : 0;

        var sheet = Svc.Data.GetExcelSheet<Aetheryte>();
        if (sheet == null)
            return 0;

        uint bestId = 0;
        var bestDistSq = float.MaxValue;

        foreach (var row in sheet)
        {
            if (!row.IsAetheryte || row.Territory.RowId != territoryId)
                continue;

            if (!IsUnlocked(row.RowId))
                continue;

            var pos = GetPosition(row);
            if (pos == null)
                continue;

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

    private static unsafe bool IsUnlocked(uint aetheryteId) =>
        UIState.Instance()->IsAetheryteUnlocked(aetheryteId);

    private static Vector3? GetPosition(Aetheryte aetheryte)
    {
        foreach (var level in aetheryte.Level)
        {
            if (level.ValueNullable is { } lv)
                return new Vector3(lv.X, lv.Y, lv.Z);
        }

        return null;
    }
}
