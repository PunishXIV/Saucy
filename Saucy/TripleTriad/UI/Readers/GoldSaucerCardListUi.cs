using FFXIVClientStructs.FFXIV.Client.UI;
using Saucy.Framework;
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
        return AddonButton.TryClick(&addon->AtkUnitBase, cardButton, requireEnabled: false);
    }
}
