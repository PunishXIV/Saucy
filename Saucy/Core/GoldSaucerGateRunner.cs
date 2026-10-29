using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using Saucy.CuffACur;
using Saucy.Framework;
using Saucy.IPC;
using System;
using System.Numerics;
namespace Saucy;

internal static unsafe class GoldSaucerGateRunner
{
    private const int ActivityCutoffSeconds = 120;
    private const int EntryCloseMarginSeconds = 30;
    private const int PlayingSettleSeconds = 5;
    private const int PostConfirmWaitSeconds = 5;
    private const uint SaucerLiftBaseId = 1011044;
    private const uint ChocoboSquareLiftBaseId = 1010489;

    private static readonly Vector3 CuffPosition = new(27.477905f, -5f, -49.841244f);
    private static readonly Vector3 LimbPosition = new(26.67197f, 0.009055138f, 22.626127f);
    private static readonly Vector3 SaucerLiftPosition = new(-82.99288f, 0.044280052f, 28.661215f);
    private static readonly Vector3 ChocoboSquareLiftPosition = new(3.0803523f, 0f, 10.296527f);
    private static readonly Vector3 AirForceOneNpcPosition = new(-55.109875f, 3.2676394f, -64.57151f);
    private static readonly Vector3 EventSquareNpcPosition = new(77.432045f, -5.000001f, -66.563446f);

    private static readonly uint[] EventNpcBaseIds = [1016306, 1031796, 1010476];

    private static Phase phase;
    private static Module.GateType gate;
    private static long handledSlot = -1;
    private static string? ownedModule;
    private static DateTime deadlineUtc;
    private static DateTime lastMoveUtc;
    private static bool ownsPath;
    private static DateTime enteredGateUtc;
    private static DateTime lastConfirmUtc;

    public static bool IsRunning { get; private set; }

    public static string Status => !IsRunning ? Loc.T("Stopped") : phase switch
    {
        Phase.Waiting => Loc.T("Between GATEs"),
        Phase.Preparing => Loc.T("Finishing the current round"),
        Phase.Lift or Phase.Travel or Phase.Joining => Loc.T("Travelling to GATE"),
        Phase.Playing => Loc.T("Playing GATE"),
        Phase.Resume => Loc.T("Returning to between-GATE activity"),
        _ => Loc.T("Running")
    };

    public static bool Start()
    {
        if (IsRunning)
        {
            return true;
        }

        if ((!GateDirector.InSaucer && !GateDirector.InChocoboSquare) || !Vnavmesh.IsInstalled)
        {
            Svc.Chat.Print(Loc.T("[Saucy] Start the GATE runner in the Gold Saucer with vnavmesh installed."));
            return false;
        }

        var activity = C.GoldSaucerGates.BetweenGates;
        if (TriadRunSession.ModuleEnabled ||
            activity != BetweenGatesActivity.OutOnALimb && GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Limb) ||
            activity != BetweenGatesActivity.CuffACur && CuffACurAutomation.IsEnabled)
        {
            Svc.Chat.Print(Loc.T("[Saucy] Stop the other game automation before starting the GATE runner."));
            return false;
        }

        if (!IsSupported(Module.GateType.AirForceOne) &&
            !IsSupported(Module.GateType.SliceIsRight) &&
            !IsSupported(Module.GateType.AnyWayTheWindBlows))
        {
            Svc.Chat.Print(Loc.T("[Saucy] Enable Air Force One or automatic movement for an enabled GATE before starting."));
            return false;
        }

        IsRunning = true;
        phase = Phase.Waiting;
        handledSlot = -1;
        ownedModule = CuffACurAutomation.IsEnabled ? ModuleNames.CuffACur
            : GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Limb) ? ModuleNames.OutOnALimb
            : null;
        Svc.Chat.Print(Loc.T("[Saucy] GATE runner started."));
        return true;
    }

    public static void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        ReleasePath();
        StopActivity();
        Svc.Chat.Print(Loc.T("[Saucy] GATE runner stopped."));
    }

    public static void Tick()
    {
        if (!IsRunning)
        {
            return;
        }

        if (Svc.Condition[ConditionFlag.WaitingForDutyFinder] && phase is not (Phase.Joining or Phase.Playing))
        {
            Stop();
            return;
        }

        if (Svc.Condition[ConditionFlag.BetweenAreas])
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (phase == Phase.Waiting)
        {
            if (!GateDirector.InSaucer && !GateDirector.InChocoboSquare)
            {
                Stop();
                return;
            }

            TickWaiting(now);
            return;
        }

        if (now >= deadlineUtc && phase != Phase.Resume)
        {
            if (phase is Phase.Preparing or Phase.Playing)
            {
                Svc.Chat.Print(Loc.T("[Saucy] GATE runner timed out during a game. Stopping."));
                Stop();
            }
            else
            {
                Svc.Chat.Print(Loc.T("[Saucy] Could not enter this GATE. Waiting for the next one."));
                ReleasePath();
                phase = Phase.Waiting;
            }

            return;
        }

        switch (phase)
        {
            case Phase.Preparing:
                if (IsArcadeMinigameActive() ||
                    GoldSaucerRewardHelper.IsVisible() ||
                    uiReaderGamesResults.HasResultsUI)
                {
                    return;
                }

                StopActivity();
                deadlineUtc = now.AddMinutes(2);
                if (GateDirector.InChocoboSquare)
                {
                    phase = Phase.Lift;
                    return;
                }

                phase = Phase.Travel;
                goto case Phase.Travel;

            case Phase.Lift:
                if (GateDirector.InSaucer)
                {
                    phase = Phase.Travel;
                    deadlineUtc = now.AddMinutes(2);
                    return;
                }

                TakeLift(ChocoboSquareLiftPosition, ChocoboSquareLiftBaseId, now);
                return;

            case Phase.Travel:
                if (!MoveTo(GateNpcPosition(gate), 1.5f))
                {
                    return;
                }

                phase = Phase.Joining;
                deadlineUtc = now.AddSeconds(30);
                return;

            case Phase.Joining:
                if (GateDirector.IsPlayerOnStage() || Svc.Condition[ConditionFlag.BoundByDuty95])
                {
                    phase = Phase.Playing;
                    enteredGateUtc = now;
                    deadlineUtc = now.AddMinutes(13);
                    return;
                }

                if (DutyConfirmHelper.IsVisible() || DutyConfirmHelper.IsQueued())
                {
                    if (now - lastConfirmUtc > TimeSpan.FromSeconds(2) && DutyConfirmHelper.TryCommence())
                    {
                        lastConfirmUtc = now;
                    }

                    deadlineUtc = now.AddSeconds(30);
                    return;
                }

                var eventNpc = FindNearest(EventNpcBaseIds);
                if (eventNpc != null &&
                    (ObjectHelper.HasNamePlateIcon(eventNpc) || TalkHelper.IsVisible() || SelectYesnoHelper.IsVisible()))
                {
                    TalkTo(eventNpc, "Saucy.GateRunner.Join", now);
                }

                return;

            case Phase.Playing:
                if (now - enteredGateUtc > TimeSpan.FromSeconds(PlayingSettleSeconds) && HasLeftGate())
                {
                    phase = Phase.Resume;
                    deadlineUtc = now.AddSeconds(4);
                    ReleasePath();
                    return;
                }

                TalkHelper.TryAdvance("Saucy.GateRunner.Talk");
                return;

            case Phase.Resume:
                if (now < deadlineUtc)
                {
                    return;
                }

                if (!GateDirector.InSaucer)
                {
                    if (now - deadlineUtc > TimeSpan.FromMinutes(2))
                    {
                        Stop();
                    }

                    return;
                }

                phase = Phase.Waiting;
                return;
        }
    }

    private static void TickWaiting(DateTime now)
    {
        var seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var slot = seconds / GoldSaucerGateTimeline.SlotSeconds * GoldSaucerGateTimeline.SlotSeconds;
        var current = GoldSaucerGateTimeline.Predict(slot);
        if (slot != handledSlot && IsSupported(current.Type) &&
            seconds - slot < current.EntrySeconds - EntryCloseMarginSeconds)
        {
            handledSlot = slot;
            gate = current.Type;
            phase = Phase.Preparing;
            deadlineUtc = now.AddMinutes(2);
            ReleasePath();
            Svc.Chat.Print(Loc.T("[Saucy] {0} GATE is starting. Travelling there after the current round.", current.Name.Localized));
            return;
        }

        var next = GoldSaucerGateTimeline.Predict(slot + GoldSaucerGateTimeline.SlotSeconds);
        var nextIsSupported = IsSupported(next.Type);
        var nextIsClose = slot + GoldSaucerGateTimeline.SlotSeconds - seconds <= ActivityCutoffSeconds;
        if (C.GoldSaucerGates.BetweenGates == BetweenGatesActivity.Idle || nextIsSupported && nextIsClose)
        {
            if (ownedModule != null && !IsArcadeMinigameActive())
            {
                StopActivity();
            }

            if (ownedModule != null)
            {
                return;
            }

            if (GateDirector.InChocoboSquare)
            {
                TakeLift(ChocoboSquareLiftPosition, ChocoboSquareLiftBaseId, now);
            }
            else if (nextIsSupported && nextIsClose)
            {
                MoveTo(GateNpcPosition(next.Type), 1.5f);
            }

            return;
        }

        StartActivity(now);
    }

    private static bool HasLeftGate() =>
        gate == Module.GateType.AirForceOne
            ? !Svc.Condition[ConditionFlag.BoundByDuty95]
            : !GateDirector.IsPlayerOnStage();

    private static bool IsSupported(Module.GateType type) => type switch
    {
        Module.GateType.AirForceOne => C.IsModuleEnabled(ModuleNames.AirForceOne),
        Module.GateType.SliceIsRight => C.IsModuleEnabled(ModuleNames.SliceIsRight) && BossMod.IsInstalled &&
                                        C.GoldSaucerGates.SliceIsRightAutoMovement,
        Module.GateType.AnyWayTheWindBlows => C.IsModuleEnabled(ModuleNames.AnyWayTheWindBlows) &&
                                              C.GoldSaucerGates.WindBlowsAutoMovement,
        _ => false
    };

    private static bool IsArcadeMinigameActive() =>
        CuffACurAutomation.IsInActiveMinigame || P.LimbManager.IsInActiveMinigame;

    private static void StartActivity(DateTime now)
    {
        if (ownedModule != null || AutoRetainerPause.IsBlocking)
        {
            return;
        }

        switch (C.GoldSaucerGates.BetweenGates)
        {
            case BetweenGatesActivity.CuffACur:
                if (GateDirector.InChocoboSquare)
                {
                    TakeLift(ChocoboSquareLiftPosition, ChocoboSquareLiftBaseId, now);
                }
                else if (MoveTo(CuffPosition, 1.5f))
                {
                    EnableActivity(ModuleNames.CuffACur);
                }

                return;

            case BetweenGatesActivity.OutOnALimb:
                if (GateDirector.InSaucer)
                {
                    TakeLift(SaucerLiftPosition, SaucerLiftBaseId, now);
                }
                else if (MoveTo(LimbPosition, 1.5f))
                {
                    EnableActivity(ModuleNames.OutOnALimb);
                }

                return;
        }
    }

    private static void EnableActivity(string moduleName)
    {
        C.SetModuleEnabled(moduleName, true);
        ownedModule = moduleName;
    }

    private static void StopActivity()
    {
        if (ownedModule == null)
        {
            return;
        }

        C.SetModuleEnabled(ownedModule, false);
        ownedModule = null;
    }

    private static void TakeLift(Vector3 liftPosition, uint liftBaseId, DateTime now)
    {
        var lift = ObjectHelper.FindNearestByBaseId(liftBaseId);
        if (lift == null)
        {
            MoveTo(liftPosition, 1.5f);
            return;
        }

        TalkTo(lift, "Saucy.GateRunner.Lift", now);
    }

    private static void TalkTo(IGameObject npc, string interactKey, DateTime now)
    {
        if (TryConfirmYesno(npc, now) ||
            TalkHelper.TryAdvance("Saucy.GateRunner.Talk") ||
            TalkHelper.IsVisible() ||
            Svc.Condition[ConditionFlag.OccupiedInQuestEvent] ||
            now - lastConfirmUtc < TimeSpan.FromSeconds(PostConfirmWaitSeconds))
        {
            return;
        }

        if (!ObjectHelper.IsInInteractRange(npc))
        {
            MoveTo(npc.Position, 2f);
            return;
        }

        ReleasePath();
        ObjectHelper.TryInteractWithObject(npc, interactKey);
    }

    private static bool TryConfirmYesno(IGameObject npc, DateTime now)
    {
        if (!SelectYesnoHelper.TryGetVisible(out var yesno) ||
            Svc.Targets.Target?.GameObjectId != npc.GameObjectId ||
            SelectYesnoHelper.IsBlockedSystemPrompt(yesno) ||
            !SelectYesnoHelper.HasYesnoButtons(yesno))
        {
            return false;
        }

        if (now - lastConfirmUtc > TimeSpan.FromSeconds(2) && SelectYesnoHelper.PressYes(yesno))
        {
            lastConfirmUtc = now;
        }

        return true;
    }

    private static bool MoveTo(Vector3 destination, float range)
    {
        if (Vnavmesh.IsWithinHorizontalRange(destination, range + 0.1f))
        {
            ReleasePath();
            return true;
        }

        if (Vnavmesh.IsMoving() || DateTime.UtcNow - lastMoveUtc < TimeSpan.FromSeconds(4))
        {
            return false;
        }

        lastMoveUtc = DateTime.UtcNow;
        ownsPath = Vnavmesh.TryMoveTo(destination, false, range);
        return false;
    }

    private static void ReleasePath()
    {
        if (!ownsPath)
        {
            return;
        }

        Vnavmesh.StopPath();
        ownsPath = false;
    }

    private static Vector3 GateNpcPosition(Module.GateType type) =>
        type == Module.GateType.AirForceOne ? AirForceOneNpcPosition : EventSquareNpcPosition;

    private static IGameObject? FindNearest(uint[] baseIds)
    {
        IGameObject? nearest = null;
        foreach (var baseId in baseIds)
        {
            var obj = ObjectHelper.FindNearestByBaseId(baseId);
            if (obj != null && (nearest == null || Player.DistanceTo(obj) < Player.DistanceTo(nearest)))
            {
                nearest = obj;
            }
        }

        return nearest;
    }

    private enum Phase
    {
        Waiting,
        Preparing,
        Lift,
        Travel,
        Joining,
        Playing,
        Resume
    }
}
