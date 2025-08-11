using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.Automation;
using ECommons.Automation.UIInput;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.OutOnALimb.ECEmbedded;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Saucy.MiniCactpot;
public unsafe class MiniCactpotManager : IDisposable
{
    private readonly CactpotSolver _solver = new();
    private int[]? boardState;
    private Task? gameTask;

    public MiniCactpotManager() => Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "LotteryDaily", OnUpdate);

    public void Dispose() => Svc.AddonLifecycle.UnregisterListener(OnUpdate);

    private void OnUpdate(AddonEvent type, AddonArgs args)
    {
        if (!Saucy.Config.EnableAutoMiniCactpot) return;
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

                        // Check if we're in the final stage (4 numbers revealed) where we pick lanes
                        var numRevealed = newState.Count(x => x > 0);
                        PluginLog.Debug($"[{nameof(MiniCactpotManager)}] Board state: [{string.Join(", ", newState)}], Revealed: {numRevealed}, Solution length: {solution.Length}, Active indexes: [{string.Join(", ", activeIndexes)}], Solution: [{string.Join(", ", solution)}]");

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
            PluginLog.Debug($"[{nameof(MiniCactpotManager)}] Clicking lane at index #{first} [{string.Join(", ", activeIndexes)}]");
            addon->LaneSelector[first]->ClickRadioButton((AtkUnitBase*)addon);
        }
        ClickConfirmClose(addon, -1);
    }

    private void ClickButtons(AddonLotteryDaily* addon, int[] activeIndexes)
    {
        if (activeIndexes.First() is { } first)
        {
            PluginLog.Debug($"[{nameof(MiniCactpotManager)}] Clicking button at index #{first} [{string.Join(", ", activeIndexes)}]");
            Callback.Fire((AtkUnitBase*)addon, true, 1, first);
        }
    }

    private void ClickConfirmClose(AddonLotteryDaily* addon, int stage)
    {
        var confirm = addon->GetComponentButtonById(67);
        if (confirm->IsEnabled)
        {
            PluginLog.Debug($"[{nameof(MiniCactpotManager)}] Clicking {(stage == 5 ? "close" : "confirm")}");
            confirm->ClickAddonButton((AtkUnitBase*)addon);
        }
    }

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
