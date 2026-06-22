using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
using System;
using static ECommons.GenericHelpers;

namespace Saucy.Cactpot;

internal static unsafe class CactpotDialogueHelper
{
    public const int JumboBrokerEntryCount = 6;

    public static bool HasNpcDialogueUi() =>
        TalkHelper.IsVisible() ||
        SelectStringHelper.IsNpcListMenuVisible() ||
        SelectYesnoHelper.IsVisible();

    public static bool IsCashierUiVisible() => HasNpcDialogueUi();

    public static bool IsLotteryWeeklyUiVisible() =>
        SelectStringHelper.TryGetLotteryWeeklyMenu(out _) ||
        (SelectYesnoHelper.TryGetVisible(out var yesno) &&
         SelectYesnoHelper.ShouldPressLotteryYesno(yesno, AgentId.LotteryWeekly));

    public static bool IsLotteryDailyUiVisible() =>
        SelectStringHelper.TryGetLotteryDailyMenu(out _) ||
        (SelectYesnoHelper.TryGetVisible(out var yesno) &&
         SelectYesnoHelper.ShouldPressLotteryYesno(yesno, AgentId.LotteryDaily));

    public static bool IsJumboRewardListVisible() =>
        TryGetAddonByName<AtkUnitBase>("LotteryWeeklyRewardList", out var addon) &&
        IsAddonReady(addon) &&
        addon->IsVisible;

    public static bool IsMiniBoardVisible() =>
        TryGetAddonByName<AtkUnitBase>("LotteryDaily", out var addon) &&
        IsAddonReady(addon) &&
        addon->IsVisible;

    public static bool IsJumboInputVisible() =>
        TryGetAddonByName<AtkUnitBase>("LotteryWeeklyInput", out var addon) &&
        IsAddonReady(addon) &&
        addon->IsVisible;

    public static bool IsTargetingCashier() =>
        Svc.Targets.Target?.BaseId == CactpotNpcs.CashierBaseId ||
        Svc.Targets.SoftTarget?.BaseId == CactpotNpcs.CashierBaseId;

    public static bool IsJumboBrokerPurchaseMenu(AddonSelectString* menu)
    {
        if (menu == null || !menu->AtkUnitBase.IsVisible)
        {
            return false;
        }

        var addonBase = &menu->AtkUnitBase;
        if (SelectYesnoHelper.IsTriadAddon(addonBase) || SelectYesnoHelper.IsArcadeAddon(addonBase))
        {
            return false;
        }

        if (AgentHelper.IsAddonOwnedBy(addonBase, AgentId.LotteryWeekly))
        {
            return TryGetSelectStringEntryCount(menu, out var agentCount) &&
                   agentCount == JumboBrokerEntryCount;
        }

        return TryGetSelectStringEntryCount(menu, out var count) && count == JumboBrokerEntryCount;
    }

    public static bool TryAdvanceTalk(string scope, string throttleKey, AgentId lotteryAgent)
    {
        if (!ObjectHelper.IsTargeting(scope))
        {
            return false;
        }

        if (SelectYesnoHelper.TryGetVisible(out var yesno) &&
            SelectYesnoHelper.ShouldPressLotteryYesno(yesno, lotteryAgent))
        {
            return false;
        }

        return TalkHelper.TryAdvance(throttleKey);
    }

    public static bool TryAdvanceCashierTalk(string throttleKey, AgentId lotteryAgent)
    {
        if (!IsTargetingCashier())
        {
            return false;
        }

        if (SelectYesnoHelper.TryGetVisible(out var yesno) &&
            SelectYesnoHelper.ShouldPressLotteryYesno(yesno, lotteryAgent))
        {
            return false;
        }

        return TalkHelper.TryAdvance(throttleKey);
    }

    public static bool TryHandleLotteryYesno(
        string scope,
        bool inFlow,
        AgentId lotteryAgent,
        string throttleKey,
        int throttleMs)
    {
        if (!NpcDialogueGate.CanAutomateYesno(scope, inFlow) &&
            !(IsTargetingCashier() && inFlow) &&
            !ObjectHelper.IsTargeting(scope))
        {
            return false;
        }

        if (QuestDialogueGuard.ShouldBlockYesno(
                ObjectHelper.IsTargeting(scope) || IsTargetingCashier()))
        {
            return false;
        }

        if (!SelectYesnoHelper.TryGetVisible(out var yesno))
        {
            return false;
        }

        if (!ShouldAutomateLotteryYesno(yesno, lotteryAgent, inFlow))
        {
            return false;
        }

        if (!EzThrottler.Throttle(throttleKey, throttleMs))
        {
            return false;
        }

        return SelectYesnoHelper.PressYes(yesno);
    }

    private static bool ShouldAutomateLotteryYesno(
        AddonSelectYesno* yesno,
        AgentId lotteryAgent,
        bool inFlow) =>
        SelectYesnoHelper.ShouldPressLotteryYesno(yesno, lotteryAgent) ||
        (inFlow &&
         SelectYesnoHelper.HasYesnoButtons(yesno) &&
         !SelectYesnoHelper.IsBlockedSystemPrompt(yesno));

    public static bool TryHandleBrokerSelectString(
        string scope,
        AgentId lotteryAgent,
        string throttleKey,
        int throttleMs)
    {
        if (!ObjectHelper.IsTargeting(scope))
        {
            return false;
        }

        if (!EzThrottler.Throttle(throttleKey, throttleMs))
        {
            return false;
        }

        if (TrySelectLotteryMenu(lotteryAgent))
        {
            return true;
        }

        if (!SelectStringHelper.TryGetVisibleSelectString(out var visibleMenu))
        {
            return false;
        }

        return TrySelectFromMenu(visibleMenu, lotteryAgent);
    }

    public static bool TryHandleCashierSelectString(string throttleKey, int throttleMs)
    {
        if (!IsTargetingCashier())
        {
            return false;
        }

        if (!EzThrottler.Throttle(throttleKey, throttleMs))
        {
            return false;
        }

        if (TrySelectLotteryMenu(AgentId.LotteryWeekly))
        {
            return true;
        }

        if (!SelectStringHelper.TryGetVisibleSelectString(out var visibleMenu))
        {
            return false;
        }

        return TrySelectFromMenu(visibleMenu, AgentId.LotteryWeekly);
    }

    public static bool TryDismissWeeklyRewardList(string throttleKey, int throttleMs, int warmupMs, ref DateTime? seenUtc)
    {
        if (!TryGetAddonByName<AtkUnitBase>("LotteryWeeklyRewardList", out var addon) ||
            !IsAddonReady(addon) ||
            !addon->IsVisible)
        {
            return false;
        }

        seenUtc ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - seenUtc.Value).TotalMilliseconds < warmupMs)
        {
            return false;
        }

        if (!EzThrottler.Throttle(throttleKey, throttleMs))
        {
            return false;
        }

        if (TryGetAddonMaster<AddonMaster.LotteryWeeklyRewardList>(out var rewardList))
        {
            rewardList.Close();
            return true;
        }

        return AddonButton.TryClick(addon, 49);
    }

    private static bool TrySelectLotteryMenu(AgentId lotteryAgent) =>
        lotteryAgent switch
        {
            AgentId.LotteryDaily => SelectStringHelper.TryGetLotteryDailyMenu(out var dailyMenu) &&
                                    TrySelectFromMenu(dailyMenu, lotteryAgent),
            AgentId.LotteryWeekly => SelectStringHelper.TryGetLotteryWeeklyMenu(out var weeklyMenu) &&
                                     TrySelectFromMenu(weeklyMenu, lotteryAgent),
            _ => false
        };

    private static bool TrySelectFromMenu(AddonSelectString* menu, AgentId lotteryAgent)
    {
        if (menu == null || !menu->AtkUnitBase.IsVisible)
        {
            return false;
        }

        if (lotteryAgent == AgentId.LotteryWeekly && IsJumboBrokerPurchaseMenu(menu))
        {
            return false;
        }

        if (lotteryAgent == AgentId.LotteryDaily && SelectStringHelper.IsLotteryDailyYesnoMenu(menu))
        {
            return SelectStringHelper.TrySelectYesEntry(menu);
        }

        if (lotteryAgent == AgentId.LotteryWeekly && SelectStringHelper.IsLotteryWeeklyYesnoMenu(menu))
        {
            return SelectStringHelper.TrySelectYesEntry(menu);
        }

        return SelectStringHelper.TrySelectYesEntry(menu);
    }

    private static bool TryGetSelectStringEntryCount(AddonSelectString* menu, out int count)
    {
        count = 0;
        try
        {
            foreach (var _ in new AddonMaster.SelectString(menu).Entries)
            {
                count++;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
