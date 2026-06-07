using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.Automation.UIInput;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Cactpot;
using Saucy.Framework;
using System;
using static ECommons.GenericHelpers;

namespace Saucy.JumboCactpot;

public unsafe class JumboCactpot : Module
{
    private const string InputAddonName = "LotteryWeeklyInput";
    private const string RewardAddonName = "LotteryWeeklyRewardList";
    private const string TalkThrottleKey = "Saucy.JumboCactpot.Talk";
    private const uint RewardConfirmNodeId = 49;
    private const int RewardWarmupMs = 700;
    private const int BrokerPickThrottleMs = 1500;

    private const int BrokerPurchaseEntryIndex = 0;
    private const int JumboBrokerEntryCount = 6;

    private const uint InputRandomizeNodeId = 32;
    private const uint InputConfirmNodeId = 31;
    private const int InputWarmupMs = 350;
    private const int InputBetweenClicksMs = 200;
    private const int InputNextTicketResetMs = 1500;
    private const int CashierHandoffDismissMs = 600;

    private readonly TimedFlowWindow ticketFlow = new(TimeSpan.FromSeconds(30));
    private bool brokerPathArmed;
    private bool cashierDialogueSeen;
    private DateTime? cashierHandoffDismissedUtc;
    private bool cashierRewardOpen;
    private DateTime? inputAddonSeenUtc;
    private bool inputConfirmed;
    private bool inputRandomized;
    private DateTime? inputRandomizedUtc;
    private DateTime? rewardAddonSeenUtc;

    public override string Name => "Jumbo Cactpot";

    public override void Enable()
    {
        NpcHelper.SetTrackedNpcs(
            CactpotNpcs.JumboBrokerScope,
            [CactpotNpcs.JumboBrokerBaseId],
            CactpotNpcs.JumboBrokerScope);

        Svc.AddonLifecycle.UnregisterListener(OnInputSetup);
        Svc.AddonLifecycle.UnregisterListener(OnInputFinalize);
        Svc.AddonLifecycle.UnregisterListener(OnRewardSetup);
        Svc.AddonLifecycle.UnregisterListener(OnRewardFinalize);
        Svc.AddonLifecycle.UnregisterListener(OnTalkUpdate);
        Svc.Framework.Update -= OnFrameworkUpdate;

        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, InputAddonName, OnInputSetup);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, InputAddonName, OnInputFinalize);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, RewardAddonName, OnRewardSetup);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, RewardAddonName, OnRewardFinalize);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnTalkUpdate);
        Svc.Framework.Update += OnFrameworkUpdate;

        ResetCashierHandoff();
        ticketFlow.Clear();
        JumboCactpotBrokerPath.Reset();
    }

    public override void Disable()
    {
        Svc.AddonLifecycle.UnregisterListener(OnInputSetup);
        Svc.AddonLifecycle.UnregisterListener(OnInputFinalize);
        Svc.AddonLifecycle.UnregisterListener(OnRewardSetup);
        Svc.AddonLifecycle.UnregisterListener(OnRewardFinalize);
        Svc.AddonLifecycle.UnregisterListener(OnTalkUpdate);
        Svc.Framework.Update -= OnFrameworkUpdate;
        NpcHelper.ClearTrackedNpcs(CactpotNpcs.JumboBrokerScope);
        JumboCactpotBrokerPath.Reset();
        rewardAddonSeenUtc = null;
        inputAddonSeenUtc = null;
        inputRandomizedUtc = null;
        inputRandomized = false;
        inputConfirmed = false;
        ticketFlow.Clear();
        ResetCashierHandoff();
    }

    private void OnTalkUpdate(AddonEvent type, AddonArgs args)
    {
        if (InSaucer && NpcHelper.HasInitiatedDialogue(CactpotNpcs.JumboBrokerScope))
        {
            ticketFlow.Mark();
        }

        TryAdvanceTalk();
    }

    private void OnInputSetup(AddonEvent type, AddonArgs args)
    {
        ticketFlow.Mark();
        inputAddonSeenUtc = DateTime.UtcNow;
        inputRandomizedUtc = null;
        inputRandomized = false;
        inputConfirmed = false;
    }

    private void OnInputFinalize(AddonEvent type, AddonArgs args)
    {
        ticketFlow.Mark();
        inputAddonSeenUtc = null;
        inputRandomizedUtc = null;
        inputRandomized = false;
        inputConfirmed = false;
    }

    private void OnRewardSetup(AddonEvent type, AddonArgs args)
    {
        ticketFlow.Mark();
        rewardAddonSeenUtc = DateTime.UtcNow;
        cashierRewardOpen = true;
    }

    private void OnRewardFinalize(AddonEvent type, AddonArgs args)
    {
        ticketFlow.Mark();
        rewardAddonSeenUtc = null;
        if (cashierRewardOpen)
        {
            ArmBrokerPath();
        }

        cashierRewardOpen = false;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (InSaucer)
        {
            TickCashierHandoff();
            JumboCactpotBrokerPath.Tick();
            RefreshTicketFlow();
        }

        TryAdvanceTalk();
        HandleYesno();
        HandleInputAddon();
        HandleRewardAddon();
        HandleBrokerMenu();
    }

    private bool IsBrokerUiBlocking() =>
        IsJumboInputVisible() ||
        TalkHelper.IsVisible() ||
        SelectStringHelper.IsNpcListMenuVisible() ||
        SelectYesnoHelper.IsVisible();

    private static bool IsTargetingCashier() =>
        Svc.Targets.Target?.BaseId == CactpotNpcs.CashierBaseId ||
        Svc.Targets.SoftTarget?.BaseId == CactpotNpcs.CashierBaseId;

    private void ResetCashierHandoff()
    {
        cashierRewardOpen = false;
        cashierDialogueSeen = false;
        brokerPathArmed = false;
        cashierHandoffDismissedUtc = null;
    }

    private void ResetCashierHandoffState()
    {
        cashierDialogueSeen = false;
        cashierHandoffDismissedUtc = null;
    }

    private void TickCashierHandoff()
    {
        if (brokerPathArmed || JumboCactpotBrokerPath.IsComplete)
        {
            return;
        }

        if (cashierRewardOpen || IsJumboAddonReady(RewardAddonName))
        {
            return;
        }

        if (IsTargetingCashier() && (TalkHelper.IsVisible() || SelectStringHelper.IsNpcListMenuVisible()))
        {
            cashierDialogueSeen = true;
            cashierHandoffDismissedUtc = null;
            return;
        }

        if (!cashierDialogueSeen)
        {
            return;
        }

        if (IsBrokerUiBlocking())
        {
            cashierHandoffDismissedUtc = null;
            return;
        }

        cashierHandoffDismissedUtc ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - cashierHandoffDismissedUtc.Value).TotalMilliseconds < CashierHandoffDismissMs)
        {
            return;
        }

        ArmBrokerPath();
        ResetCashierHandoffState();
    }

    private void ArmBrokerPath()
    {
        if (brokerPathArmed || JumboCactpotBrokerPath.IsComplete)
        {
            return;
        }

        brokerPathArmed = true;
        JumboCactpotBrokerPath.Request();
        Log("Cashier done; pathing to Jumbo broker once.");
    }

    private void HandleInputAddon()
    {
        if (!InSaucer || !IsInTicketFlow() || !NpcHelper.IsTargeting(CactpotNpcs.JumboBrokerScope))
        {
            return;
        }

        if (!TryGetAddonByName<AtkUnitBase>(InputAddonName, out var addon))
        {
            return;
        }

        if (!IsAddonReady(addon) || !addon->IsVisible)
        {
            return;
        }

        if (inputAddonSeenUtc == null)
        {
            inputAddonSeenUtc = DateTime.UtcNow;
        }

        if (inputConfirmed)
        {
            if (!inputRandomizedUtc.HasValue ||
                (DateTime.UtcNow - inputRandomizedUtc.Value).TotalMilliseconds <= InputNextTicketResetMs)
            {
                return;
            }

            inputAddonSeenUtc = DateTime.UtcNow;
            inputRandomizedUtc = null;
            inputRandomized = false;
            inputConfirmed = false;
        }

        if ((DateTime.UtcNow - inputAddonSeenUtc.Value).TotalMilliseconds < InputWarmupMs)
        {
            return;
        }

        ticketFlow.Mark();

        if (!inputRandomized)
        {
            if (TryClickButton(addon, InputRandomizeNodeId))
            {
                inputRandomized = true;
                inputRandomizedUtc = DateTime.UtcNow;
                Log($"Randomize clicked (node {InputRandomizeNodeId}).");
            }

            return;
        }

        if (inputRandomizedUtc == null ||
            (DateTime.UtcNow - inputRandomizedUtc.Value).TotalMilliseconds < InputBetweenClicksMs)
        {
            return;
        }

        if (TryClickButton(addon, InputConfirmNodeId))
        {
            inputConfirmed = true;
            Log($"Purchase clicked (node {InputConfirmNodeId}).");
        }
    }

    private static bool TryClickButton(AtkUnitBase* addon, uint nodeId)
    {
        var btn = addon->GetComponentButtonById(nodeId);
        if (btn == null || btn->AtkResNode == null || !btn->AtkResNode->IsVisible() || !btn->IsEnabled)
        {
            return false;
        }

        try
        {
            btn->ClickAddonButton(addon);
            addon->Update(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[JumboCactpot] Button node {nodeId} click failed");
            return false;
        }
    }

    private void HandleBrokerMenu()
    {
        if (!InSaucer || !NpcHelper.HasInitiatedDialogue(CactpotNpcs.JumboBrokerScope))
        {
            return;
        }

        if (!SelectStringHelper.TryGetVisibleSelectString(out var menu) ||
            !IsJumboBrokerSelectString(menu))
        {
            return;
        }

        if (!EzThrottler.Throttle("JumboCactpotBrokerPick", BrokerPickThrottleMs))
        {
            return;
        }

        ticketFlow.Mark();
        if (SelectStringHelper.TrySelectEntry(menu, BrokerPurchaseEntryIndex))
        {
            Log($"Broker SelectString entry {BrokerPurchaseEntryIndex} selected.");
        }
        else
        {
            LogVerbose($"Broker SelectString entry {BrokerPurchaseEntryIndex} select failed.");
        }
    }

    private static bool IsJumboBrokerSelectString(AddonSelectString* menu)
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
            return true;
        }

        return TryGetSelectStringEntryCount(menu, out var count) && count == JumboBrokerEntryCount;
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

    private void HandleRewardAddon()
    {
        if (!TryGetAddonByName<AtkUnitBase>(RewardAddonName, out var addon) ||
            !IsAddonReady(addon) ||
            !addon->IsVisible)
        {
            return;
        }

        if (rewardAddonSeenUtc == null)
        {
            rewardAddonSeenUtc = DateTime.UtcNow;
        }

        if ((DateTime.UtcNow - rewardAddonSeenUtc.Value).TotalMilliseconds < RewardWarmupMs)
        {
            return;
        }

        if (!EzThrottler.Throttle("JumboCactpotRewardClick", MinigameInputPacing.ClickIntervalMs))
        {
            return;
        }

        ticketFlow.Mark();
        if (!TryClickButton(addon, RewardConfirmNodeId))
        {
            LogVerbose($"Reward confirm (node {RewardConfirmNodeId}) click skipped.");
        }
    }

    private void TryAdvanceTalk()
    {
        if (!InSaucer || !NpcHelper.HasInitiatedDialogue(CactpotNpcs.JumboBrokerScope))
        {
            return;
        }

        if (SelectYesnoHelper.TryGetVisible(out var yesno) &&
            !SelectYesnoHelper.ShouldPressLotteryYesno(yesno, AgentId.LotteryWeekly))
        {
            return;
        }

        TalkHelper.TryAdvance(TalkThrottleKey);
    }

    private void HandleYesno()
    {
        if (!InSaucer || !NpcDialogueGate.CanAutomateYesno(CactpotNpcs.JumboBrokerScope, IsInTicketFlow()))
        {
            return;
        }

        if (QuestDialogueGuard.ShouldBlockYesno(NpcHelper.IsTargeting(CactpotNpcs.JumboBrokerScope)))
        {
            return;
        }

        if (!SelectYesnoHelper.TryGetVisible(out var yesno) ||
            !SelectYesnoHelper.ShouldPressLotteryYesno(yesno, AgentId.LotteryWeekly))
        {
            return;
        }

        if (!EzThrottler.Throttle("JumboCactpotYes", MinigameInputPacing.ClickIntervalMs))
        {
            return;
        }

        ticketFlow.Mark();
        if (!SelectYesnoHelper.PressYes(yesno))
        {
            LogVerbose("SelectYesno Yes press failed.");
        }
    }

    private void RefreshTicketFlow() =>
        NpcDialogueGate.RefreshTimedFlow(
            CactpotNpcs.JumboBrokerScope,
            IsInTicketFlow(),
            ticketFlow.Mark,
            HasTicketFlowUi);

    private bool HasTicketFlowUi() =>
        TalkHelper.IsVisible() ||
        SelectStringHelper.IsNpcListMenuVisible() ||
        SelectYesnoHelper.IsVisible() ||
        IsJumboInputVisible();

    private bool IsInTicketFlow() =>
        IsJumboInputVisible() ||
        AgentHelper.IsActive(AgentId.LotteryWeekly) ||
        ticketFlow.IsActive;

    private bool IsJumboInputVisible() => IsJumboAddonReady(InputAddonName);

    private static bool IsJumboAddonReady(string addonName) =>
        TryGetAddonByName<AtkUnitBase>(addonName, out var addon) &&
        IsAddonReady(addon) &&
        addon->IsVisible;
}
