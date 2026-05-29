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
using static ECommons.GenericHelpers;
namespace Saucy.MiniCactpot;

public unsafe class MiniCactpot : Module
{
    private readonly int[] scratchState = new int[9];
    private readonly CactpotSolver solver = new();
    private int[]? boardState;
    private bool isProcessing;
    private int[]? pendingState;
    public override string Name => "Mini Cactpot";

    public override void Enable()
    {
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "LotteryDaily", OnPostUpdate);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "LotteryDaily", OnPreFinalize);
    }

    public override void Disable()
    {
        Svc.AddonLifecycle.UnregisterListener(OnPostUpdate);
        Svc.AddonLifecycle.UnregisterListener(OnPreFinalize);
        TaskManager.Abort();
        boardState = null;
        pendingState = null;
        isProcessing = false;
    }

    private void OnPreFinalize(AddonEvent type, AddonArgs args)
    {
        TaskManager.Abort();
        boardState = null;
        pendingState = null;
        isProcessing = false;
    }

    private void OnPostUpdate(AddonEvent type, AddonArgs args)
    {
        if (!TryGetLotteryAddon(out var addon))
        {
            return;
        }

        var stage = new Reader((AtkUnitBase*)addon).Stage;

        if (stage == 5 && EzThrottler.Throttle("CloseGame") && !TaskManager.IsBusy)
        {
            TaskManager.Enqueue(() => TryClickConfirmButton(expectedStage: 5), "Click close");
        }

        ReadBoardState(addon, scratchState);

        if (!StatesEqual(boardState, scratchState))
        {
            if (!isProcessing && !TaskManager.IsBusy)
            {
                ProcessAndCommit(addon, scratchState);
            }
            else
            {
                pendingState = (int[])scratchState.Clone();
            }
        }
        else if (pendingState is not null && !isProcessing && !TaskManager.IsBusy)
        {
            if (!StatesEqual(boardState, pendingState))
            {
                ProcessAndCommit(addon, pendingState);
            }

            pendingState = null;
        }
    }

    private static bool TryGetLotteryAddon(out AddonLotteryDaily* addon)
    {
        if (TryGetAddonByName("LotteryDaily", out addon) && IsAddonReady((AtkUnitBase*)addon))
        {
            return true;
        }

        addon = null;
        return false;
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

    private void ProcessAndCommit(AddonLotteryDaily* addon, int[] state)
    {
        ProcessGameState(addon, state);
        boardState = (int[])state.Clone();
    }

    private void ProcessGameState(AddonLotteryDaily* addon, int[] newState)
    {
        isProcessing = true;

        try
        {
            if (CountRevealed(newState) == 0)
            {
                return;
            }

            var solution = solver.Solve(newState);
            var activeIndexes = CollectActiveIndexes(solution);

            if (solution.Length is 8)
            {
                ExecuteLaneSelection(activeIndexes);
            }
            else
            {
                ExecuteButtonSelection(activeIndexes);
            }
        }
        catch (Exception ex)
        {
            LogError($"Error processing game state: {ex}");
        }
        finally
        {
            isProcessing = false;
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

    private void ExecuteLaneSelection(int[] activeIndexes)
    {
        if (activeIndexes.Length == 0)
        {
            return;
        }

        var first = activeIndexes[0];
        var csLane = SolverLaneToCsLane(first);

        TaskManager.Enqueue(() =>
            {
                if (!TryGetLotteryAddon(out var addon))
                {
                    return true;
                }

                var lane = addon->LaneSelector[csLane];
                if (lane == null)
                {
                    return true;
                }

                if (EzThrottler.Throttle($"ClickLane_{first}", 100))
                {
                    lane->ClickRadioButton((AtkUnitBase*)addon);
                    return true;
                }

                return false;
            }, $"Click lane {first}");

        TaskManager.Enqueue(() =>
        {
            if (!EzThrottler.Throttle("ConfirmLane", 300))
            {
                return false;
            }

            return TryClickConfirmButton(expectedStage: -1);
        }, "Confirm lane selection");
    }

    private void ExecuteButtonSelection(int[] activeIndexes)
    {
        if (activeIndexes.Length == 0)
        {
            return;
        }

        var first = activeIndexes[0];

        TaskManager.Enqueue(() =>
            {
                if (!TryGetLotteryAddon(out var addon))
                {
                    return true;
                }

                if (EzThrottler.Throttle($"ClickButton_{first}", 100))
                {
                    Callback.Fire((AtkUnitBase*)addon, true, 1, first);
                    return true;
                }

                return false;
            }, $"Click button {first}");
    }

    private static bool TryClickConfirmButton(int expectedStage)
    {
        if (!TryGetLotteryAddon(out var addon))
        {
            return true;
        }

        var stage = new Reader((AtkUnitBase*)addon).Stage;
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

        var confirmBtn = addon->GetComponentButtonById(67);
        if (confirmBtn == null)
        {
            return true;
        }

        if (!confirmBtn->IsEnabled)
        {
            return false;
        }

        if (EzThrottler.Throttle("ClickConfirm", 100))
        {
            confirmBtn->ClickAddonButton((AtkUnitBase*)addon);
            return true;
        }

        return false;
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
