using ECommons;
using ECommons.EzIpcManager;
using System;
using System.Linq;
using System.Numerics;

namespace Saucy.IPC;

/// <summary>
/// IPC client for awgil/vnavmesh (<c>vnavmesh/IPCProvider.cs</c>).
/// EzIPC pattern follows <see href="https://github.com/Knightmore/Henchman/tree/master/Henchman/IPC"/>.
/// </summary>
internal static class VnavmeshInterop
{
    private const string PluginName = "vnavmesh";

    private static EzIPCDisposalToken[]? _disposals;

    public static bool IsInstalled => IsLoaded && _disposals != null;

    private static bool IsLoaded =>
        Svc.PluginInterface.InstalledPlugins.Any(x => x.InternalName == PluginName && x.IsLoaded);

    public static void Refresh()
    {
        if (!IsLoaded)
        {
            Unsubscribe();
            return;
        }

        if (_disposals != null)
            return;

        _disposals = EzIPC.Init(typeof(Ipc), PluginName);
    }

    public static void Dispose()
    {
        Unsubscribe();
    }

    public static bool IsNavReady()
    {
        Refresh();
        return IsInstalled && Ipc.NavIsReady();
    }

    public static bool TryPathfindAndMoveTo(Vector3 destination, bool fly = false)
    {
        Refresh();

        if (!IsInstalled)
            return false;

        return Ipc.SimpleMovePathfindAndMoveTo.TryInvoke(destination, fly, out var started) && started;
    }

    public static Vector3? TryGetPointOnFloor(Vector3 position, bool allowUnlandable = false, float halfExtentXz = 3f)
    {
        Refresh();

        if (!IsInstalled)
            return null;

        return Ipc.QueryMeshPointOnFloor.TryInvoke(position, allowUnlandable, halfExtentXz, out var point)
            ? point
            : null;
    }

    private static void Unsubscribe()
    {
        if (_disposals == null)
            return;

        foreach (var token in _disposals)
            token.Dispose();

        _disposals = null;
    }

    private static class Ipc
    {
        [EzIPC("Nav.IsReady")]
        public static Func<bool> NavIsReady;

        [EzIPC("SimpleMove.PathfindAndMoveTo")]
        public static Func<Vector3, bool, bool> SimpleMovePathfindAndMoveTo;

        [EzIPC("Query.Mesh.PointOnFloor")]
        public static Func<Vector3, bool, float, Vector3?> QueryMeshPointOnFloor;
    }
}
