using Saucy.IPC;
namespace Saucy.OtherGames;

internal static class GateShared
{
    public static void StopAll()
    {
        BossMod.TryDisableGateAi();
        WindBlowsGateMovement.ReleaseIfOwned();
    }
}
