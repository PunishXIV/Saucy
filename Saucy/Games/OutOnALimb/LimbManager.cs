using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation.UIInput;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
using System;
using System.Collections.Generic;
using static ECommons.GenericHelpers;

namespace Saucy.OutOnALimb;

public unsafe partial class LimbManager
{
    private const GoldSaucerArcadeMachine Machine = GoldSaucerArcadeMachine.Limb;
    private const string MachineSanityThrottleKey = "Saucy.OutOnALimb.MachineSanityCheck";
    private const int AimgHitThrottleMs = 500;
    private const uint WeakHit = 100;
    private const uint StrongHit = 400;

    private const uint GoldSaucerTerritoryId = 144;
    private static readonly int[] StartingPoints = [20, 50, 80];

    private static readonly Dictionary<LimbDifficulty, int> Heights = new()
    {
        [LimbDifficulty.Titan] = 20, [LimbDifficulty.Morbol] = 40, [LimbDifficulty.Cactuar] = 340
    };

    private static readonly Dictionary<LimbDifficulty, uint> NodeIDs = new()
    {
        [LimbDifficulty.Titan] = 41, [LimbDifficulty.Morbol] = 44, [LimbDifficulty.Cactuar] = 47
    };
    private readonly List<HitResult> Results = [];
    public LimbConfig Cfg;

    private bool Exit;
    private int MinIndex;
    private int? Next;
    private bool OldHitPending;
    private uint OldState;
    private bool OnlyRequest;
    private int? PendingCursor;
    private uint? PreviousHealth;
    private bool RecordMinIndex;
    private int? Request;
    private int RequestInput;

    public LimbManager(LimbConfig conf) => Cfg = conf;

    private void ToggleModule(bool enabled)
    {
        if (enabled)
        {
            GoldSaucerArcadeMachineHelper.DisableConflictingModules(Machine);
        }

        C.SetModuleEnabled(ModuleNames.OutOnALimb, enabled);
    }

    private void DisableModule()
    {
        GoldSaucerArcadeRunSession.ClearStopForDutyFinder(Machine);
        GoldSaucerArcadeFakeBreak.Clear(Machine);
        C.SetModuleEnabled(ModuleNames.OutOnALimb, false);
        C.Save();
    }

    private static void RegisterTrackedMachines() =>
        ObjectHelper.SetTrackedObjects(
            ArcadeMachineScopes.Limb,
            ArcadeMachineBaseIds.Limb,
            logLabel: ModuleNames.OutOnALimb);

    internal static void ResetSession(bool unregisterMachines = true)
    {
        ArcadeMachineSession.ResetRunState(Machine);
        ArcadeMachineGate.ClearFlow(ArcadeMachineScopes.Limb);
        if (unregisterMachines)
        {
            ObjectHelper.ClearTrackedObjects(ArcadeMachineScopes.Limb);
        }

        EzThrottler.Throttle("Saucy.OutOnALimb.Interact", 0, true);
        EzThrottler.Throttle("Saucy.OutOnALimb.Interact.Target", 0, true);
        EzThrottler.Throttle("Saucy.OutOnALimb.StartMenu", 0, true);
        EzThrottler.Throttle(MachineSanityThrottleKey, 0, true);
        EzThrottler.Throttle(GoldSaucerArcadeMachineHelper.GetDeclineStartThrottleKey(Machine), 0, true);
    }

    internal static void PrepareSession()
    {
        ResetSession(unregisterMachines: false);
        RegisterTrackedMachines();
        if (FindNearestLimbMachine() != null ||
            ObjectHelper.IsNearRememberedObject(
                ArcadeMachineScopes.Limb,
                ObjectHelper.GetHorizontalEdgeDistance))
        {
            ArcadeMachineGate.MarkFlow(ArcadeMachineScopes.Limb);
        }
    }

    private static IGameObject? FindNearestLimbMachine() =>
        ObjectHelper.FindNearest(
            ArcadeMachineScopes.Limb,
            ObjectHelper.GetHorizontalEdgeDistance,
            static obj => obj.BaseId == ArcadeMachineBaseIds.Limb[0] ? 2.5f : 4f);

    private static bool ShouldSkipMachineSanityCheck()
    {
        if (!Svc.ClientState.IsLoggedIn || !Player.Available)
        {
            return true;
        }

        if (Svc.Condition[ConditionFlag.BetweenAreas])
        {
            return true;
        }

        return false;
    }

    private void TryDisableForMissingMachine()
    {
        var throttleMs = Svc.ClientState.TerritoryType == GoldSaucerTerritoryId ? 5000 : 1000;
        if (!EzThrottler.Throttle(MachineSanityThrottleKey, throttleMs))
        {
            return;
        }

        DuoLog.Warning("No Out on a Limb machine nearby (maybe get closer if in front of one).");
        DisableModule();
    }

    private void InteractWithLimbMachine(IGameObject limb)
    {
        ObjectHelper.RememberObject(ArcadeMachineScopes.Limb, limb);

        if (!ObjectHelper.TryInteractWithObject(limb, "Saucy.OutOnALimb.Interact"))
        {
            return;
        }

        ArcadeMachineSession.SetInteractPending(Machine, true);
        ArcadeMachineGate.MarkFlow(ArcadeMachineScopes.Limb);
    }

    private void Reset()
    {
        Results.Clear();
        for (var i = 0; i <= 100; i += Cfg.Step)
        {
            Results.Add(new(i, HitPower.Unobserved));
        }

        Next = null;
        MinIndex = 0;
        OldHitPending = false;
        RecordMinIndex = false;
        PendingCursor = null;
        PreviousHealth = null;
    }

    private int GetCursor()
    {
        const float Min = -0.733f;
        const float Max = 0.733f;
        if (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon))
        {
            var cursorFloat = addon->GetNodeById(17)->Rotation;
            cursorFloat -= Min;
            cursorFloat /= Max - Min;
            cursorFloat *= 100;
            return (int)Math.Round(cursorFloat);
        }

        return 0;
    }

    private bool SafeClickButtonAimg()
    {
        var ret = false;
        if (TryGetAddonByName<AtkUnitBase>("MiniGameAimg", out var addon) && IsAddonReady(addon))
        {
            var button = addon->GetComponentButtonById(37);
            if (button->IsEnabled)
            {
                if (EzThrottler.Throttle("ClickAimgGameButton"))
                {
                    ret = true;
                    button->ClickAddonButton(addon);
                }
            }
        }

        return ret;
    }

    private bool SafeClickButtonBotanist(int cursor)
    {
        var ret = false;
        if (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon))
        {
            var reader = new ReaderMiniGameBotanist(addon);
            var button = addon->GetComponentButtonById(24);
            if (button->IsEnabled && reader.State == 3)
            {
                if (EzThrottler.Throttle("ClickBtnGameButton", 2000))
                {
                    PendingCursor = cursor;
                    PreviousHealth = GetHealth(addon);
                    ret = true;
                    button->ClickAddonButton(addon);
                }
            }
        }

        return ret;
    }

    public void RunModule()
    {
        try
        {
            if (!GoldSaucerArcadeMachineHelper.IsEnabled(Machine))
            {
                return;
            }

            if (!Player.Available)
            {
                return;
            }

            if (!IsScreenReady())
            {
                return;
            }

            if (ArcadeMachineSession.TickPendingShutdown(Machine))
            {
                return;
            }

            if (Svc.Condition[ConditionFlag.OccupiedInQuestEvent])
            {
                ArcadeMachineGate.TryReclaimSession(
                    ArcadeMachineScopes.Limb,
                    HasLimbSessionUi,
                    () => FindNearestLimbMachine() != null);

                if (HasLimbSessionUi())
                {
                    RunLimbMinigame();
                    return;
                }

                if (ArcadeMachineGate.IsUnrelatedQuestOccupancy(
                    ArcadeMachineScopes.Limb,
                    HasLimbSessionUi,
                    () => FindNearestLimbMachine() != null))
                {
                    return;
                }
            }

            if (ArcadeMachineSession.BlocksRewardHandling(Machine) ||
                ArcadeMachineSession.IsPlayingFinalRound(Machine))
            {
                return;
            }

            if (!GoldSaucerArcadeRunSession.ShouldContinue(Machine))
            {
                if (GoldSaucerArcadeRunSession.PlayXTimes(Machine))
                {
                    ArcadeMachineSession.RequestShutdown(Machine);
                }

                return;
            }

            if (ShouldSkipMachineSanityCheck())
            {
                return;
            }

            if (GoldSaucerArcadeFakeBreak.IsActive(Machine))
            {
                return;
            }

            var limb = FindNearestLimbMachine();
            if (limb == null)
            {
                TryDisableForMissingMachine();
                return;
            }

            if (ArcadeMachineSession.IsInteractPending(Machine))
            {
                return;
            }

            InteractWithLimbMachine(limb);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[LimbManager] RunModule failed");
        }
    }
}
