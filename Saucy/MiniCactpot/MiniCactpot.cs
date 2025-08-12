using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.Automation;
using ECommons.Automation.UIInput;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
using Saucy.OutOnALimb.ECEmbedded;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Saucy.MiniCactpot;
public unsafe class MiniCactpot : Module
{
    public override string Name => "Mini Cactpot";

    private readonly CactpotSolver _solver = new();
    private int[]? boardState;
    private Task? gameTask;

    public override void Enable() => Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "LotteryDaily", OnUpdate);
    public override void Disable() => Svc.AddonLifecycle.UnregisterListener(OnUpdate);

    private void OnUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonLotteryDaily*)args.Addon.Address;
        if (new Reader((AtkUnitBase*)args.Addon.Address).Stage == 5) ClickConfirmClose((AddonLotteryDaily*)args.Addon.Address, 5);
        var newState = Enumerable.Range(0, 9).Select(i => addon->GameNumbers[i]).ToArray();
        if (!boardState?.SequenceEqual(newState) ?? true)
        {
            try
            {
                if (gameTask is null or { Status: TaskStatus.RanToCompletion or TaskStatus.Faulted or TaskStatus.Canceled })
                {
                    gameTask = Task.Run(() =>
                    {
                        var solution = _solver.Solve(newState);
                        var activeIndexes = solution
                            .Select((value, index) => new { value, index })
                            .Where(item => item.value)
                            .Select(item => item.index)
                            .ToArray();

                        PluginLog.Debug($"[{nameof(MiniCactpot)}] Board state: [{string.Join(", ", newState)}], Revealed: {newState.Count(x => x > 0)}, Solution length: {solution.Length}, Active indexes: [{string.Join(", ", activeIndexes)}], Solution: [{string.Join(", ", solution)}]");

                        if (solution.Length is 8)
                            ClickLanes(addon, activeIndexes);
                        else
                            ClickButtons(addon, activeIndexes);
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                PluginLog.Error("Updater has crashed");
            }
        }

        boardState = newState;
    }

    private void ClickLanes(AddonLotteryDaily* addon, int[] activeIndexes)
    {
        if (activeIndexes.First() is { } first)
        {
            PluginLog.Debug($"[{nameof(MiniCactpot)}] Clicking lane at index #{SolverLaneToCsLane(first)} [{string.Join(", ", activeIndexes)}]");
            ExecuteTask(() =>
            {
                if (addon != null)
                {
                    var lane = addon->LaneSelector[SolverLaneToCsLane(first)];
                    if (lane != null)
                        lane->ClickRadioButton((AtkUnitBase*)addon);
                    else
                        TaskManager.Abort();
                }
                else
                    TaskManager.Abort();
            });
        }
        ClickConfirmClose(addon, -1);
    }

    private void ClickButtons(AddonLotteryDaily* addon, int[] activeIndexes)
    {
        if (activeIndexes.First() is { } first)
        {
            PluginLog.Debug($"[{nameof(MiniCactpot)}] Clicking button at index #{first} [{string.Join(", ", activeIndexes)}]");
            ExecuteTask(() =>
            {
                if (addon != null)
                    Callback.Fire((AtkUnitBase*)addon, true, 1, first);
                else
                    TaskManager.Abort();
            });
        }
    }

    private void ClickConfirmClose(AddonLotteryDaily* addon, int stage)
    {
        var confirm = addon->GetComponentButtonById(67);
        if (confirm->IsEnabled)
        {
            PluginLog.Debug($"[{nameof(MiniCactpot)}] Clicking {(stage == 5 ? "close" : "confirm")}");
            ExecuteTask(() =>
            {
                if (confirm != null)
                    confirm->ClickAddonButton((AtkUnitBase*)addon);
                else
                    TaskManager.Abort();
            });
        }
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
