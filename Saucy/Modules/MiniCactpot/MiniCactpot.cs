using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.Automation.UIInput;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
using System;
using static ECommons.GenericHelpers;
namespace Saucy.MiniCactpot;

public unsafe class MiniCactpot : Module
{
    // Shared throttle key + delay between any auto-click — feels less frantic and matches a human pace.
    private const string ClickThrottleKey = "Saucy.MiniCactpot.Click";
    private const int ClickThrottleMs = 600;

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
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", OnTicketPromptSetup);
    }

    public override void Disable()
    {
        Svc.AddonLifecycle.UnregisterListener(OnPostUpdate);
        Svc.AddonLifecycle.UnregisterListener(OnPreFinalize);
        Svc.AddonLifecycle.UnregisterListener(OnTicketPromptSetup);
        TaskManager.Abort();
        boardState = null;
        pendingState = null;
        isProcessing = false;
    }

    private void OnTicketPromptSetup(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null || !addon->IsVisible)
        {
            return;
        }

        var prompt = ReadYesnoPromptText(addon);
        // Game text varies by client locale and current MGP, so match on a stable substring.
        if (string.IsNullOrEmpty(prompt) || !prompt.Contains("purchase a ticket", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!EzThrottler.Throttle("MiniCactpotTicketYes", 500))
        {
            return;
        }

        try
        {
            new AddonMaster.SelectYesno(addon).Yes();
        }
        catch (Exception ex)
        {
            LogVerbose($"Ticket Yes click failed: {ex.Message}");
        }
    }

    private static string? ReadYesnoPromptText(AtkUnitBase* addon)
    {
        // Standard SelectYesno layout puts the prompt at NodeList[15].
        if (addon->UldManager.NodeListCount <= 15)
        {
            return null;
        }

        var node = addon->UldManager.NodeList[15];
        if (node == null)
        {
            return null;
        }

        var textNode = node->GetAsAtkTextNode();
        if (textNode == null)
        {
            return null;
        }

        return textNode->NodeText.GetText();
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

        var stage = GetStage(addon);

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
                if (lane == null || lane->AtkComponentButton.AtkResNode == null)
                {
                    return true;
                }

                if (EzThrottler.Throttle(ClickThrottleKey, ClickThrottleMs))
                {
                    try
                    {
                        lane->ClickRadioButton((AtkUnitBase*)addon);
                    }
                    catch (Exception ex)
                    {
                        LogVerbose($"Lane {first} click failed: {ex.Message}");
                    }

                    return true;
                }

                return false;
            }, $"Click lane {first}");

        TaskManager.Enqueue(() =>
        {
            if (!EzThrottler.Throttle(ClickThrottleKey, ClickThrottleMs))
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

                var tile = addon->GameBoard[first];
                if (tile == null)
                {
                    return true;
                }

                var button = (AtkComponentButton*)tile;
                // ClickAddonButton dereferences AtkResNode internally; a tile pointer can be non-null while the underlying node hasn't been initialized yet, which crashed the old call.
                if (button->AtkResNode == null || !button->AtkResNode->IsVisible())
                {
                    return false;
                }

                if (EzThrottler.Throttle(ClickThrottleKey, ClickThrottleMs))
                {
                    try
                    {
                        button->ClickAddonButton((AtkUnitBase*)addon);
                    }
                    catch (Exception ex)
                    {
                        LogVerbose($"Tile {first} click failed: {ex.Message}");
                    }

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

        var confirmBtn = addon->GetComponentButtonById(67);
        if (confirmBtn == null)
        {
            return true;
        }

        if (!confirmBtn->IsEnabled)
        {
            return false;
        }

        if (EzThrottler.Throttle(ClickThrottleKey, ClickThrottleMs))
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
}
