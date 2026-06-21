using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using ECommons.GameHelpers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.CuffACur;
using Saucy.Framework;
using Saucy.IPC;
using System;
using static ECommons.GenericHelpers;
using Callback = ECommons.Automation.Callback;
namespace Saucy;

internal static unsafe class AutoRetainerPause
{
    // Placed summoning bell furnishing (HousingEventObject BaseId 196630).
    private const uint SummoningBellBaseId = 196630;
    private const float BellInteractRange = 6f;
    private const int HandlingTimeoutSeconds = 120;

    private static readonly string[] RetainerAddonsToClose =
    [
        "RetainerTaskResult",
        "RetainerTaskAsk",
        "RetainerSell",
        "RetainerItemTransferProgress",
        "RetainerItemTransferList",
        "SelectYesno",
        "SelectString",
        "RetainerList"
    ];

    private static bool bellInteracted;
    private static bool autoRetainerEnableSent;
    private static bool autoRetainerBusySeen;
    private static bool closingUi;
    private static DateTime handlingStartedUtc;

    public static bool IsHandling { get; private set; }

    public static bool IsBlocking =>
        IsHandling || (C.PauseForAutoRetainer && IsRetainerPauseArmed());

    public static bool HasBellInRange() => FindNearbySummoningBell() != null;

    public static bool BlocksArcadeSessions(GoldSaucerArcadeMachine machine) => IsBlocking;

    public static void Tick()
    {
        if (!C.PauseForAutoRetainer || !AutoRetainerIpc.IsInstalled)
        {
            Reset(forceStopAutoRetainer: IsHandling);
            return;
        }

        if (IsHandling)
        {
            TickHandling();
            return;
        }

        if (!IsRetainerPauseArmed() || !IsArcadeAutomationActive())
        {
            return;
        }

        if (IsBlockingActiveGameplay())
        {
            return;
        }

        BeginHandling();
    }

    private static bool IsRetainerPauseArmed() =>
        HasBellInRange() &&
        AutoRetainerIpc.AreAnyRetainersReady() &&
        !AutoRetainerIpc.IsBusyNow();

    private static void BeginHandling()
    {
        IsHandling = true;
        bellInteracted = false;
        autoRetainerEnableSent = false;
        autoRetainerBusySeen = false;
        closingUi = false;
        handlingStartedUtc = DateTime.UtcNow;

        Svc.Chat.Print("[Saucy] Pausing arcade automation — retainers ready at nearby bell.");
    }

    private static void TickHandling()
    {
        var elapsed = DateTime.UtcNow - handlingStartedUtc;
        if (elapsed.TotalSeconds > HandlingTimeoutSeconds)
        {
            Svc.Chat.Print("[Saucy] AutoRetainer pause timed out; resuming automation.");
            Reset(forceStopAutoRetainer: true);
            return;
        }

        if (!bellInteracted)
        {
            var bell = FindNearbySummoningBell();
            if (bell != null && ObjectHelper.TryInteractWithObject(bell, "Saucy.AutoRetainer.Bell"))
            {
                bellInteracted = true;
            }

            return;
        }

        // Same as AutoDuty: enable AutoRetainer at the bell so users don't need expert OpenBell settings.
        if (!autoRetainerEnableSent &&
            Svc.Condition[ConditionFlag.OccupiedSummoningBell] &&
            AutoRetainerIpc.AreAnyRetainersReady())
        {
            Chat.ExecuteCommand("/autoretainer e");
            autoRetainerEnableSent = true;
            return;
        }

        if (autoRetainerEnableSent && AutoRetainerIpc.IsBusyNow())
        {
            autoRetainerBusySeen = true;
        }

        if (autoRetainerBusySeen && !AutoRetainerIpc.IsBusyNow())
        {
            TickClosingUi();
        }
    }

    private static void TickClosingUi()
    {
        if (!closingUi)
        {
            closingUi = true;
            DisableAutoRetainer();
        }

        Svc.Targets.Target = null;

        if (TryCloseRetainerUiStep())
        {
            return;
        }

        if (!AnyRetainerAddonVisible() &&
            Player.Interactable &&
            !Svc.Condition[ConditionFlag.OccupiedSummoningBell])
        {
            Svc.Chat.Print("[Saucy] AutoRetainer finished; resuming automation.");
            Reset(forceStopAutoRetainer: true);
        }
    }

    private static bool TryCloseRetainerUiStep()
    {
        foreach (var name in RetainerAddonsToClose)
        {
            if (!TryGetAddonByName<AtkUnitBase>(name, out var addon) || !addon->IsVisible)
            {
                continue;
            }

            TryDismissAddon(addon, name);
            return true;
        }

        return false;
    }

    private static bool AnyRetainerAddonVisible()
    {
        foreach (var name in RetainerAddonsToClose)
        {
            if (TryGetAddonByName<AtkUnitBase>(name, out var addon) && addon->IsVisible)
            {
                return true;
            }
        }

        return false;
    }

    private static void TryDismissAddon(AtkUnitBase* addon, string name)
    {
        if (name == "SelectYesno" && TryGetAddonMaster<AddonMaster.SelectYesno>(out var yesno))
        {
            yesno.No();
            return;
        }

        if (addon->IsReady())
        {
            addon->FireCallbackInt(-1);
            return;
        }

        if (IsAddonReady(addon))
        {
            addon->FireCallbackInt(-1);
            return;
        }

        try
        {
            Callback.Fire(addon, true, 0);
            addon->Update(0);
            if (!addon->IsVisible)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[AutoRetainerPause] Callback.Fire failed for {name}");
        }

        try
        {
            addon->Close(true);
            addon->Update(0);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[AutoRetainerPause] Close(true) failed for {name}");
        }
    }

    private static void DisableAutoRetainer()
    {
        if (!autoRetainerEnableSent)
        {
            return;
        }

        if (AutoRetainerIpc.IsBusyNow())
        {
            AutoRetainerIpc.AbortAllTasks();
        }

        Chat.ExecuteCommand("/autoretainer d");
    }

    private static void Reset(bool forceStopAutoRetainer = false)
    {
        if (forceStopAutoRetainer)
        {
            Svc.Targets.Target = null;
            while (TryCloseRetainerUiStep())
            {
                // Best-effort close on reset/timeout.
            }

            DisableAutoRetainer();
        }

        IsHandling = false;
        bellInteracted = false;
        autoRetainerEnableSent = false;
        autoRetainerBusySeen = false;
        closingUi = false;
    }

    private static bool IsArcadeAutomationActive() =>
        CuffACurAutomation.IsEnabled ||
        GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Limb);

    private static bool IsBlockingActiveGameplay() =>
        CuffACurAutomation.IsInActiveMinigame || P.LimbManager.IsInActiveMinigame;

    private static IGameObject? FindNearbySummoningBell()
    {
        IGameObject? nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var obj in Svc.Objects)
        {
            if (!IsSummoningBell(obj))
            {
                continue;
            }

            var distance = Player.DistanceTo(obj);
            if (distance > BellInteractRange || distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearest = obj;
        }

        return nearest;
    }

    private static bool IsSummoningBell(IGameObject obj) =>
        obj.ObjectKind == ObjectKind.HousingEventObject && obj.BaseId == SummoningBellBaseId;
}
