using System;
namespace Saucy.TripleTriad.UI;

public class UIReaderTriadResults : IUIReader
{
    private const int ResultNotifyFallbackFrames = 90;

    private UIStateTriadResults cachedState = new();
    private int framesSinceShown;

    public Action<UIStateTriadResults>? OnUpdated;

    public bool HasPendingNotify { get; private set; }

    public string GetAddonName() => "TripleTriadResult";

    public void OnAddonLost()
    {
        // If the window closed before the rewards panel finished populating,
        // publish what we have so the match is still counted.
        if (HasPendingNotify && (cachedState.isDraw || cachedState.isLose || cachedState.isWin))
        {
            PublishResult();
        }

        HasPendingNotify = false;
        framesSinceShown = 0;
        TriadRematchAutomation.ResetResultMatchRecording();
    }

    public void OnAddonShown(nint addonPtr)
    {
        HasPendingNotify = true;
        framesSinceShown = 0;
        cachedState = new();
    }

    public unsafe void OnAddonUpdate(nint addonPtr)
    {
        var addon = (AddonTripleTriadResult*)addonPtr;
        if (addon == null || !HasPendingNotify)
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

        HasPendingNotify = false;
        framesSinceShown = 0;
        OnUpdated?.Invoke(cachedState);
    }

    private unsafe bool IsResultReadyToNotify(AddonTripleTriadResult* addon)
    {
        if (!cachedState.isDraw && !cachedState.isLose && !cachedState.isWin)
        {
            return false;
        }

        // The win/lose banner shows before the rewards panel is populated. Wait for
        // the MGP reward to parse so stats aren't recorded with 0 MGP / no card;
        // the frame fallback in OnAddonUpdate still publishes if it never appears.
        if (cachedState.numMGP < 0)
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
