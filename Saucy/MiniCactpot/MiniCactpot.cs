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
using System.Linq;
namespace Saucy.MiniCactpot;

public unsafe class MiniCactpot : Module
{
    private readonly CactpotSolver solver = new();
    private readonly int[] scratchState = new int[9];
    private int[]? boardState;
    private int[]? pendingState;
    private bool isProcessing;
    private bool loggedDeferred;
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
        pendingState = null;
        loggedDeferred = false;
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

        ReadBoardState(addon, scratchState);

        if (!StatesEqual(boardState, scratchState))
        {
            var previousState = boardState is null ? "null" : $"[{string.Join(", ", boardState)}]";
            LogStep("Step 3", $"Board state changed (stage={stage}): {previousState} -> [{string.Join(", ", scratchState)}]");

            if (!isProcessing && !TaskManager.IsBusy)
            {
                LogStep("Step 4", $"Processing new state (isProcessing={isProcessing}, TaskManager.IsBusy={TaskManager.IsBusy})");
                ProcessAndCommit(addon, scratchState, stage);
                loggedDeferred = false;
            }
            else
            {
                pendingState = (int[])scratchState.Clone();
                if (!loggedDeferred)
                {
                    LogStep("Step 4", $"Deferred processing while busy (isProcessing={isProcessing}, TaskManager.IsBusy={TaskManager.IsBusy})");
                    loggedDeferred = true;
                }
            }
        }
        else if (pendingState is not null && !isProcessing && !TaskManager.IsBusy)
        {
            if (!StatesEqual(boardState, pendingState))
            {
                LogStep("Step 4", "Processing deferred board state");
                ProcessAndCommit(addon, pendingState, stage);
            }

            pendingState = null;
            loggedDeferred = false;
        }
    }

    private static void ReadBoardState(AddonLotteryDaily* addon, int[] dest)
    {
        for (var i = 0; i < 9; i++)
        {
            dest[i] = addon->GameNumbers[i];
        }
    }

    private static bool StatesEqual(int[]? a, int[] b)
    {
        if (a is null)
        {
            return false;
        }

        for (var i = 0; i < 9; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private void ProcessAndCommit(AddonLotteryDaily* addon, int[] state, int stage)
    {
        ProcessGameState(addon, state, stage);
        boardState = (int[])state.Clone();
    }

    private void ProcessGameState(AddonLotteryDaily* addon, int[] newState, int stage)
    {
        isProcessing = true;
        LogStep("Step 5", $"ProcessGameState started (stage={stage}, revealed={CountRevealed(newState)})");

        try
        {
            LogStep("Step 6", "Running solver");
            if (CountRevealed(newState) == 0)
            {
                LogStep("Step 6", "Skipping solver - no tiles revealed yet");
                return;
            }

            var solution = solver.Solve(newState);
            var activeIndexes = CollectActiveIndexes(solution);

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
                    return true;
                }

                LogStep("Step 12", $"Lane click throttled for solverLane={first}, csLane={csLane}");
                return false;
            }, $"Click lane {first}");

        TaskManager.Enqueue(() =>
        {
            if (!EzThrottler.Throttle("ConfirmLane", 300))
            {
                LogStep("Step 13", "ConfirmLane throttled, retrying confirm");
                return false;
            }

            LogStep("Step 13", "ConfirmLane throttle passed, enqueuing confirm click");
            return ClickConfirmClose(addon, -1);
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
                    return true;
                }

                LogStep("Step 12", $"Tile click throttled for index={first}");
                return false;
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
                    LogStep("Step 15", $"{action} click task waiting - confirm button (id 67) is disabled");
                    return false;
                }

                if (EzThrottler.Throttle("ClickConfirm", 100))
                {
                    LogStep("Step 15", $"Executing {action} click");
                    confirmBtn->ClickAddonButton((AtkUnitBase*)addon);
                    return true;
                }

                LogStep("Step 15", $"{action} click throttled");
                return false;
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
    }
}
