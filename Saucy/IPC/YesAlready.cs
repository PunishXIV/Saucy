using ECommons.EzSharedDataManager;
using System.Collections.Generic;
namespace Saucy.IPC;

internal static class YesAlready
{
    private const string StopRequestsKey = "YesAlready.StopRequests";

    private static bool pausedBySaucy;

    public static void SyncForGameActivity(bool gamePlaying)
    {
        if (gamePlaying)
        {
            if (!pausedBySaucy)
            {
                Lock();
                pausedBySaucy = true;
            }

            return;
        }

        ResumeIfPausedBySaucy();
    }

    public static void ResumeIfPausedBySaucy()
    {
        if (!pausedBySaucy)
        {
            return;
        }

        pausedBySaucy = false;
        Unlock();
    }

    private static void Lock()
    {
        if (EzSharedData.TryGet<HashSet<string>>(StopRequestsKey, out var data))
        {
            data.Add(Svc.PluginInterface.Manifest.InternalName);
        }
    }

    private static void Unlock()
    {
        if (EzSharedData.TryGet<HashSet<string>>(StopRequestsKey, out var data))
        {
            data.Remove(Svc.PluginInterface.Manifest.InternalName);
        }
    }
}
