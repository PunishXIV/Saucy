using ECommons.Automation.UIInput;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
namespace Saucy.Framework;

public static unsafe class AddonButton
{
    public static bool TryClick(AtkUnitBase* addon, uint nodeId)
    {
        if (addon == null)
        {
            return false;
        }

        return TryClick(addon, addon->GetComponentButtonById(nodeId), requireEnabled: true);
    }

    public static bool TryClick(AtkUnitBase* addon, AtkComponentButton* button, bool requireEnabled = true)
    {
        if (addon == null || button == null || button->AtkResNode == null || !button->AtkResNode->IsVisible())
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
            Svc.Log.Verbose(ex, "[AddonButton] click failed");
            return false;
        }
    }
}
