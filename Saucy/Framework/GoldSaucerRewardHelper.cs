using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using static ECommons.GenericHelpers;

namespace Saucy.Framework;

internal static unsafe class GoldSaucerRewardHelper
{
    private static readonly uint[] CloseButtonNodeIds = [0, 1, 2, 3, 4, 5, 41];

    public static bool IsVisible() => TryGetAddonByName<AtkUnitBase>("GoldSaucerReward", out var addon) && addon->IsVisible;

    public static bool TryDismiss()
    {
        if (!TryGetAddonByName<AtkUnitBase>("GoldSaucerReward", out var addon) || !addon->IsVisible)
        {
            return false;
        }

        if (TryHideViaUiModule())
        {
            return true;
        }

        if (TryClickCloseButtons(addon))
        {
            return true;
        }

        foreach (var callbackId in new[]
        {
            0, 1
        })
        {
            if (TryFireCallback(addon, callbackId))
            {
                return true;
            }
        }

        try
        {
            Callback.Fire(addon, true, 0);
            addon->Update(0);
            if (!addon->IsVisible)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[GoldSaucerReward] Callback.Fire(0) failed");
        }

        try
        {
            addon->Close(true);
            addon->Update(0);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[GoldSaucerReward] Close(true) failed");
        }

        return !addon->IsVisible;
    }

    private static bool TryHideViaUiModule()
    {
        try
        {
            var uiModule = (UIModule*)Svc.GameGui.GetUIModule().Address;
            if (uiModule == null)
            {
                return false;
            }

            uiModule->HideGoldSaucerReward();
            return !IsVisible();
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[GoldSaucerReward] HideGoldSaucerReward failed");
            return false;
        }
    }

    private static bool TryClickCloseButtons(AtkUnitBase* addon)
    {
        foreach (var nodeId in CloseButtonNodeIds)
        {
            var button = addon->GetComponentButtonById(nodeId);
            if (AddonButton.TryClick(addon, button, requireEnabled: false) && !addon->IsVisible)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFireCallback(AtkUnitBase* addon, int callbackId)
    {
        try
        {
            addon->FireCallbackInt(callbackId);
            addon->Update(0);
            return !addon->IsVisible;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[GoldSaucerReward] FireCallbackInt({callbackId}) failed");
            return false;
        }
    }
}
