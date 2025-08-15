using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.Automation;
using ECommons.Automation.UIInput;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
using Saucy.OutOnALimb.ECEmbedded;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Saucy.MiniCactpot;
public unsafe class MiniCactpot : Module
{
    public override string Name => "Mini Cactpot";

    private readonly CactpotSolver _solver = new();
    private int[]? boardState;
    private bool isProcessing = false;

    public override void Enable() => Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "LotteryDaily", OnUpdate);
    public override void Disable() => Svc.AddonLifecycle.UnregisterListener(OnUpdate);

    private void OnUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonLotteryDaily*)args.Addon.Address;
        if (new Reader((AtkUnitBase*)args.Addon.Address).Stage == 5)
            if (EzThrottler.Throttle("CloseGame", 500))
                ClickConfirmClose((AddonLotteryDaily*)args.Addon.Address, 5);

        var newState = Enumerable.Range(0, 9).Select(i => addon->GameNumbers[i]).ToArray();
        if (!boardState?.SequenceEqual(newState) ?? true)
        {
            if (!isProcessing && !TaskManager.IsBusy)
            {
                LogVerbose($"[{nameof(MiniCactpot)}] Processing new state, TaskManager.IsBusy: {TaskManager.IsBusy}, isProcessing: {isProcessing}");
                ProcessGameState(addon, newState);
            }
            else
                LogVerbose($"[{nameof(MiniCactpot)}] Skipping state processing - isProcessing: {isProcessing}, TaskManager.IsBusy: {TaskManager.IsBusy}");
        }

        boardState = newState;
    }

    private void ProcessGameState(AddonLotteryDaily* addon, int[] newState)
    {
        isProcessing = true;

        try
        {
            var solution = _solver.Solve(newState);
            var activeIndexes = solution
                .Select((value, index) => new { value, index })
                .Where(item => item.value)
                .Select(item => item.index)
                .ToArray();

            LogDebug($"[{nameof(MiniCactpot)}] Board state: [{string.Join(", ", newState)}], Revealed: {newState.Count(x => x > 0)}, Solution length: {solution.Length}, Active indexes: [{string.Join(", ", activeIndexes)}], Solution: [{string.Join(", ", solution)}]");

            if (solution.Length is 8)
                ExecuteLaneSelection(addon, activeIndexes);
            else
                ExecuteButtonSelection(addon, activeIndexes);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Error processing game state: {ex}");
        }
        finally
        {
            isProcessing = false;
        }
    }

    private void ExecuteLaneSelection(AddonLotteryDaily* addon, int[] activeIndexes)
    {
        if (activeIndexes.First() is { } first)
        {
            LogDebug($"[{nameof(MiniCactpot)}] Clicking lane at index #{SolverLaneToCsLane(first)} [{string.Join(", ", activeIndexes)}]");

            TaskManager.Enqueue(() =>
            {
                if (addon == null) return true;

                var lane = addon->LaneSelector[SolverLaneToCsLane(first)];
                if (lane == null) return true;

                if (EzThrottler.Throttle($"ClickLane_{first}", 100))
                {
                    LogDebug($"[{nameof(MiniCactpot)}] Executing click for lane {first}");
                    lane->ClickRadioButton((AtkUnitBase*)addon);
                }
                else
                    LogDebug($"[{nameof(MiniCactpot)}] Skipping click for lane {first} due to throttling");
                return true;
            }, $"Click lane {first}");

            TaskManager.Enqueue(() =>
            {
                if (EzThrottler.Throttle("ConfirmLane", 300))
                    return ClickConfirmClose(addon, -1);
                else
                    LogDebug($"[{nameof(MiniCactpot)}] Skipping lane confirmation due to throttling");
                return true;
            }, "Confirm lane selection");
        }
    }

    private void ExecuteButtonSelection(AddonLotteryDaily* addon, int[] activeIndexes)
    {
        if (activeIndexes.First() is { } first)
        {
            LogDebug($"[{nameof(MiniCactpot)}] Clicking button at index #{first} [{string.Join(", ", activeIndexes)}]");

            TaskManager.Enqueue(() =>
            {
                if (addon == null) return true;

                if (EzThrottler.Throttle($"ClickButton_{first}", 100))
                {
                    LogDebug($"[{nameof(MiniCactpot)}] Executing click for button #{first}");
                    Callback.Fire((AtkUnitBase*)addon, true, 1, first);
                }
                else
                    LogDebug($"[{nameof(MiniCactpot)}] Skipping click for button #{first} due to throttling");
                return true;
            }, $"Click button {first}");
        }
    }

    private bool ClickConfirmClose(AddonLotteryDaily* addon, int stage)
    {
        if (addon == null) return false;

        var confirm = addon->GetComponentButtonById(67);
        if (confirm == null || !confirm->IsEnabled) return false;

        LogDebug($"[{nameof(MiniCactpot)}] Clicking {(stage == 5 ? "close" : "confirm")}");

        TaskManager.Enqueue(() =>
        {
            if (addon == null) return true;

            var confirmBtn = addon->GetComponentButtonById(67);
            if (confirmBtn == null || !confirmBtn->IsEnabled) return true;

            if (EzThrottler.Throttle("ClickConfirm", 100))
            {
                LogDebug($"[{nameof(MiniCactpot)}] Executing {(stage == 5 ? "close" : "confirm")} click");
                confirmBtn->ClickAddonButton((AtkUnitBase*)addon);
            }
            else
                LogDebug($"[{nameof(MiniCactpot)}] Skipping {(stage == 5 ? "close" : "confirm")} click due to throttling");
            return true;
        }, $"Click {(stage == 5 ? "close" : "confirm")}");

        return true;
    }

    private int SolverLaneToCsLane(int lane)
        => lane switch
        {
            0 => 5,
            1 => 6,
            2 => 7,
            3 => 1,
            4 => 2,
            5 => 3,
            6 => 0,
            7 => 4,
            _ => throw new ArgumentOutOfRangeException($"{nameof(lane)}", lane, "Must be between 0 and 8 (inclusive)"),
        };

    public class Reader(AtkUnitBase* UnitBase, int BeginOffset = 0) : AtkReader(UnitBase, BeginOffset)
    {
        public int Stage => ReadInt(0) ?? -1;
        public string State => ReadString(3);
        public IEnumerable<int> Numbers
        {
            get
            {
                for (var i = 6; i <= 14; i++)
                    yield return ReadInt(i) ?? -1;
            }
        }
    }
}
