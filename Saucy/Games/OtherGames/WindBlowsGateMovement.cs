using ECommons.GameHelpers;
using Saucy.IPC;
using System;
using System.Numerics;
namespace Saucy.OtherGames;

internal static class WindBlowsGateMovement
{
    private const float FloorSnapHalfExtent = 1.5f;
    private const float MaxSnapDrift = 1f;
    private const float PlatformYTolerance = 0.35f;

    private static bool _ownsPath;
    private static Vector3? _snappedDestination;

    public static bool TryMoveTo(Vector3 destination, float closeRange = 0.25f)
    {
        if (!Vnavmesh.IsInstalled)
        {
            return false;
        }

        var pathDestination = ResolveDestination(destination);
        if (pathDestination == null)
        {
            ReleaseIfOwned();
            return false;
        }

        if (!IsOnPlatform(Player.Position))
        {
            ReleaseIfOwned();
            return false;
        }

        if (Vnavmesh.IsWithinHorizontalRange(pathDestination.Value, closeRange))
        {
            ReleaseIfOwned();
            return true;
        }

        if (_ownsPath)
        {
            if (!IsOnPlatform(Player.Position))
            {
                ReleaseIfOwned();
                return false;
            }

            return Vnavmesh.IsMoving() || Vnavmesh.TryMoveTo(pathDestination.Value, false, closeRange);
        }

        if (Vnavmesh.IsMoving())
        {
            return false;
        }

        if (!Vnavmesh.TryMoveTo(pathDestination.Value, false, closeRange))
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
            _snappedDestination = null;
            return;
        }

        _ownsPath = false;
        _snappedDestination = null;
        Vnavmesh.StopPath();
    }

    private static Vector3? ResolveDestination(Vector3 destination)
    {
        if (_snappedDestination is { } cached)
        {
            return cached;
        }

        var snapped = Vnavmesh.TryGetPointOnFloor(destination, halfExtentXz: FloorSnapHalfExtent);
        if (snapped == null)
        {
            return null;
        }

        var drift = snapped.Value - destination;
        if ((drift.X * drift.X) + (drift.Z * drift.Z) > MaxSnapDrift * MaxSnapDrift)
        {
            return null;
        }

        _snappedDestination = snapped;
        return snapped;
    }

    private static bool IsOnPlatform(Vector3 position)
    {
        if (!IsWithinHorizontalRange(position, AnyWayTheWindBlows.Stage.PlatformCenter, AnyWayTheWindBlows.Stage.PlatformRadius))
        {
            return false;
        }

        var snapped = Vnavmesh.TryGetPointOnFloor(position, halfExtentXz: FloorSnapHalfExtent);
        if (snapped == null)
        {
            return false;
        }

        return MathF.Abs(snapped.Value.Y - AnyWayTheWindBlows.Stage.PlatformFloorY) <= PlatformYTolerance;
    }

    private static bool IsWithinHorizontalRange(Vector3 position, Vector3 center, float range)
    {
        var delta = position - center;
        return (delta.X * delta.X) + (delta.Z * delta.Z) <= range * range;
    }
}
