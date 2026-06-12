using ECommons.EzIpcManager;
using System;
namespace Saucy.IPC;

[IPC(IPCNames.Lifestream)]
internal static class Lifestream
{
    [EzIPC]
    public static Func<uint, byte, bool> Teleport = null!;

    [EzIPC]
    public static Func<bool> IsBusy = null!;

    [EzIPC]
    public static Action Abort = null!;

    [EzIPC]
    public static Action<string> ExecuteCommand = null!;

    public static bool IsInstalled => SubscriptionManager.IsInitialized(IPCNames.Lifestream);

    public static bool IsBusyNow() => IsInstalled && IsBusy();

    public static void TryAbort()
    {
        if (!IsInstalled)
        {
            return;
        }

        try
        {
            Abort();
        }
        catch
        {
            // Lifestream not loaded or IPC unavailable.
        }
    }

    public static bool TryTeleport(uint aetheryteId, byte subIndex = 0) =>
        IsInstalled &&
        aetheryteId != 0 &&
        Teleport.TryInvoke(aetheryteId, subIndex, out var started) &&
        started;

    public static bool TryAethernetViaLiCommand(string? destinationName)
    {
        if (!IsInstalled || string.IsNullOrWhiteSpace(destinationName) || IsBusyNow())
        {
            return false;
        }

        ExecuteCommand(destinationName.Trim());
        return true;
    }
}
