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
    private readonly CactpotSolver solver = new();
    private int[]? boardState;
    private bool isProcessing;
    public override string Name => "Mini Cactpot";

    public override void Enable()
    {
        Log("Step 0: Registering LotteryDaily PostUpdate listener");
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "LotteryDaily", OnUpdate);
    }

    public override void Disable()
    {
        Log("Step 0: Unregistering LotteryDaily PostUpdate listener");
        Svc.AddonLifecycle.UnregisterListener(OnUpdate);
        boardState = null;
    }

    private void LogStep(string step, string message) => Log($"{step}: {message}");

    private void OnUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonLotteryDaily*)args.Addon.Address;
        var stage = new Reader((AtkUnitBase*)args.Addon.Address).Stage;

        if (stage == 5)
        {
            LogStep("Step 1", $"Game finished (stage 5), attempting close (CloseGame throttle: {!EzThrottler.Check("CloseGame")})");
            if (EzThrottler.Throttle("CloseGame"))
            {
                LogStep("Step 2", "CloseGame throttle passed, enqueuing close click");
                ClickConfirmClose((AddonLotteryDaily*)args.Addon.Address, 5);
            }
            else
            {
                LogStep("Step 2", "CloseGame throttled, skipping close click this tick");
            }
        }

        var newState = Enumerable.Range(0, 9).Select(i => addon->GameNumbers[i]).ToArray();
        if (!boardState?.SequenceEqual(newState) ?? true)
        {
            var previousState = boardState is null ? "null" : $"[{string.Join(", ", boardState)}]";
            LogStep("Step 3", $"Board state changed (stage={stage}): {previousState} -> [{string.Join(", ", newState)}]");

            if (!isProcessing && !TaskManager.IsBusy)
            {
                LogStep("Step 4", $"Processing new state (isProcessing={isProcessing}, TaskManager.IsBusy={TaskManager.IsBusy})");
                ProcessGameState(addon, newState, stage);
                boardState = newState;
            }
            else
            {
                LogStep("Step 4", $"Deferred processing while busy (isProcessing={isProcessing}, TaskManager.IsBusy={TaskManager.IsBusy})");
            }
        }
    }

    private void ProcessGameState(AddonLotteryDaily* addon, int[] newState, int stage)
    {
        isProcessing = true;
        LogStep("Step 5", $"ProcessGameState started (stage={stage}, revealed={newState.Count(x => x > 0)})");

        try
        {
            LogStep("Step 6", "Running solver");
            var solution = solver.Solve(newState);
            var activeIndexes = solution
                .Select((value, index) => new
                {
                    value, index
                })
                .Where(item => item.value)
                .Select(item => item.index)
                .ToArray();

            LogStep("Step 7", $"Solver result: solutionLength={solution.Length}, activeIndexes=[{string.Join(", ", activeIndexes)}], solution=[{string.Join(", ", solution)}]");

            if (solution.Length is 8)
            {
                LogStep("Step 8", "Selecting lane (4 tiles revealed)");
                ExecuteLaneSelection(addon, activeIndexes);
            }
            else
            {
                LogStep("Step 8", "Selecting tile");
                ExecuteButtonSelection(addon, activeIndexes);
            }
        }
        catch (Exception ex)
        {
            LogError($"Step 9: Error processing game state: {ex}");
        }
        finally
        {
            isProcessing = false;
            LogStep("Step 10", "ProcessGameState finished");
        }
    }

    private void ExecuteLaneSelection(AddonLotteryDaily* addon, int[] activeIndexes)
    {
        if (activeIndexes.Length == 0)
        {
            LogWarning("Step 11: No active lane index from solver, nothing to click");
            return;
        }

        var first = activeIndexes[0];
        var csLane = SolverLaneToCsLane(first);
        LogStep("Step 11", $"Enqueuing lane click: solverLane={first}, csLane={csLane}, activeIndexes=[{string.Join(", ", activeIndexes)}]");

        TaskManager.Enqueue(() =>
            {
                if (addon == null)
                {
                    LogWarning("Step 12: Lane click task aborted - addon is null");
                    return true;
                }

                var lane = addon->LaneSelector[csLane];
                if (lane == null)
                {
                    LogWarning($"Step 12: Lane click task aborted - LaneSelector[{csLane}] is null");
                    return true;
                }

                if (EzThrottler.Throttle($"ClickLane_{first}", 100))
                {
                    LogStep("Step 12", $"Executing lane click for solverLane={first}, csLane={csLane}");
                    lane->ClickRadioButton((AtkUnitBase*)addon);
                }
                else
                {
                    LogStep("Step 12", $"Lane click throttled for solverLane={first}, csLane={csLane}");
                }
                return true;
            }, $"Click lane {first}");

        TaskManager.Enqueue(() =>
        {
            if (EzThrottler.Throttle("ConfirmLane", 300))
            {
                LogStep("Step 13", "ConfirmLane throttle passed, enqueuing confirm click");
                return ClickConfirmClose(addon, -1);
            }

            LogStep("Step 13", "ConfirmLane throttled, skipping confirm this task tick");
            return true;
        }, "Confirm lane selection");
    }

    private void ExecuteButtonSelection(AddonLotteryDaily* addon, int[] activeIndexes)
    {
        if (activeIndexes.Length == 0)
        {
            LogWarning("Step 11: No active tile index from solver, nothing to click");
            return;
        }

        var first = activeIndexes[0];
        LogStep("Step 11", $"Enqueuing tile click: index={first}, activeIndexes=[{string.Join(", ", activeIndexes)}]");

        TaskManager.Enqueue(() =>
            {
                if (addon == null)
                {
                    LogWarning("Step 12: Tile click task aborted - addon is null");
                    return true;
                }

                if (EzThrottler.Throttle($"ClickButton_{first}", 100))
                {
                    LogStep("Step 12", $"Executing tile click via Callback.Fire for index={first}");
                    Callback.Fire((AtkUnitBase*)addon, true, 1, first);
                }
                else
                {
                    LogStep("Step 12", $"Tile click throttled for index={first}");
                }
                return true;
            }, $"Click button {first}");
    }

    private bool ClickConfirmClose(AddonLotteryDaily* addon, int stage)
    {
        var action = stage == 5 ? "close" : "confirm";

        if (addon == null)
        {
            LogWarning($"Step 14: {action} click aborted - addon is null");
            return false;
        }

        var confirm = addon->GetComponentButtonById(67);
        if (confirm == null)
        {
            LogWarning($"Step 14: {action} click aborted - confirm button (id 67) is null");
            return false;
        }

        if (!confirm->IsEnabled)
        {
            LogStep("Step 14", $"{action} click deferred - confirm button (id 67) is disabled");
            return false;
        }

        LogStep("Step 14", $"Enqueuing {action} click (stage={stage})");

        TaskManager.Enqueue(() =>
            {
                var confirmBtn = addon->GetComponentButtonById(67);
                if (confirmBtn == null)
                {
                    LogWarning($"Step 15: {action} click task aborted - confirm button (id 67) is null");
                    return true;
                }

                if (!confirmBtn->IsEnabled)
                {
                    LogStep("Step 15", $"{action} click task skipped - confirm button (id 67) is disabled");
                    return true;
                }

                if (EzThrottler.Throttle("ClickConfirm", 100))
                {
                    LogStep("Step 15", $"Executing {action} click");
                    confirmBtn->ClickAddonButton((AtkUnitBase*)addon);
                }
                else
                {
                    LogStep("Step 15", $"{action} click throttled");
                }
                return true;
            }, $"Click {action}");

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
            var _ => throw new ArgumentOutOfRangeException($"{nameof(lane)}", lane, "Must be between 0 and 8 (inclusive)")
        };

    public class Reader(AtkUnitBase* unitBase, int beginOffset = 0) : AtkReader(unitBase, beginOffset)
    {
        public int Stage => ReadInt(0) ?? -1;
        public string State => ReadString(3)!;
        public IEnumerable<int> Numbers
        {
            get
            {
                for (var i = 6; i <= 14; i++)
                {
                    yield return ReadInt(i) ?? -1;
                }
            }
        }
    }
}
