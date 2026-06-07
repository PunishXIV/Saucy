using ECommons.Automation.UIInput;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
namespace Saucy.TripleTriad.UI;

internal static unsafe class GoldSaucerCardListUi
{
    internal static bool TryClickGridButton(nint addonPtr, int pageIndex, int cellIndex)
    {
        if (addonPtr == nint.Zero || cellIndex < 0 || cellIndex >= 30)
        {
            return false;
        }

        var addon = (AddonGSInfoCardList*)addonPtr;
        var atkUnit = &addon->AtkUnitBase;

        if (pageIndex >= 0 && pageIndex != addon->SelectedPage)
        {
            addon->RequestedPage = pageIndex;
            addon->TabController.SetTabIndexAndCallBack(pageIndex);
            atkUnit->Update(0);
        }

        return TryClickCell(addonPtr, cellIndex);
    }

    internal static bool TryClickCell(nint addonPtr, int cellIndex)
    {
        if (addonPtr == nint.Zero || cellIndex < 0 || cellIndex >= 30)
        {
            return false;
        }

        var addon = (AddonGSInfoCardList*)addonPtr;
        var cardButton = addon->CardButtons[cellIndex];
        return TryClickAddonButton(&addon->AtkUnitBase, cardButton, false);
    }

    private static bool TryClickAddonButton(AtkUnitBase* addon, AtkComponentButton* button, bool requireEnabled = true)
    {
        if (button == null || button->AtkResNode == null || !button->AtkResNode->IsVisible())
        {
            return false;
        }

        if (requireEnabled && !button->IsEnabled)
        {
            return false;
        }

        try
        {
            button->ClickAddonButton(addon);
            addon->Update(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[GoldSaucerCardListUi] Addon button click failed");
            return false;
        }
    }
}
