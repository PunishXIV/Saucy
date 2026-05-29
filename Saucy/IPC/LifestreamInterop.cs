using ECommons;
using ECommons.EzIpcManager;
using System;
using System.Linq;

namespace Saucy.IPC;

/// <summary>
/// IPC client for NightmareXIV/Lifestream (<c>Lifestream/IPC/IPCProvider.cs</c>).
/// </summary>
internal static class LifestreamInterop
{
    private const string PluginName = "Lifestream";

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

    public static void Dispose() => Unsubscribe();

    public static bool IsBusy()
    {
        Refresh();
        return IsInstalled && Ipc.IsBusy();
    }

    public static bool TryTeleport(uint aetheryteId, byte subIndex = 0)
    {
        Refresh();

        if (!IsInstalled || aetheryteId == 0)
            return false;

        return Ipc.Teleport.TryInvoke(aetheryteId, subIndex, out var started) && started;
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
        [EzIPC]
        public static Func<uint, byte, bool> Teleport;

        [EzIPC]
        public static Func<bool> IsBusy;
    }
}
