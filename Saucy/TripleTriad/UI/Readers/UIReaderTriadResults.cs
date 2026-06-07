using System;
namespace Saucy.TripleTriad.UI;

public class UIReaderTriadResults : IUIReader
{
    private const int ResultNotifyFallbackFrames = 30;

    private UIStateTriadResults cachedState = new();
    private int framesSinceShown;

    private bool needsNotify;
    public Action<UIStateTriadResults>? OnUpdated;

    public string GetAddonName() => "TripleTriadResult";

    public void OnAddonLost()
    {
        needsNotify = false;
        framesSinceShown = 0;
        TriadRematchAutomation.ResetResultMatchRecording();
    }

    public void OnAddonShown(nint addonPtr)
    {
        needsNotify = true;
        framesSinceShown = 0;
        cachedState = new();
    }

    public unsafe void OnAddonUpdate(nint addonPtr)
    {
        var addon = (AddonTripleTriadResult*)addonPtr;
        if (addon == null || !needsNotify)
        {
            return;
        }

        framesSinceShown++;
        RefreshCachedState(addon);

        var ready = IsResultReadyToNotify(addon);
        if (!ready && framesSinceShown >= ResultNotifyFallbackFrames)
        {
            ready = true;
        }

        if (ready)
        {
            PublishResult();
        }
    }

    public unsafe void ForceNotifyFromFallback(nint addonPtr = default)
    {
        AddonTripleTriadResult* addon;
        if (addonPtr != nint.Zero)
        {
            addon = (AddonTripleTriadResult*)addonPtr;
        }
        else if (!TriadLocalClientStructs.TryGetResult(out addon))
        {
            return;
        }

        RefreshCachedState(addon);
        PublishResult();
    }

    private unsafe void RefreshCachedState(AddonTripleTriadResult* addon)
    {
        cachedState.cardItemId = TriadResultRewardReader.TryReadRewardItemId(addon);
        TriadResultReader.Read(addon, cachedState);
    }

    private void PublishResult()
    {
        if (cachedState.numMGP < 0)
        {
            cachedState.numMGP = 0;
        }

        needsNotify = false;
        framesSinceShown = 0;
        OnUpdated?.Invoke(cachedState);
    }

    private unsafe bool IsResultReadyToNotify(AddonTripleTriadResult* addon)
    {
        if (!cachedState.isDraw && !cachedState.isLose && !cachedState.isWin)
        {
            return false;
        }

        return TriadRematchAutomation.IsResultReady(&addon->AtkUnitBase);
    }
}

public class UIStateTriadResults
{
    public uint cardItemId;
    public bool isDraw;
    public bool isLose;
    public bool isWin;
    public int numMGP;
}
