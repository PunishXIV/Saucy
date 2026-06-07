using ECommons.Automation;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using static ECommons.GenericHelpers;

namespace Saucy.Framework;

public static unsafe class SelectStringHelper
{
    public const uint ListNodeId = 3;

    public const int YesnoMenuEntryCount = 2;

    public const int YesEntryIndex = 0;

    public const int NoEntryIndex = 1;

    private static readonly uint[] TriadListEntryIconIds = [60091u, 61721u, 61723u];

    public static bool IsNpcListMenuVisible() =>
        TryGetVisibleSelectString(out var _) || TryGetVisibleSelectIconString(out var _);

    public static bool TryGetArcadeMenu(out AddonSelectString* menu) =>
        TryGetAgentMenu(out menu, SelectYesnoHelper.IsArcadeAddon);

    public static bool TryGetTriadMenu(out AddonSelectString* menu) =>
        TryGetAgentMenu(out menu, SelectYesnoHelper.IsTriadAddon);

    public static bool IsArcadeYesnoMenu(AddonSelectString* menu) =>
        IsAgentYesnoMenu(menu, SelectYesnoHelper.IsArcadeAddon);

    public static bool IsTriadYesnoMenu(AddonSelectString* menu) =>
        IsAgentYesnoMenu(menu, SelectYesnoHelper.IsTriadAddon);

    public static bool TrySelectYesEntry(AddonSelectString* menu) => TrySelectEntry(menu, YesEntryIndex);

    public static bool TrySelectNoEntry(AddonSelectString* menu) => TrySelectEntry(menu, NoEntryIndex);

    public static bool TrySelectEntry(AddonSelectString* menu, int index)
    {
        if (menu == null || !IsAddonReady(&menu->AtkUnitBase) || !menu->AtkUnitBase.IsVisible)
        {
            return false;
        }

        try
        {
            Callback.Fire(&menu->AtkUnitBase, true, index);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[SelectString] Callback.Fire({index}) failed");
        }

        try
        {
            var select = new AddonMaster.SelectString(menu);
            var entryIndex = 0;
            foreach (var entry in select.Entries)
            {
                if (entryIndex == index)
                {
                    entry.Select();
                    return true;
                }

                entryIndex++;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[SelectString] Entry {index} select failed");
        }

        return false;
    }

    public static bool TrySelectTriadEntry(string throttleKey = "SaucyTriadSelectMenu")
    {
        if (TryGetTriadMenu(out var triadMenu) && IsTriadYesnoMenu(triadMenu))
        {
            return TrySelectYesEntry(triadMenu);
        }

        if (TryGetVisibleSelectIconString(out var iconMenu) &&
            TrySelectTriadIconStringEntry(iconMenu, throttleKey))
        {
            return true;
        }

        if (TryGetVisibleSelectString(out var menu) &&
            TrySelectTriadListEntry(&menu->AtkUnitBase, throttleKey))
        {
            return true;
        }

        return false;
    }

    public static void CollectTriadMenuDebugLines(List<string> lines)
    {
        lines.Add($"npc menu visible: {IsNpcListMenuVisible()}");

        if (TryGetTriadMenu(out var triadMenu))
        {
            lines.Add($"triad-agent SelectString: yesnoMenu={IsTriadYesnoMenu(triadMenu)}");
        }

        if (TryGetVisibleSelectIconString(out var iconMenu))
        {
            AppendMenuListDebug(&iconMenu->AtkUnitBase, "SelectIconString", lines);
            try
            {
                var popupCount = new AddonMaster.SelectIconString(iconMenu).EntryCount;
                var fallbackIndex0 = TryFindTriadIconStringEntryIndex(iconMenu, out var resolvedIndex);
                lines.Add($"SelectIconString popup entries={popupCount}, resolvedIndex={resolvedIndex}, fallbackIndex0={fallbackIndex0}");
            }
            catch (Exception ex)
            {
                lines.Add($"SelectIconString popup entries: read failed ({ex.Message})");
            }
        }

        if (TryGetVisibleSelectString(out var menu))
        {
            AppendMenuListDebug(&menu->AtkUnitBase, "SelectString", lines);
        }
    }

    private static bool TryGetAgentMenu(out AddonSelectString* menu, AgentAddonPredicate isAgentAddon)
    {
        menu = null;
        for (var i = 1; i < 100; i++)
        {
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("SelectString", i).Address;
            if (addon == null)
            {
                break;
            }

            if (!addon->IsVisible || !IsAddonReady(addon) || !isAgentAddon(addon))
            {
                continue;
            }

            menu = (AddonSelectString*)addon;
            return true;
        }

        return false;
    }

    private static bool IsAgentYesnoMenu(AddonSelectString* menu, AgentAddonPredicate isAgentAddon)
    {
        if (menu == null || !isAgentAddon(&menu->AtkUnitBase))
        {
            return false;
        }

        var listNode = menu->AtkUnitBase.GetNodeById(ListNodeId);
        if (listNode == null || !listNode->IsVisible())
        {
            return false;
        }

        return TryGetEntryCount(menu, out var entryCount) && entryCount == YesnoMenuEntryCount;
    }

    private static bool TrySelectTriadIconStringEntry(
        AddonSelectIconString* menu,
        string throttleKey,
        int throttleMs = 400)
    {
        if (menu == null || !IsAddonReady(&menu->AtkUnitBase) || !menu->AtkUnitBase.IsVisible)
        {
            return false;
        }

        if (!EzThrottler.Throttle(throttleKey, throttleMs))
        {
            return false;
        }

        if (!TryFindTriadIconStringEntryIndex(menu, out var index))
        {
            return false;
        }

        return TryFireSelectIconStringEntry(menu, index);
    }

    private static bool TryFireSelectIconStringEntry(AddonSelectIconString* menu, int index)
    {
        if (menu == null || index < 0)
        {
            return false;
        }

        try
        {
            new AddonMaster.SelectIconString(menu).Entries[index].Select();
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[SelectString] SelectIconString AddonMaster entry {index} failed");
        }

        try
        {
            Callback.Fire(&menu->AtkUnitBase, true, index);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[SelectString] SelectIconString Callback.Fire({index}) failed");
        }

        var list = menu->AtkUnitBase.GetComponentListById(ListNodeId);
        if (list != null)
        {
            try
            {
                list->SelectItem(index, true);
                return true;
            }
            catch (Exception ex)
            {
                Svc.Log.Verbose(ex, $"[SelectString] SelectIconString SelectItem({index}) failed");
            }
        }

        return false;
    }

    private static bool TrySelectTriadListEntry(
        AtkUnitBase* menu,
        string throttleKey,
        int throttleMs = 400,
        bool skipThrottle = false)
    {
        if (menu == null || !IsAddonReady(menu) || !menu->IsVisible)
        {
            return false;
        }

        if (!skipThrottle && !EzThrottler.Throttle(throttleKey, throttleMs))
        {
            return false;
        }

        if (!TryFindTriadEntryIndex(menu, out var index))
        {
            return false;
        }

        try
        {
            Callback.Fire(menu, true, index);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[SelectString] Triad Callback.Fire({index}) failed");
        }

        try
        {
            new AddonMaster.SelectString((AddonSelectString*)menu).Entries[index].Select();
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[SelectString] SelectString entry {index} failed");
        }

        return false;
    }

    private static bool TryFindTriadEntryIndex(AtkUnitBase* menu, out int index)
    {
        index = -1;
        var listNode = menu->GetNodeById(ListNodeId);
        if (listNode == null || !listNode->IsVisible())
        {
            return false;
        }

        var list = menu->GetComponentListById(ListNodeId);
        if (list == null)
        {
            return false;
        }

        var count = list->GetItemCount();
        if (count <= 0)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (TryGetListEntryIconId(list, i, out var iconId) && IsTriadListEntryIcon(iconId))
            {
                index = i;
                return true;
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (IsTriadListEntryText(TryGetListEntryText(menu, i)))
            {
                index = i;
                return true;
            }
        }

        if (count == 1)
        {
            index = 0;
            return true;
        }

        return false;
    }

    private static bool TryFindTriadIconStringEntryIndex(AddonSelectIconString* menu, out int index)
    {
        if (TryFindTriadEntryIndex(&menu->AtkUnitBase, out index))
        {
            return true;
        }

        try
        {
            var entryCount = new AddonMaster.SelectIconString(menu).EntryCount;
            if (entryCount is >= 2 and <= 4)
            {
                index = 0;
                return true;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[SelectString] Failed to read SelectIconString entry count");
        }

        index = -1;
        return false;
    }

    private static bool IsTriadListEntryText(string? text) =>
        !string.IsNullOrEmpty(text) &&
        (text.Contains("Triple Triad", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("トリプル", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("triade", StringComparison.OrdinalIgnoreCase));

    private static bool IsTriadListEntryIcon(uint iconId) =>
        Array.IndexOf(TriadListEntryIconIds, iconId) >= 0;

    private static bool TryGetListEntryIconId(AtkComponentList* list, int entryIndex, out uint iconId)
    {
        iconId = 0;
        if (list == null || entryIndex < 0 || entryIndex >= list->GetItemCount())
        {
            return false;
        }

        iconId = list->ItemRendererList[entryIndex].IconId;
        return true;
    }

    private static void AppendMenuListDebug(AtkUnitBase* menu, string addonName, List<string> lines)
    {
        var list = menu->GetComponentListById(ListNodeId);
        var count = list == null ? 0 : list->GetItemCount();
        var listNode = menu->GetNodeById(ListNodeId);
        var foundTriad = TryFindTriadEntryIndex(menu, out var triadIndex);
        lines.Add(
            $"{addonName}: listReady={list != null}, listNodeVisible={listNode != null && listNode->IsVisible()}, entries={count}, triadIndex={triadIndex}, matched={foundTriad}");

        if (list == null)
        {
            lines.Add($"{addonName}: list node {ListNodeId} missing");
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var icon = TryGetListEntryIconId(list, i, out var iconId) ? iconId.ToString() : "?";
            var text = TryGetListEntryText(menu, i) ?? "";
            lines.Add($"  [{i}] icon={icon} text=\"{text}\"");
        }
    }

    private static string? TryGetListEntryText(AtkUnitBase* menu, int index)
    {
        try
        {
            if (menu->NameString.Contains("SelectIconString", StringComparison.Ordinal))
            {
                return new AddonMaster.SelectIconString((AddonSelectIconString*)menu).Entries[index].Text;
            }

            return new AddonMaster.SelectString((AddonSelectString*)menu).Entries[index].Text;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[SelectString] Failed to read entry {index} text");
            return null;
        }
    }

    private static bool TryGetEntryCount(AddonSelectString* menu, out int entryCount)
    {
        entryCount = 0;
        try
        {
            foreach (var _ in new AddonMaster.SelectString(menu).Entries)
            {
                entryCount++;
            }

            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[SelectString] Failed to read entry count");
            return false;
        }
    }

    public static bool TryGetVisibleSelectString(out AddonSelectString* menu) =>
        TryGetVisibleAddon("SelectString", out menu);

    public static bool TryGetVisibleSelectIconString(out AddonSelectIconString* menu) =>
        TryGetVisibleAddon("SelectIconString", out menu);

    private static bool TryGetVisibleAddon<T>(string addonName, out T* menu) where T : unmanaged
    {
        menu = null;
        for (var i = 1; i < 100; i++)
        {
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(addonName, i).Address;
            if (addon == null)
            {
                break;
            }

            if (!addon->IsVisible || !IsAddonReady(addon))
            {
                continue;
            }

            menu = (T*)addon;
            return true;
        }

        return false;
    }
    private delegate bool AgentAddonPredicate(AtkUnitBase* addon);
}
