using ECommons.EzIpcManager;
using System;
using System.Numerics;
namespace Saucy.IPC;

/// <summary>
///     IPC client for awgil/vnavmesh (<c>vnavmesh/IPCProvider.cs</c>).
/// </summary>
[IPC(IPCNames.Vnavmesh)]
internal static class Vnavmesh
{
    [EzIPC("Nav.IsReady")]
    public static Func<bool> NavIsReady = null!;

    [EzIPC("SimpleMove.PathfindAndMoveTo")]
    public static Func<Vector3, bool, bool> SimpleMovePathfindAndMoveTo = null!;

    [EzIPC("Query.Mesh.PointOnFloor")]
    public static Func<Vector3, bool, float, Vector3?> QueryMeshPointOnFloor = null!;

    public static bool IsInstalled => SubscriptionManager.IsInitialized(IPCNames.Vnavmesh);

    public static bool IsNavReady() =>
        IsInstalled && NavIsReady();

    public static bool TryPathfindAndMoveTo(Vector3 destination, bool fly = false) =>
        IsInstalled &&
        SimpleMovePathfindAndMoveTo.TryInvoke(destination, fly, out var started) &&
        started;

    public static Vector3? TryGetPointOnFloor(Vector3 position, bool allowUnlandable = false, float halfExtentXz = 3f) =>
        IsInstalled && QueryMeshPointOnFloor.TryInvoke(position, allowUnlandable, halfExtentXz, out var point)
            ? point
            : null;
}
