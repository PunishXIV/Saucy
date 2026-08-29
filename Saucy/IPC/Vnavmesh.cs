using ECommons.EzIpcManager;
using ECommons.GameHelpers;
using System;
using System.Numerics;
namespace Saucy.IPC;

[IPC(IPCNames.Vnavmesh)]
internal static class Vnavmesh
{
    public const float NpcCloseRange = 3f;

    public const float AetheryteCloseRange = 8.5f;

    /// <summary>
    /// vnav often completes a few tenths outside the requested stop radius. Without this slack,
    /// callers re-issue PathfindAndMove every tick (Bocchi treasure-hunt ~2y spam).
    /// </summary>
    public const float ArrivalSlack = 0.5f;

    [EzIPC("Nav.IsReady")]
    private static Func<bool> NavIsReadyRpc = null!;

    [EzIPC("Nav.BuildProgress")]
    private static Func<float> NavBuildProgressRpc = null!;

    [EzIPC("Nav.Reload")]
    private static Func<bool> NavReloadRpc = null!;

    [EzIPC("Nav.PathfindInProgress")]
    private static Func<bool> NavPathfindInProgressRpc = null!;

    [EzIPC("Query.Mesh.PointOnFloor")]
    private static Func<Vector3, bool, float, Vector3?> QueryMeshPointOnFloorRpc = null!;

    [EzIPC("Path.IsRunning")]
    private static Func<bool> PathIsRunningRpc = null!;

    [EzIPC("Path.Stop")]
    private static Action PathStopRpc = null!;

    [EzIPC("SimpleMove.PathfindAndMoveTo")]
    private static Func<Vector3, bool, bool> SimpleMovePathfindAndMoveToRpc = null!;

    [EzIPC("SimpleMove.PathfindAndMoveCloseTo")]
    private static Func<Vector3, bool, float, bool> SimpleMovePathfindAndMoveCloseToRpc = null!;

    [EzIPC("SimpleMove.PathfindInProgress")]
    private static Func<bool> SimpleMovePathfindInProgressRpc = null!;

    public static bool IsInstalled => SubscriptionManager.IsInitialized(IPCNames.Vnavmesh);

    public static bool IsNavReady() => IsInstalled && NavIsReadyRpc();

    public static float GetBuildProgress() =>
        IsInstalled && NavBuildProgressRpc.TryInvoke(out var progress) ? progress : -1f;

    public static bool IsBuildInProgress()
    {
        var progress = GetBuildProgress();
        return progress is >= 0f and < 1f;
    }

    public static void TryEnsureNavMeshLoading()
    {
        if (!IsInstalled || IsNavReady())
        {
            return;
        }

        // Match Henchman: poll until ready. Only kick a load when vnavmesh is not already building.
        if (IsBuildInProgress())
        {
            return;
        }

        NavReloadRpc?.TryInvoke(out var _);
    }

    public static bool ShouldDeferHeavyWork() =>
        IsInstalled && (!IsNavReady() || IsBuildInProgress() || IsPathfindInProgress());

    /// Navmesh build/load, path calculation, and active movement all contend with deck optimizer CPU.
    public static bool ShouldDeferDeckOptimizerWork() =>
        IsInstalled && (!IsNavReady() || IsBuildInProgress() || IsMoving());

    public static bool IsPathRunning() => IsInstalled && PathIsRunningRpc();

    public static bool IsPathfindInProgress() =>
        IsInstalled && (SimpleMovePathfindInProgressRpc() || NavPathfindInProgressRpc());

    public static bool IsMoving() => IsPathRunning() || IsPathfindInProgress();

    /// <summary>
    /// SimpleMove rejects stacked calls with "Pathfinding task is in progress".
    /// Path.Stop does not clear that pending task — wait for the slot (Ocelot VNavmeshIpc).
    /// </summary>
    public static bool CanStartPathfind() =>
        IsInstalled && IsNavReady() && !IsPathfindInProgress();

    public static void StopPath()
    {
        if (IsInstalled)
        {
            PathStopRpc();
        }
    }

    public static Vector3? TryGetPointOnFloor(Vector3 position, bool allowUnlandable = false, float halfExtentXz = 3f) =>
        IsInstalled && QueryMeshPointOnFloorRpc.TryInvoke(position, allowUnlandable, halfExtentXz, out var point)
            ? point
            : null;

    public static bool TryPathfindAndMoveTo(Vector3 destination, bool fly = false)
    {
        if (!CanStartPathfind())
        {
            return false;
        }

        return SimpleMovePathfindAndMoveToRpc.TryInvoke(destination, fly, out var started) && started;
    }

    public static bool TryPathfindAndMoveCloseTo(Vector3 destination, bool fly, float range)
    {
        if (!CanStartPathfind())
        {
            return false;
        }

        return SimpleMovePathfindAndMoveCloseToRpc.TryInvoke(destination, fly, range, out var started) &&
               started;
    }

    public static bool TryMoveTo(Vector3 destination, bool fly, float closeRange = 0f)
    {
        if (closeRange > 0f && IsWithinHorizontalRange(destination, closeRange + ArrivalSlack))
        {
            return true;
        }

        return closeRange > 0f
            ? TryPathfindAndMoveCloseTo(destination, fly, closeRange)
            : TryPathfindAndMoveTo(destination, fly);
    }

    public static bool TickArrival(Vector3 destination, float closeRange)
    {
        if (!IsWithinHorizontalRange(destination, closeRange + ArrivalSlack))
        {
            return false;
        }

        if (IsPathRunning())
        {
            StopPath();
        }

        return true;
    }

    public static bool IsWithinHorizontalRange(Vector3 destination, float range)
    {
        var delta = Player.Position - destination;
        return (delta.X * delta.X) + (delta.Z * delta.Z) <= range * range;
    }
}
