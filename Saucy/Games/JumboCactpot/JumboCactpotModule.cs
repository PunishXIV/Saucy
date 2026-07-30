using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Cactpot;
using Saucy.Framework;
using Saucy.IPC;
using System;
using static ECommons.GenericHelpers;

namespace Saucy.JumboCactpot;

public unsafe class JumboCactpot : Module
{
    private const string InputAddonName = "LotteryWeeklyInput";
    private const string RewardAddonName = "LotteryWeeklyRewardList";
    private const string TalkThrottleKey = "Saucy.JumboCactpot.Talk";
    private const string CashierTalkThrottleKey = "Saucy.JumboCactpot.CashierTalk";
    private const string CashierSelectThrottleKey = "Saucy.JumboCactpot.CashierSelect";
    private const string CashierYesThrottleKey = "Saucy.JumboCactpot.CashierYes";
    private const int RewardWarmupMs = 700;
    private const int BrokerPickThrottleMs = 1500;

    private const int BrokerPurchaseEntryIndex = 0;

    private const uint InputRandomizeNodeId = 32;
    private const uint InputConfirmNodeId = 31;
    private const int InputWarmupMs = 350;
    private const int InputBetweenClicksMs = 200;
    private const int InputVerifyDelayMs = 400;
    private const int InputNextTicketResetMs = 1500;
    private const int CashierHandoffDismissMs = 600;
    private const int CashierNextRewardGraceMs = 3000;

    private readonly TimedFlowWindow ticketFlow = new(TimeSpan.FromSeconds(30));
    private bool brokerPathArmed;
    private bool cashierDialogueSeen;
    private DateTime? cashierHandoffDismissedUtc;
    private DateTime? lastCashierRewardClosedUtc;
    private bool cashierRewardOpen;
    private DateTime? inputAddonSeenUtc;
    private bool inputConfirmed;
    private bool inputPrepared;
    private DateTime? inputPreparedUtc;
    private int ticketPurchaseIndex;
    private DateTime? rewardAddonSeenUtc;
    private int keypadDigitIndex;
    private int keypadEntryTarget = -1;
    private DateTime? lastKeypadClickUtc;
    private bool wasBetweenAreas;

    public override string Name => "Jumbo Cactpot";

    public override void Enable()
    {
        ObjectHelper.SetTrackedObjects(
            CactpotNpcs.JumboBrokerScope,
            [CactpotNpcs.JumboBrokerBaseId],
            logLabel: CactpotNpcs.JumboBrokerScope);
        ObjectHelper.SetTrackedObjects(
            CactpotNpcs.CashierScope,
            [CactpotNpcs.CashierBaseId],
            logLabel: CactpotNpcs.CashierScope);

        Svc.AddonLifecycle.UnregisterListener(OnInputSetup);
        Svc.AddonLifecycle.UnregisterListener(OnInputFinalize);
        Svc.AddonLifecycle.UnregisterListener(OnRewardSetup);
        Svc.AddonLifecycle.UnregisterListener(OnRewardFinalize);
        Svc.AddonLifecycle.UnregisterListener(OnTalkUpdate);
        Svc.Framework.Update -= OnFrameworkUpdate;
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;

        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, InputAddonName, OnInputSetup);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, InputAddonName, OnInputFinalize);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, RewardAddonName, OnRewardSetup);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, RewardAddonName, OnRewardFinalize);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnTalkUpdate);
        Svc.Framework.Update += OnFrameworkUpdate;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;

        wasBetweenAreas = Svc.Condition[ConditionFlag.BetweenAreas];
        ResetCashierHandoff();
        ticketFlow.Clear();
        ticketPurchaseIndex = 0;
        ResetKeypadEntry();
        LotteryWeeklyInputHelper.ClearDiscoveryCache();
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
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        ObjectHelper.ClearTrackedObjects(CactpotNpcs.JumboBrokerScope);
        ObjectHelper.ClearTrackedObjects(CactpotNpcs.CashierScope);
        JumboCactpotBrokerPath.Reset();
        rewardAddonSeenUtc = null;
        inputAddonSeenUtc = null;
        inputPreparedUtc = null;
        inputPrepared = false;
        inputConfirmed = false;
        ticketPurchaseIndex = 0;
        ticketFlow.Clear();
        ResetKeypadEntry();
        LotteryWeeklyInputHelper.ClearDiscoveryCache();
        ResetCashierHandoff();
        CactpotSessionActivity.ResetJumbo();
        wasBetweenAreas = false;
    }

    private void OnTalkUpdate(AddonEvent type, AddonArgs args)
    {
        if (InSaucer && ObjectHelper.HasInitiatedDialogue(CactpotNpcs.JumboBrokerScope))
        {
            ticketFlow.Mark();
        }

        if (InSaucer && CactpotDialogueHelper.IsTargetingCashier())
        {
            cashierDialogueSeen = true;
        }

        TryAdvanceTalk();
        TryAdvanceCashierTalk();
    }

    private void OnInputSetup(AddonEvent type, AddonArgs args)
    {
        ticketFlow.Mark();
        inputAddonSeenUtc = DateTime.UtcNow;
        inputPreparedUtc = null;
        inputPrepared = false;
        inputConfirmed = false;
        ResetKeypadEntry();
    }

    private void OnInputFinalize(AddonEvent type, AddonArgs args)
    {
        ticketFlow.Mark();
        inputAddonSeenUtc = null;
        inputPreparedUtc = null;
        inputPrepared = false;
        inputConfirmed = false;
        ResetKeypadEntry();
    }

    private void OnRewardSetup(AddonEvent type, AddonArgs args)
    {
        ticketFlow.Mark();
        rewardAddonSeenUtc = DateTime.UtcNow;
        cashierRewardOpen = true;
        lastCashierRewardClosedUtc = null;
        JumboCactpotBrokerPath.Reset();
        brokerPathArmed = false;
    }

    private void OnRewardFinalize(AddonEvent type, AddonArgs args)
    {
        ticketFlow.Mark();
        rewardAddonSeenUtc = null;
        lastCashierRewardClosedUtc = DateTime.UtcNow;
        cashierRewardOpen = false;
    }

    private void OnTerritoryChanged(uint territoryType) => AbandonJumboSession();

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Teleports keep territory 144 (aetheryte) or hold BetweenAreas before the
        // territory swap — !InSaucer alone never sees those. Cancel mid-ticket handoff
        // on any zone transition so YesAlready is not re-locked on landing.
        var betweenAreas = Svc.Condition[ConditionFlag.BetweenAreas];
        if ((betweenAreas && !wasBetweenAreas) || !InSaucer)
        {
            AbandonJumboSession();
        }

        wasBetweenAreas = betweenAreas;

        SyncYesAlreadyPauseState();

        if (InSaucer && !betweenAreas)
        {
            TickCashierHandoff();
            JumboCactpotBrokerPath.Tick();
            RefreshTicketFlow();
        }

        TryAdvanceTalk();
        TryAdvanceCashierTalk();
        HandleYesno();
        HandleCashierYesno();
        HandleCashierSelectString();
        HandleInputAddon();
        HandleRewardAddon();
        HandleBrokerMenu();

        if (C.IsModuleEnabled(ModuleNames.JumboCactpot))
        {
            YesAlready.SyncForGameActivity(GoldSaucerGameActivity.IsAnyGamePlaying());
        }

        ClearSessionIfIdle();
    }

    private bool ShouldPauseYesAlready()
    {
        // Never hold YesAlready across loads / teleports.
        if (!InSaucer || Svc.Condition[ConditionFlag.BetweenAreas])
        {
            return false;
        }

        return CactpotDialogueHelper.IsJumboInputVisible() ||
               CactpotDialogueHelper.IsJumboRewardListVisible() ||
               IsWaitingForNextCashierReward() ||
               JumboCactpotBrokerPath.IsActive ||
               IsCashierHandoffPending() ||
               (IsInCashierFlow() && (CactpotDialogueHelper.IsCashierUiVisible() || AgentHelper.IsActive(AgentId.LotteryWeekly))) ||
               (ObjectHelper.IsTargeting(CactpotNpcs.JumboBrokerScope) && HasTicketFlowUi());
    }

    /// <summary>
    /// True only during the brief post-cashier dismiss window before broker path arms.
    /// <see cref="cashierDialogueSeen"/> alone must not keep YesAlready paused.
    /// </summary>
    private bool IsCashierHandoffPending() =>
        cashierDialogueSeen &&
        cashierHandoffDismissedUtc != null &&
        !brokerPathArmed &&
        !JumboCactpotBrokerPath.IsActive;

    private bool HasJumboSessionState() =>
        cashierDialogueSeen ||
        cashierRewardOpen ||
        brokerPathArmed ||
        cashierHandoffDismissedUtc != null ||
        lastCashierRewardClosedUtc != null ||
        JumboCactpotBrokerPath.IsActive ||
        JumboCactpotBrokerPath.IsComplete ||
        ticketFlow.IsActive;

    private void AbandonJumboSession()
    {
        if (!HasJumboSessionState())
        {
            return;
        }

        JumboCactpotBrokerPath.Reset();
        ResetCashierHandoff();
        ticketFlow.Clear();
        ticketPurchaseIndex = 0;
        ResetKeypadEntry();
        rewardAddonSeenUtc = null;
        inputAddonSeenUtc = null;
        inputPreparedUtc = null;
        inputPrepared = false;
        inputConfirmed = false;
        CactpotSessionActivity.ResetJumbo();
        Log("Jumbo session abandoned (zone/teleport).");
    }

    private void ClearSessionIfIdle()
    {
        // Path may be Reset() by a teleport while brokerPathArmed is still true.
        if (brokerPathArmed && !JumboCactpotBrokerPath.IsActive)
        {
            brokerPathArmed = false;
        }

        // Do not gate cleanup on ShouldPauseYesAlready — sticky handoff flags used to
        // make that true forever and prevent ResetCashierHandoff from ever running.
        if (HasVisibleOrActiveJumboUi() || IsCashierHandoffPending())
        {
            return;
        }

        ticketFlow.Clear();

        if (cashierHandoffDismissedUtc != null)
        {
            return;
        }

        if (!IsTargetingCashier() &&
            !HasTicketFlowUi() &&
            !JumboCactpotBrokerPath.IsActive &&
            !brokerPathArmed)
        {
            ResetCashierHandoff();
            if (JumboCactpotBrokerPath.IsComplete)
            {
                JumboCactpotBrokerPath.Reset();
            }
        }
    }

    private bool HasVisibleOrActiveJumboUi() =>
        CactpotDialogueHelper.IsJumboInputVisible() ||
        CactpotDialogueHelper.IsJumboRewardListVisible() ||
        IsWaitingForNextCashierReward() ||
        JumboCactpotBrokerPath.IsActive ||
        (IsInCashierFlow() && (CactpotDialogueHelper.IsCashierUiVisible() || AgentHelper.IsActive(AgentId.LotteryWeekly))) ||
        (ObjectHelper.IsTargeting(CactpotNpcs.JumboBrokerScope) && HasTicketFlowUi());

    private void TryAdvanceCashierTalk()
    {
        if (!InSaucer || !IsInCashierFlow())
        {
            return;
        }

        if (CactpotDialogueHelper.TryAdvanceCashierTalk(CashierTalkThrottleKey, AgentId.LotteryWeekly))
        {
            ticketFlow.Mark();
        }
    }

    private void HandleCashierSelectString()
    {
        if (!InSaucer || !IsInCashierFlow())
        {
            return;
        }

        if (CactpotDialogueHelper.TryHandleCashierSelectString(
                CashierSelectThrottleKey,
                MinigameInputPacing.ClickIntervalMs))
        {
            ticketFlow.Mark();
        }
    }

    private void HandleCashierYesno()
    {
        if (!InSaucer || !IsInCashierFlow())
        {
            return;
        }

        if (CactpotDialogueHelper.TryHandleLotteryYesno(
                CactpotNpcs.CashierScope,
                inFlow: true,
                AgentId.LotteryWeekly,
                CashierYesThrottleKey,
                MinigameInputPacing.ClickIntervalMs))
        {
            ticketFlow.Mark();
        }
    }

    private bool IsInCashierFlow() =>
        cashierDialogueSeen ||
        cashierRewardOpen ||
        IsJumboAddonReady(RewardAddonName) ||
        (CactpotDialogueHelper.IsTargetingCashier() &&
         (AgentHelper.IsActive(AgentId.LotteryWeekly) || ObjectHelper.HasInitiatedDialogue(CactpotNpcs.CashierScope)));

    private void SyncYesAlreadyPauseState() =>
        CactpotSessionActivity.SyncJumbo(InSaucer, ShouldPauseYesAlready());

    private void ResetCashierHandoff()
    {
        cashierRewardOpen = false;
        cashierDialogueSeen = false;
        brokerPathArmed = false;
        cashierHandoffDismissedUtc = null;
        lastCashierRewardClosedUtc = null;
    }

    private void ResetCashierHandoffState()
    {
        cashierDialogueSeen = false;
        cashierHandoffDismissedUtc = null;
        lastCashierRewardClosedUtc = null;
    }

    private static bool IsTargetingCashier() => CactpotDialogueHelper.IsTargetingCashier();

    private bool IsBrokerUiBlocking() =>
        CactpotDialogueHelper.IsJumboInputVisible() ||
        CactpotDialogueHelper.HasNpcDialogueUi();

    private bool IsWaitingForNextCashierReward()
    {
        if (lastCashierRewardClosedUtc == null)
        {
            return false;
        }

        return (DateTime.UtcNow - lastCashierRewardClosedUtc.Value).TotalMilliseconds < CashierNextRewardGraceMs;
    }

    private void TickCashierHandoff()
    {
        if (brokerPathArmed || JumboCactpotBrokerPath.IsActive)
        {
            return;
        }

        if (cashierRewardOpen || IsJumboAddonReady(RewardAddonName))
        {
            return;
        }

        if (IsWaitingForNextCashierReward())
        {
            cashierHandoffDismissedUtc = null;
            return;
        }

        if (IsTargetingCashier() && CactpotDialogueHelper.IsCashierUiVisible())
        {
            cashierDialogueSeen = true;
            cashierHandoffDismissedUtc = null;
            return;
        }

        if (IsInCashierFlow() && CactpotDialogueHelper.HasNpcDialogueUi())
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

        if (IsTargetingCashier())
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
        if (brokerPathArmed || JumboCactpotBrokerPath.IsActive)
        {
            return;
        }

        if (JumboCactpotBrokerPath.IsComplete)
        {
            JumboCactpotBrokerPath.Reset();
        }

        brokerPathArmed = true;
        ticketPurchaseIndex = 0;
        ResetKeypadEntry();
        JumboCactpotBrokerPath.Request();
        Log("Cashier done; pathing to Jumbo broker once.");
    }

    private void HandleInputAddon()
    {
        if (!InSaucer || !IsInTicketFlow() || !ObjectHelper.IsTargeting(CactpotNpcs.JumboBrokerScope))
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
            if (!inputPreparedUtc.HasValue ||
                (DateTime.UtcNow - inputPreparedUtc.Value).TotalMilliseconds <= InputNextTicketResetMs)
            {
                return;
            }

            inputAddonSeenUtc = DateTime.UtcNow;
            inputPreparedUtc = null;
            inputPrepared = false;
            inputConfirmed = false;
        }

        if ((DateTime.UtcNow - inputAddonSeenUtc.Value).TotalMilliseconds < InputWarmupMs)
        {
            return;
        }

        ticketFlow.Mark();

        if (!inputPrepared)
        {
            if (keypadDigitIndex is > 0 and < 4 &&
                lastKeypadClickUtc.HasValue &&
                (DateTime.UtcNow - lastKeypadClickUtc.Value).TotalMilliseconds < InputBetweenClicksMs)
            {
                return;
            }

            if (TryPrepareTicketNumber(addon))
            {
                inputPrepared = true;
                inputPreparedUtc = DateTime.UtcNow;
            }

            return;
        }

        if (inputPreparedUtc == null ||
            (DateTime.UtcNow - inputPreparedUtc.Value).TotalMilliseconds < InputBetweenClicksMs)
        {
            return;
        }

        if (AddonButton.TryClick(addon, InputConfirmNodeId))
        {
            inputConfirmed = true;
            ticketPurchaseIndex++;
            Log($"Purchase clicked (node {InputConfirmNodeId}).");
        }
    }

    private bool TryPrepareTicketNumber(AtkUnitBase* addon)
    {
        if (TryGetSpecificTicketNumber(out var specificNumber))
        {
            if (keypadEntryTarget != specificNumber)
            {
                keypadEntryTarget = specificNumber;
                keypadDigitIndex = 0;
            }

            Span<char> formatted = stackalloc char[4];
            JumboCactpotSettings.FormatTicketNumber(specificNumber, formatted);

            if (keypadDigitIndex < 4)
            {
                if (!LotteryWeeklyInputHelper.TryClickKeypadDigit(addon, formatted[keypadDigitIndex] - '0'))
                {
                    Log($"Ticket {ticketPurchaseIndex + 1}: could not set {specificNumber:D4}; falling back to random.");
                    ResetKeypadEntry();
                    return TryClickRandom(addon);
                }

                keypadDigitIndex++;
                lastKeypadClickUtc = DateTime.UtcNow;
                return false;
            }

            if (lastKeypadClickUtc.HasValue &&
                (DateTime.UtcNow - lastKeypadClickUtc.Value).TotalMilliseconds < InputVerifyDelayMs)
            {
                return false;
            }

            Log($"Ticket {ticketPurchaseIndex + 1}: set number {new string(formatted)}.");
            ResetKeypadEntry();
            return true;
        }

        ResetKeypadEntry();
        return TryClickRandom(addon);
    }

    private bool TryClickRandom(AtkUnitBase* addon)
    {
        if (AddonButton.TryClick(addon, InputRandomizeNodeId))
        {
            Log($"Randomize clicked (node {InputRandomizeNodeId}).");
            return true;
        }

        return false;
    }

    private void ResetKeypadEntry()
    {
        keypadDigitIndex = 0;
        keypadEntryTarget = -1;
        lastKeypadClickUtc = null;
    }

    private bool TryGetSpecificTicketNumber(out int number)
    {
        number = 0;
        if (C.JumboCactpot.NumberMode != JumboCactpotNumberMode.Specific)
        {
            return false;
        }

        return JumboCactpotSettings.TryParseTicketNumber(
            C.JumboCactpot.GetTicketNumber(ticketPurchaseIndex),
            out number);
    }

    private void HandleBrokerMenu()
    {
        if (!InSaucer || !ObjectHelper.HasInitiatedDialogue(CactpotNpcs.JumboBrokerScope))
        {
            return;
        }

        if (!SelectStringHelper.TryGetVisibleSelectString(out var menu) ||
            !CactpotDialogueHelper.IsJumboBrokerPurchaseMenu(menu))
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

    private void HandleRewardAddon()
    {
        if (!InSaucer || !IsInCashierFlow())
        {
            return;
        }

        if (CactpotDialogueHelper.TryDismissWeeklyRewardList(
                "JumboCactpotRewardClick",
                MinigameInputPacing.ClickIntervalMs,
                RewardWarmupMs,
                ref rewardAddonSeenUtc))
        {
            ticketFlow.Mark();
        }
    }

    private void TryAdvanceTalk()
    {
        if (!InSaucer)
        {
            return;
        }

        CactpotDialogueHelper.TryAdvanceTalk(
            CactpotNpcs.JumboBrokerScope,
            TalkThrottleKey,
            AgentId.LotteryWeekly);
    }

    private void HandleYesno()
    {
        if (!InSaucer)
        {
            return;
        }

        if (CactpotDialogueHelper.TryHandleLotteryYesno(
                CactpotNpcs.JumboBrokerScope,
                IsInTicketFlow(),
                AgentId.LotteryWeekly,
                "JumboCactpotYes",
                MinigameInputPacing.ClickIntervalMs))
        {
            ticketFlow.Mark();
        }
    }

    private void RefreshTicketFlow() =>
        NpcDialogueGate.RefreshTimedFlow(
            CactpotNpcs.JumboBrokerScope,
            IsInTicketFlow(),
            ticketFlow.Mark,
            HasTicketFlowUi);

    private bool HasTicketFlowUi() =>
        CactpotDialogueHelper.HasNpcDialogueUi() ||
        CactpotDialogueHelper.IsJumboInputVisible();

    private bool IsInTicketFlow() =>
        CactpotDialogueHelper.IsJumboInputVisible() ||
        AgentHelper.IsActive(AgentId.LotteryWeekly) ||
        ticketFlow.IsActive ||
        (SelectYesnoHelper.IsVisible() && ObjectHelper.IsTargeting(CactpotNpcs.JumboBrokerScope));

    private bool IsJumboInputVisible() => CactpotDialogueHelper.IsJumboInputVisible();

    private static bool IsJumboAddonReady(string addonName) =>
        TryGetAddonByName<AtkUnitBase>(addonName, out var addon) &&
        IsAddonReady(addon) &&
        addon->IsVisible;
}
