using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using Saucy.CuffACur;
using Saucy.Framework;
using Saucy.IPC;
using System;
namespace Saucy;

internal static class AutoRetainerPause
{
    private const float BellInteractRange = 6f;
    private const int HandlingTimeoutSeconds = 120;

    private static bool bellInteracted;
    private static bool autoRetainerBusySeen;
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
            Reset();
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
        autoRetainerBusySeen = false;
        handlingStartedUtc = DateTime.UtcNow;

        Svc.Chat.Print("[Saucy] Pausing arcade automation — retainers ready at nearby bell.");
    }

    private static void TickHandling()
    {
        var elapsed = DateTime.UtcNow - handlingStartedUtc;
        if (elapsed.TotalSeconds > HandlingTimeoutSeconds)
        {
            Svc.Chat.Print("[Saucy] AutoRetainer pause timed out; resuming automation.");
            Reset();
            return;
        }

        if (!bellInteracted)
        {
            var bell = FindNearbySummoningBell();
            if (bell != null && ObjectHelper.TryInteractWithObject(bell, "Saucy.AutoRetainer.Bell"))
            {
                bellInteracted = true;
            }
        }

        if (AutoRetainerIpc.IsBusyNow())
        {
            autoRetainerBusySeen = true;
        }

        if (autoRetainerBusySeen &&
            !AutoRetainerIpc.IsBusyNow() &&
            Player.Interactable &&
            !IsPlayerOccupiedInRetainerFlow())
        {
            Svc.Chat.Print("[Saucy] AutoRetainer finished; resuming automation.");
            Reset();
            return;
        }

        if (!autoRetainerBusySeen && !AutoRetainerIpc.AreAnyRetainersReady())
        {
            Reset();
        }
    }

    private static void Reset()
    {
        IsHandling = false;
        bellInteracted = false;
        autoRetainerBusySeen = false;
    }

    private static bool IsArcadeAutomationActive() =>
        CuffACurAutomation.IsEnabled ||
        GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Limb);

    private static bool IsBlockingActiveGameplay() =>
        CuffACurAutomation.IsInActiveMinigame || P.LimbManager.IsInActiveMinigame;

    private static bool IsPlayerOccupiedInRetainerFlow() =>
        Svc.Condition[ConditionFlag.OccupiedInEvent] ||
        Svc.Condition[ConditionFlag.OccupiedInQuestEvent] ||
        Svc.Condition[ConditionFlag.OccupiedSummoningBell];

    private static IGameObject? FindNearbySummoningBell()
    {
        IGameObject? nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var obj in Svc.Objects)
        {
            if (obj.ObjectKind != ObjectKind.EventObj)
            {
                continue;
            }

            var name = obj.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name) ||
                !name.Contains("Summoning Bell", StringComparison.OrdinalIgnoreCase))
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
}
