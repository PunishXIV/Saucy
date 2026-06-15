using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
using System;
using System.Linq;
using static ECommons.GenericHelpers;
namespace Saucy.OutOnALimb;

public unsafe partial class LimbManager
{
    private const int LimbYesnoThrottleMs = 800;
    private const string StartMenuThrottleKey = "Saucy.OutOnALimb.StartMenu";

    private static bool HasLimbMinigameUi() =>
        (TryGetAddonByName<AtkUnitBase>("MiniGameAimg", out var aimg) && IsAddonReady(aimg)) ||
        (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var botanist) && IsAddonReady(botanist));

    public bool IsInActiveMinigame => HasLimbMinigameUi();

    private static bool HasLimbSessionUi() =>
        HasLimbMinigameUi() ||
        ObjectHelper.HasInitiatedArcadeMenu(ArcadeMachineScopes.Limb) ||
        (ArcadeMachineGate.IsInFlow(ArcadeMachineScopes.Limb) && ArcadeMachineGate.HasVisibleArcadeStartMenu());

    private void RunLimbMinigame()
    {
        ArcadeMachineGate.RefreshFlow(
            ArcadeMachineScopes.Limb,
            ArcadeMachineGate.IsInFlow(ArcadeMachineScopes.Limb),
            () => ArcadeMachineGate.MarkFlow(ArcadeMachineScopes.Limb),
            HasLimbMinigameUi);

        if (SelectStringHelper.TryGetArcadeMenu(out var startMenu) &&
            SelectStringHelper.IsArcadeYesnoMenu(startMenu))
        {
            if (TryConfirmLimbStart(startMenu))
            {
                return;
            }
        }
        else if (TryConfirmLimbStart())
        {
            return;
        }

        {
            if (TryGetAddonByName<AtkUnitBase>("MiniGameAimg", out var addon) && IsAddonReady(addon))
            {
                var reference = addon->GetNodeById(NodeIDs[Cfg.LimbDifficulty]);
                var cursor = addon->GetNodeById(39);
                var iCursor = 400 - cursor->Height;
                if (iCursor > reference->Y && iCursor < reference->Y + Heights[Cfg.LimbDifficulty])
                {
                    SafeClickButtonAimg();
                }
            }
        }
        HandleYesno();
        {
            if (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon))
            {
                var reader = new ReaderMiniGameBotanist(addon);
                var hitPending = GetHitPending(addon);

                if (PendingCursor != null && OldHitPending && !hitPending)
                {
                    PluginLog.Debug("Out on a limb - hit result event");
                    Record(addon);
                    Next = GetNextTargetCursorPos();
                }

                if (reader.State == 3)
                {
                    if (OldState != 3)
                    {
                        PreviousHealth = GetHealth(addon);
                        if (reader.SwingsLeft == 10)
                        {
                            PluginLog.Debug("Out on a limb - GAME RESET");
                            Reset();
                            PreviousHealth = GetHealth(addon);
                        }

                        PluginLog.Debug("Out on a limb - turn start event");
                        Next = GetNextTargetCursorPos();
                    }

                    if (OnlyRequest)
                    {
                        if (Request != null)
                        {
                            AtkStage.Instance()->GetNumberArrayData(NumberArrayType.GoldSaucerArcadeMachine)->IntArray[0] = Math.Clamp(Request.Value * 100 + Random.Shared.Next(200) - 100, 1, 9999);
                            if (SafeClickButtonBotanist(Request.Value))
                            {
                                Request = null;
                            }
                        }
                    }
                    else
                    {
                        if (Next != null)
                        {
                            AtkStage.Instance()->GetNumberArrayData(NumberArrayType.GoldSaucerArcadeMachine)->IntArray[0] = Math.Clamp(Next.Value * 100 + Random.Shared.Next(200) - 100, 1, 9999);
                            if (SafeClickButtonBotanist(Next.Value))
                            {
                                Next = null;
                            }
                        }
                    }
                }
                else
                {
                    if (OldState == 3)
                    {
                        PluginLog.Debug("Out on a limb - turn finish event");
                    }
                }

                OldState = reader.State;
                OldHitPending = hitPending;
            }
        }
    }

    private static bool TryConfirmLimbStart(AddonSelectString* startMenu = null) =>
        ArcadeMachineGate.TryConfirmStartMenu(Machine, StartMenuThrottleKey, menu: startMenu);

    private static bool CanAutomateLimbYesno() =>
        ArcadeMachineGate.CanAutomateArcadeYesno(ArcadeMachineScopes.Limb) ||
        (HasLimbMinigameUi() && ArcadeMachineGate.IsInFlow(ArcadeMachineScopes.Limb));

    private void HandleYesno()
    {
        if (!CanAutomateLimbYesno() ||
            !SelectYesnoHelper.TryGetVisible(out var ss) ||
            !SelectYesnoHelper.IsArcadeYesno(ss))
        {
            return;
        }

        if (!EzThrottler.Throttle("Limb.Yesno", LimbYesnoThrottleMs))
        {
            return;
        }

        if (!SelectYesnoHelper.IsArcadeDoubleDownYesno(ss))
        {
            if (Exit)
            {
                SelectYesnoHelper.PressNo(ss);
                Exit = false;
                ArcadeMachineSession.RequestShutdown(Machine);
                return;
            }

            PluginLog.Information("[OutOnALimb] Arcade yesno visible but rejected (not detected as double-down).");
            return;
        }

        PluginLog.Information("[OutOnALimb] Double-down yesno detected; evaluating timer.");

        if (ArcadeMachineSession.IsPendingShutdown(Machine))
        {
            SelectYesnoHelper.PressNo(ss);
            return;
        }

        if (Exit)
        {
            SelectYesnoHelper.PressNo(ss);
            Exit = false;
            ArcadeMachineSession.RequestShutdown(Machine);
            return;
        }

        if (GoldSaucerArcadeRunSession.IsStopForDutyFinder(Machine))
        {
            SelectYesnoHelper.PressNo(ss);
            return;
        }

        var rawTimer = LimbArcadeTimer.TryGetSecondsRemaining();
        var secondsRemaining = rawTimer ?? 0;
        var continueRound = secondsRemaining > Cfg.MinSecondsForAnotherRound;

        PluginLog.Information(
            $"[OutOnALimb] DoubleDown decision: raw={rawTimer?.ToString() ?? "null"}, " +
            $"secondsRemaining={secondsRemaining}, threshold={Cfg.MinSecondsForAnotherRound} -> " +
            $"{(continueRound ? "PressYes" : "PressNo")}");

        if (continueRound)
        {
            SelectYesnoHelper.PressYes(ss);
        }
        else
        {
            SelectYesnoHelper.PressNo(ss);
        }
    }

    private static uint GetHealth(AtkUnitBase* addon) => AddonMiniGameBotanist.From(addon)->Health;

    private static bool GetHitPending(AtkUnitBase* addon) => AddonMiniGameBotanist.From(addon)->HitPending;

    private HitPower GetHitPower(uint previousHealth, uint health)
    {
        if (health == 0)
        {
            return HitPower.Maximum;
        }

        if (previousHealth < health)
        {
            return HitPower.Unobserved;
        }

        return (previousHealth - health) switch
        {
            0 => HitPower.Nothing,
            WeakHit => HitPower.Weak,
            StrongHit => HitPower.Strong,
            var _ => HitPower.Unobserved
        };
    }

    private int GetNextTargetCursorPos()
    {
        for (var i = MinIndex; i < Results.Count; i++)
        {
            var current = Results[i];
            if (current.Power == HitPower.Strong)
            {
                return current.Position;
            }
        }

        for (var i = MinIndex; i < Results.Count; i++)
        {
            var current = Results[i];
            var prev = Results.SafeSelect(i - 1);
            var next = Results.SafeSelect(i + 1);
            if (current.Power == HitPower.Weak)
            {
                if (prev?.Power == HitPower.Unobserved && i - 1 >= MinIndex)
                {
                    return prev.Position;
                }

                if (next?.Power == HitPower.Unobserved)
                {
                    return next.Position;
                }
            }
        }

        foreach (var x in StartingPoints)
        {
            int[] adjustedPoints = [.. StartingPoints.Where(z => !IsStartingPointChecked(z))];
            if (adjustedPoints.Length == 0)
            {
                break;
            }

            var transformedPoints = adjustedPoints.Select(z => GetClosestResultPoint(z)
                    .Position)
                .ToArray();
            var index = 0;
            PluginLog.Debug($"Returning starting point {adjustedPoints[index]}->{transformedPoints[index]}");
            if (StartingPoints.Length != transformedPoints.Length)
            {
                RecordMinIndex = true;
            }

            return transformedPoints[index];
        }

        MinIndex = 0;
        var unobserveds = Results.Where(x => x.Power == HitPower.Unobserved)
            .ToArray();
        if (unobserveds.Length == 0)
        {
            PluginLog.Error("No more results");
            return -100;
        }

        var res = unobserveds[Random.Shared.Next(unobserveds.Length)].Position;
        PluginLog.Debug($"Returning random unobserved point {res}");
        return res;
    }

    private HitResult GetClosestResultPoint(int point) => Results.OrderBy(x => Math.Abs(point - x.Position))
        .First();

    private bool IsStartingPointChecked(int position)
    {
        var item = GetClosestResultPoint(position);
        return item.Power != HitPower.Unobserved;
    }

    private void Record(AtkUnitBase* addon)
    {
        if (PendingCursor == null || PreviousHealth == null)
        {
            return;
        }

        var health = GetHealth(addon);
        var result = GetHitPower(PreviousHealth.Value, health);
        if (result != HitPower.Unobserved)
        {
            Record(result, PendingCursor.Value);
        }

        PendingCursor = null;
        PreviousHealth = null;
    }

    private void Record(HitPower result, int cursor)
    {
        if (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon))
        {
            var item = Results.OrderBy(x => Math.Abs(x.Position - cursor))
                .First();
            if (RecordMinIndex)
            {
                RecordMinIndex = false;
                MinIndex = Results.IndexOf(item);
            }

            if (result < item.Power)
            {
                MinIndex = 0;
                RecordMinIndex = false;
            }

            item.Power = result;
            PluginLog.Debug($"{result}");
        }
    }
}
