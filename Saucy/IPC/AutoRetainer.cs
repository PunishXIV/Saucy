using ECommons.EzIpcManager;
using System;
namespace Saucy.IPC;

[IPC(IPCNames.AutoRetainer)]
internal static class AutoRetainerIpc
{
    [EzIPC("PluginState.IsBusy")]
    private static Func<bool> IsBusyRpc = null!;

    [EzIPC("PluginState.AreAnyRetainersAvailableForCurrentChara")]
    private static Func<bool> AreAnyRetainersAvailableRpc = null!;

    public static bool IsInstalled => SubscriptionManager.IsInitialized(IPCNames.AutoRetainer);

    public static bool IsBusyNow()
    {
        if (!IsInstalled)
        {
            return false;
        }

        try
        {
            return IsBusyRpc();
        }
        catch
        {
            return false;
        }
    }

    public static bool AreAnyRetainersReady()
    {
        if (!IsInstalled)
        {
            return false;
        }

        try
        {
            return AreAnyRetainersAvailableRpc();
        }
        catch
        {
            return false;
        }
    }
}
