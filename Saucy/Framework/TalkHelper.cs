using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using static ECommons.GenericHelpers;

namespace Saucy.Framework;

public static unsafe class TalkHelper
{
    public static bool IsVisible()
    {
        if (!TryGetAddonByName<AtkUnitBase>("Talk", out var talk))
        {
            return false;
        }

        return talk->IsVisible && IsAddonReady(talk);
    }

    public static bool TryAdvance(string throttleKey = "Saucy.Talk.Advance", int throttleMs = 400)
    {
        if (!TryGetAddonByName<AtkUnitBase>("Talk", out var talk) || !IsAddonReady(talk) || !talk->IsVisible)
        {
            return false;
        }

        if (!EzThrottler.Throttle(throttleKey, throttleMs))
        {
            return false;
        }

        try
        {
            new AddonMaster.Talk(talk).Click();
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TalkHelper] Talk click failed; trying callback");
        }

        try
        {
            talk->FireCallbackInt(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TalkHelper] Talk callback failed");
            return false;
        }
    }
}
