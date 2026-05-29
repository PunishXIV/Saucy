using ECommons.EzIpcManager;
using System;

namespace Saucy.IPC;

/// <summary>
/// IPC client for NightmareXIV/Lifestream (<c>Lifestream/IPC/IPCProvider.cs</c>).
/// </summary>
[IPC(IPCNames.Lifestream)]
internal static class Lifestream
{
    [EzIPC]
    public static Func<uint, byte, bool> Teleport;

    [EzIPC]
    public static Func<bool> IsBusy;

    public static bool IsInstalled => SubscriptionManager.IsInitialized(IPCNames.Lifestream);

    public static bool IsBusyNow() => IsInstalled && IsBusy();

    public static bool TryTeleport(uint aetheryteId, byte subIndex = 0) =>
        IsInstalled &&
        aetheryteId != 0 &&
        Teleport.TryInvoke(aetheryteId, subIndex, out var started) &&
        started;
}
