using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using ECommons.ImGuiMethods;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
using System;
namespace Saucy.CuffACur;

public unsafe partial class CuffACurAutomation
{
    public delegate nint UnknownFunction(nint a1, ushort a2, int a3, void* a4);
    private const GoldSaucerArcadeMachine Machine = GoldSaucerArcadeMachine.Cuff;
    private const string StartMenuThrottleKey = "Saucy.CuffACur.StartMenu";

    public static Hook<UnknownFunction>? FuncHook;

    public static bool IsEnabled => C.IsModuleEnabled(ModuleNames.CuffACur);

    public static nint FuncDetour(nint a1, ushort a2, int a3, void* a4) => FuncHook!.Original(a1, a2, a3, a4);

    private static void RegisterTrackedMachines() =>
        ObjectHelper.SetTrackedObjects(
            ArcadeMachineScopes.Cuff,
            ArcadeMachineBaseIds.Cuff,
            logLabel: ModuleNames.CuffACur);

    public static void ResetSession(bool unregisterMachines = true)
    {
        ArcadeMachineSession.ResetRunState(Machine);
        ArcadeMachineGate.ClearFlow(ArcadeMachineScopes.Cuff);
        if (unregisterMachines)
        {
            ObjectHelper.ClearTrackedObjects(ArcadeMachineScopes.Cuff);
        }

        EzThrottler.Throttle("Saucy.CuffACur.Interact", 0, true);
        EzThrottler.Throttle("Saucy.CuffACur.Interact.Target", 0, true);
        EzThrottler.Throttle(StartMenuThrottleKey, 0, true);
        EzThrottler.Throttle(GoldSaucerArcadeMachineHelper.GetDeclineStartThrottleKey(Machine), 0, true);
    }

    public static void PrepareSession()
    {
        ResetSession(unregisterMachines: false);
        RegisterTrackedMachines();
        if (FindNearestCuffMachine() != null)
        {
            ArcadeMachineGate.MarkFlow(ArcadeMachineScopes.Cuff);
        }
    }

    public static void RunModule()
    {
        var addon = Svc.GameGui.GetAddonByName("PunchingMachine");
        try
        {
            if (ArcadeMachineSession.TickPendingShutdown(Machine))
            {
                return;
            }

            if (Svc.Condition[ConditionFlag.OccupiedInQuestEvent])
            {
                ArcadeMachineGate.TryReclaimSession(
                    ArcadeMachineScopes.Cuff,
                    () => HasCuffMinigameUi(addon),
                    () => FindNearestCuffMachine() != null);

                if (ObjectHelper.HasInitiatedArcadeMenu(ArcadeMachineScopes.Cuff) ||
                    HasCuffMinigameUi(addon) ||
                    (ArcadeMachineGate.IsInFlow(ArcadeMachineScopes.Cuff) &&
                     ArcadeMachineGate.HasVisibleArcadeStartMenu()))
                {
                    if (SelectStringHelper.TryGetArcadeMenu(out var cuffMenu) &&
                        SelectStringHelper.IsArcadeYesnoMenu(cuffMenu))
                    {
                        TryConfirmCuffStart(cuffMenu);
                        return;
                    }

                    if (TryConfirmCuffStart())
                    {
                        return;
                    }

                    if (addon != nint.Zero)
                    {
                        var ui = (AtkUnitBase*)addon.Address;

                        if (ui->IsVisible && ui->UldManager.NodeListCount > 18)
                        {
                            var slidingNode = ui->UldManager.NodeList[18];
                            var btn = ui->UldManager.NodeList[10];
                            if (slidingNode->Width is >= 210 and <= 240)
                            {
                                var evt = stackalloc AtkEvent[]
                                {
                                    new()
                                    {
                                        Node = btn, Target = (AtkEventTarget*)btn, Param = 0, NextEvent = null
                                    }
                                };

                                FuncHook ??= Svc.Hook.HookFromAddress<UnknownFunction>(Svc.SigScanner.ScanText("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 0F B7 FA"), FuncDetour);
                                FuncHook.Original(addon, 0x17, 0, evt);
                            }
                        }
                    }

                    return;
                }

                if (ArcadeMachineGate.IsUnrelatedQuestOccupancy(
                    ArcadeMachineScopes.Cuff,
                    () => HasCuffMinigameUi(addon),
                    () => FindNearestCuffMachine() != null))
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

            if (GoldSaucerArcadeFakeBreak.IsActive(Machine))
            {
                ArcadeMachineSession.ClearInteractPending(Machine);
                return;
            }

            var cuff = FindNearestCuffMachine();
            if (cuff == null)
            {
                DuoLog.Warning("No Cuff-a-Cur machine nearby (maybe get closer if in front of one).");
                DisableModule();
                return;
            }

            if (ArcadeMachineSession.IsInteractPending(Machine))
            {
                return;
            }

            if (!ObjectHelper.TryInteractWithObject(cuff, "Saucy.CuffACur.Interact"))
            {
                return;
            }

            ArcadeMachineSession.SetInteractPending(Machine, true);
            ArcadeMachineGate.MarkFlow(ArcadeMachineScopes.Cuff);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[CuffACurAutomation] RunModule failed");
        }
    }

    private static bool TryConfirmCuffStart(AddonSelectString* cuffMenu = null) =>
        ArcadeMachineGate.TryConfirmStartMenu(Machine, StartMenuThrottleKey, menu: cuffMenu);

    private static void DisableModule()
    {
        GoldSaucerArcadeRunSession.ClearStopForDutyFinder(Machine);
        GoldSaucerArcadeFakeBreak.Clear(Machine);
        C.SetModuleEnabled(ModuleNames.CuffACur, false);
        C.Save();
    }

    private static IGameObject? FindNearestCuffMachine() =>
        ObjectHelper.FindNearest(
            ArcadeMachineScopes.Cuff,
            ObjectHelper.GetHorizontalEdgeDistance,
            static obj => obj.BaseId == ArcadeMachineBaseIds.Cuff[0] ? 1f : 4f);

    private static bool HasCuffMinigameUi(nint punchingMachineAddon)
    {
        if (punchingMachineAddon != nint.Zero)
        {
            var ui = (AtkUnitBase*)punchingMachineAddon;
            if (ui->IsVisible)
            {
                return true;
            }
        }

        return ObjectHelper.HasInitiatedArcadeMenu(ArcadeMachineScopes.Cuff);
    }

    public static void DrawDebug()
    {
        ImGuiEx.Text($"Module enabled: {IsEnabled}");
        ImGuiEx.Text($"Play X: {GoldSaucerArcadeRunSession.PlayXTimes(Machine)}");
        ImGuiEx.Text($"Pending shutdown: {ArcadeMachineSession.IsPendingShutdown(Machine)}");
        ImGuiEx.Text($"Playing final round: {ArcadeMachineSession.IsPlayingFinalRound(Machine)}");
        ImGuiEx.Text($"Remaining: {GoldSaucerArcadeRunSession.GetRemaining(Machine)}");
        ImGuiEx.Text($"Interact pending: {ArcadeMachineSession.IsInteractPending(Machine)}");
        ImGuiEx.Text($"In quest event: {Svc.Condition[ConditionFlag.OccupiedInQuestEvent]}");
        ImGuiEx.Text($"Results UI: {uiReaderGamesResults.HasResultsUI}");

        if (SelectStringHelper.TryGetArcadeMenu(out var startMenu))
        {
            ImGuiEx.Text($"SelectString arcade yes/no={SelectStringHelper.IsArcadeYesnoMenu(startMenu)}");
        }
        else if (SelectStringHelper.TryGetVisibleSelectString(out var otherMenu))
        {
            ImGuiEx.Text("SelectString visible (not arcade agent)");
        }

        var machine = FindNearestCuffMachine();
        if (machine != null)
        {
            ImGuiEx.Text($"Nearest machine: {machine.Name.TextValue} ({machine.BaseId}) dist={ObjectHelper.GetHorizontalEdgeDistance(machine):F2}");
            ImGuiEx.Text($"Targeting cuff machine: {ObjectHelper.IsTargeting(ArcadeMachineScopes.Cuff)}");
            ImGuiEx.Text($"Cuff flow active: {ArcadeMachineGate.IsInFlow(ArcadeMachineScopes.Cuff)}");
        }
        else
        {
            ImGuiEx.Text("Nearest machine: none");
        }

        var addon = Svc.GameGui.GetAddonByName("PunchingMachine");
        if (addon == nint.Zero)
        {
            ImGuiEx.Text("PunchingMachine: not loaded");
            return;
        }

        var ui = (AtkUnitBase*)addon.Address;
        ImGuiEx.Text($"PunchingMachine visible: {ui->IsVisible}, nodes: {ui->UldManager.NodeListCount}");
        if (ui->IsVisible && ui->UldManager.NodeListCount > 18)
        {
            var slidingNode = ui->UldManager.NodeList[18];
            var inPunchWindow = slidingNode->Width is >= 210 and <= 240;
            ImGuiEx.Text($"Sliding node width: {slidingNode->Width:F1} (punch window: {inPunchWindow})");
        }
    }
}
