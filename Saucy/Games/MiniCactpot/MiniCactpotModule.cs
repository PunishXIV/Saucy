using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.Automation.UIInput;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Cactpot;
using Saucy.Framework;
using System;
using System.Linq;
using static ECommons.GenericHelpers;

namespace Saucy.MiniCactpot;

public unsafe class MiniCactpot : Module
{
    private const uint ConfirmButtonNodeId = 67;

    private const string TalkThrottleKey = "Saucy.MiniCactpot.Talk";

    private readonly int[] scratchState = new int[9];
    private readonly CactpotSolver solver = new();
    private readonly TimedFlowWindow ticketFlow = new(TimeSpan.FromSeconds(30));
    private DateTime? lotteryBoardReadyUtc;

    public override string Name => "Mini Cactpot";

    public override void Enable()
    {
        ObjectHelper.SetTrackedObjects(CactpotNpcs.MiniScope, [CactpotNpcs.MiniBrokerBaseId], logLabel: CactpotNpcs.MiniScope);

        Svc.AddonLifecycle.UnregisterListener(OnLotterySetup);
        Svc.AddonLifecycle.UnregisterListener(OnPreFinalize);
        Svc.AddonLifecycle.UnregisterListener(OnTalkUpdate);
        Svc.Framework.Update -= OnFrameworkUpdate;

        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "LotteryDaily", OnLotterySetup);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "LotteryDaily", OnPreFinalize);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnTalkUpdate);
        Svc.Framework.Update += OnFrameworkUpdate;
        ticketFlow.Clear();
    }

    public override void Disable()
    {
        Svc.AddonLifecycle.UnregisterListener(OnLotterySetup);
        Svc.AddonLifecycle.UnregisterListener(OnPreFinalize);
        Svc.AddonLifecycle.UnregisterListener(OnTalkUpdate);
        Svc.Framework.Update -= OnFrameworkUpdate;
        ObjectHelper.ClearTrackedObjects(CactpotNpcs.MiniScope);
        ticketFlow.Clear();
    }

    private void OnLotterySetup(AddonEvent type, AddonArgs args)
    {
        ticketFlow.Mark();
        MinigameInputPacing.Reset(ref lotteryBoardReadyUtc);
    }

    private void OnPreFinalize(AddonEvent type, AddonArgs args)
    {
        ticketFlow.Mark();
        MinigameInputPacing.Reset(ref lotteryBoardReadyUtc);
    }

    private void OnTalkUpdate(AddonEvent type, AddonArgs args)
    {
        if (InSaucer && ObjectHelper.HasInitiatedDialogue(CactpotNpcs.MiniScope))
        {
            ticketFlow.Mark();
        }

        TryAdvanceDialogue();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (InSaucer)
        {
            RefreshTicketFlow();
        }

        TryAdvanceDialogue();
        HandleYesno();

        if (InSaucer && IsInTicketFlow() && ObjectHelper.IsTargeting(CactpotNpcs.MiniScope) && TryGetLotteryAddon(out var addon))
        {
            ProcessAddon(addon);
        }
    }

    private void TryAdvanceDialogue()
    {
        if (!InSaucer || !ObjectHelper.HasInitiatedDialogue(CactpotNpcs.MiniScope))
        {
            return;
        }

        if (SelectYesnoHelper.TryGetVisible(out var yesno) &&
            !SelectYesnoHelper.ShouldPressLotteryYesno(yesno, AgentId.LotteryDaily))
        {
            return;
        }

        TalkHelper.TryAdvance(TalkThrottleKey);
    }

    private void HandleYesno()
    {
        if (!InSaucer || !NpcDialogueGate.CanAutomateYesno(CactpotNpcs.MiniScope, IsInTicketFlow()))
        {
            return;
        }

        if (QuestDialogueGuard.ShouldBlockYesno(ObjectHelper.IsTargeting(CactpotNpcs.MiniScope)))
        {
            return;
        }

        if (!SelectYesnoHelper.TryGetVisible(out var yesno) ||
            !SelectYesnoHelper.ShouldPressLotteryYesno(yesno, AgentId.LotteryDaily))
        {
            return;
        }

        if (!EzThrottler.Throttle("MiniCactpotTicketYes", MinigameInputPacing.ClickIntervalMs))
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
            CactpotNpcs.MiniScope,
            IsInTicketFlow(),
            ticketFlow.Mark,
            HasTicketFlowUi);

    private bool HasTicketFlowUi() =>
        TalkHelper.IsVisible() ||
        SelectStringHelper.IsNpcListMenuVisible() ||
        SelectYesnoHelper.IsVisible() ||
        TryGetLotteryAddon(out var _);

    private bool IsInTicketFlow() =>
        TryGetLotteryAddon(out var _) ||
        AgentHelper.IsActive(AgentId.LotteryDaily) ||
        ticketFlow.IsActive;

    private void ProcessAddon(AddonLotteryDaily* addon)
    {
        if (TalkHelper.IsVisible())
        {
            if (ObjectHelper.HasInitiatedDialogue(CactpotNpcs.MiniScope))
            {
                TalkHelper.TryAdvance(TalkThrottleKey);
            }

            return;
        }

        if (!MinigameInputPacing.TryMarkWarmup(ref lotteryBoardReadyUtc))
        {
            return;
        }

        var stage = GetStage(addon);
        ReadBoardState(addon, scratchState);
        var revealed = CountRevealed(scratchState);

        if (stage == 5)
        {
            if (EzThrottler.Throttle("MiniCactpotClose", MinigameInputPacing.ClickIntervalMs))
            {
                TryClickConfirmButton(expectedStage: 5);
            }

            return;
        }

        if (revealed < 4)
        {
            if (!EzThrottler.Throttle("MiniCactpotTile", MinigameInputPacing.ClickIntervalMs))
            {
                return;
            }

            TryRevealNextTile(addon, scratchState);
            return;
        }

        if (EzThrottler.Throttle("MiniCactpotLane", MinigameInputPacing.ClickIntervalMs))
        {
            TrySelectLane(addon, scratchState);
            return;
        }

        if (EzThrottler.Throttle("MiniCactpotConfirm", MinigameInputPacing.ClickIntervalMs))
        {
            TryClickConfirmButton(expectedStage: -1);
        }
    }

    private void TryRevealNextTile(AddonLotteryDaily* addon, int[] state)
    {
        if (CountRevealed(state) == 0)
        {
            return;
        }

        try
        {
            var solution = solver.Solve(state);
            if (solution.Length == 8)
            {
                return;
            }

            var activeIndexes = CollectActiveIndexes(solution)
                .Where(i => state[i] == 0)
                .ToArray();
            if (activeIndexes.Length == 0)
            {
                return;
            }

            TryClickTile(addon, activeIndexes[0]);
        }
        catch (Exception ex)
        {
            LogError($"Error revealing tile: {ex.Message}");
        }
    }

    private void TrySelectLane(AddonLotteryDaily* addon, int[] state)
    {
        try
        {
            var solution = solver.Solve(state);
            if (solution.Length != 8)
            {
                return;
            }

            var activeIndexes = CollectActiveIndexes(solution);
            if (activeIndexes.Length == 0)
            {
                return;
            }

            var csLane = SolverLaneToCsLane(activeIndexes[0]);
            TryClickLane(addon, csLane);
        }
        catch (Exception ex)
        {
            LogError($"Error selecting lane: {ex.Message}");
        }
    }

    private static bool TryGetLotteryAddon(out AddonLotteryDaily* addon)
    {
        if (TryGetAddonByName("LotteryDaily", out addon) && IsAddonReady(&addon->AtkUnitBase))
        {
            return true;
        }

        addon = null;
        return false;
    }

    private static int GetStage(AddonLotteryDaily* addon)
    {
        var unit = &addon->AtkUnitBase;
        if (unit->AtkValuesCount < 1)
        {
            return -1;
        }

        var value = unit->AtkValues[0];
        return value.Type == AtkValueType.Int ? value.Int : -1;
    }

    private static void ReadBoardState(AddonLotteryDaily* addon, int[] dest)
    {
        for (var i = 0; i < 9; i++)
        {
            dest[i] = addon->GameNumbers[i];
        }
    }

    private static int CountRevealed(int[] state)
    {
        var count = 0;
        for (var i = 0; i < state.Length; i++)
        {
            if (state[i] > 0)
            {
                count++;
            }
        }

        return count;
    }

    private static int[] CollectActiveIndexes(bool[] solution)
    {
        var count = 0;
        for (var i = 0; i < solution.Length; i++)
        {
            if (solution[i])
            {
                count++;
            }
        }

        var activeIndexes = new int[count];
        var index = 0;
        for (var i = 0; i < solution.Length; i++)
        {
            if (solution[i])
            {
                activeIndexes[index++] = i;
            }
        }

        return activeIndexes;
    }

    private static bool TryClickTile(AddonLotteryDaily* addon, int tileIndex)
    {
        var tile = addon->GameBoard[tileIndex];
        if (tile == null)
        {
            return false;
        }

        return AddonButton.TryClick(&addon->AtkUnitBase, (AtkComponentButton*)tile, requireEnabled: false);
    }

    private static bool TryClickLane(AddonLotteryDaily* addon, int csLane)
    {
        var lane = addon->LaneSelector[csLane];
        if (lane->AtkComponentButton.AtkResNode == null)
        {
            return false;
        }

        try
        {
            lane->ClickRadioButton(&addon->AtkUnitBase);
            addon->AtkUnitBase.Update(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[MiniCactpot] Lane {csLane} click failed");
            return false;
        }
    }

    private static bool TryClickConfirmButton(int expectedStage)
    {
        if (!TryGetLotteryAddon(out var addon))
        {
            return true;
        }

        var stage = GetStage(addon);
        if (expectedStage == 5)
        {
            if (stage != 5)
            {
                return false;
            }
        }
        else if (stage == 5)
        {
            return true;
        }

        return AddonButton.TryClick(&addon->AtkUnitBase, ConfirmButtonNodeId);
    }

    private static int SolverLaneToCsLane(int lane) =>
        lane switch
        {
            0 => 5,
            1 => 6,
            2 => 7,
            3 => 1,
            4 => 2,
            5 => 3,
            6 => 0,
            7 => 4,
            var _ => throw new ArgumentOutOfRangeException($"{nameof(lane)}", lane, "Must be between 0 and 8 (inclusive)")
        };
}
