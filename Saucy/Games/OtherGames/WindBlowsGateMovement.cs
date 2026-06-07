using Saucy.IPC;
using System.Numerics;
namespace Saucy.OtherGames;

internal static class WindBlowsGateMovement
{
    private static bool _ownsPath;

    public static bool TryMoveTo(Vector3 destination, float closeRange = 0.25f)
    {
        if (!Vnavmesh.IsInstalled)
        {
            return false;
        }

        if (Vnavmesh.IsWithinHorizontalRange(destination, closeRange))
        {
            ReleaseIfOwned();
            return true;
        }

        if (_ownsPath)
        {
            return Vnavmesh.IsMoving() || Vnavmesh.TryMoveTo(destination, false, closeRange);
        }

        if (Vnavmesh.IsMoving())
        {
            return false;
        }

        if (!Vnavmesh.TryMoveTo(destination, false, closeRange))
        {
            return false;
        }

        _ownsPath = true;
        return true;
    }

    public static void ReleaseIfOwned()
    {
        if (!_ownsPath || !Vnavmesh.IsInstalled)
        {
            _ownsPath = false;
            return;
        }

        _ownsPath = false;
        Vnavmesh.StopPath();
    }
}
