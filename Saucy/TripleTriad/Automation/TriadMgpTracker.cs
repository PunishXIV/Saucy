using FFXIVClientStructs.FFXIV.Client.Game;

namespace Saucy.TripleTriad;

internal static unsafe class TriadMgpTracker
{
    private const uint MgpItemId = 29;

    private static int mgpAtMatchStart = -1;

    public static void SnapshotAtMatchStart()
    {
        var current = ReadInventoryMgp();
        if (current >= 0)
        {
            mgpAtMatchStart = current;
        }
    }

    public static void Reset()
    {
        mgpAtMatchStart = -1;
    }

    // Match fee is deducted before the board opens; a win adds gross prize MGP on top.
    public static bool TryConsumeWinReward(out int mgp)
    {
        mgp = -1;
        if (mgpAtMatchStart < 0)
        {
            return false;
        }

        var current = ReadInventoryMgp();
        var baseline = mgpAtMatchStart;
        mgpAtMatchStart = -1;

        if (current < 0)
        {
            return false;
        }

        var delta = current - baseline;
        if (delta <= 0)
        {
            return false;
        }

        mgp = delta;
        return true;
    }

    private static int ReadInventoryMgp()
    {
        var im = InventoryManager.Instance();
        return im is null ? -1 : im->GetInventoryItemCount(MgpItemId, false, false, false);
    }
}
