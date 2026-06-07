using Dalamud.Game.ClientState.Conditions;
using ECommons.Throttlers;
using Saucy.Framework;
using System;
namespace Saucy;

internal static unsafe class ArcadeMachineSession
{
    private static readonly RunState[] States = [new(), new()];

    public static void WireCompleteShutdown(GoldSaucerArcadeMachine machine, Action callback) =>
        GetState(machine).CompleteShutdown = callback;

    public static void ResetRunState(GoldSaucerArcadeMachine machine)
    {
        var state = GetState(machine);
        state.PendingShutdown = false;
        state.ShutdownArmedUtc = null;
        state.DeclinedStartMenu = false;
        state.PlayingFinalRound = false;
        state.InteractPending = false;
        state.CountedCurrentReward = false;
    }

    public static void OnRewardScreenOpened(GoldSaucerArcadeMachine machine)
    {
        var state = GetState(machine);
        state.PlayingFinalRound = false;
        if (!GoldSaucerArcadeRunSession.PlayXTimes(machine) || state.CountedCurrentReward)
        {
            return;
        }

        state.CountedCurrentReward = true;
        if (GoldSaucerArcadeRunSession.TryCompleteGame(machine))
        {
            RequestShutdown(machine);
        }
    }

    public static void OnRewardScreenClosed(GoldSaucerArcadeMachine machine) =>
        GetState(machine).CountedCurrentReward = false;

    public static bool BlocksRewardHandling(GoldSaucerArcadeMachine machine)
    {
        var state = GetState(machine);
        return state.PendingShutdown ||
               uiReaderGamesResults.HasResultsUI ||
               GoldSaucerRewardHelper.IsVisible();
    }

    public static bool BlocksAnotherStart(GoldSaucerArcadeMachine machine) =>
        BlocksRewardHandling(machine) || GetState(machine).PlayingFinalRound;

    public static void RequestShutdown(GoldSaucerArcadeMachine machine)
    {
        var state = GetState(machine);
        state.PendingShutdown = true;
        state.ShutdownArmedUtc = DateTime.UtcNow;
        state.DeclinedStartMenu = false;
    }

    public static bool TickPendingShutdown(GoldSaucerArcadeMachine machine)
    {
        var state = GetState(machine);
        if (!state.PendingShutdown)
        {
            return false;
        }

        if (uiReaderGamesResults.HasResultsUI || GoldSaucerRewardHelper.IsVisible())
        {
            state.DeclinedStartMenu = false;
            return true;
        }

        if (SelectStringHelper.TryGetArcadeMenu(out var menu) &&
            SelectStringHelper.IsArcadeYesnoMenu(menu))
        {
            if (!state.DeclinedStartMenu &&
                EzThrottler.Throttle(GoldSaucerArcadeMachineHelper.GetDeclineStartThrottleKey(machine), 400) &&
                SelectStringHelper.TrySelectNoEntry(menu))
            {
                state.DeclinedStartMenu = true;
            }

            return true;
        }

        if (!state.DeclinedStartMenu &&
            state.ShutdownArmedUtc != null &&
            DateTime.UtcNow - state.ShutdownArmedUtc.Value < TimeSpan.FromSeconds(3) &&
            Svc.Condition[ConditionFlag.OccupiedInQuestEvent])
        {
            return true;
        }

        state.PendingShutdown = false;
        state.ShutdownArmedUtc = null;
        state.DeclinedStartMenu = false;
        state.CompleteShutdown?.Invoke();
        return true;
    }

    public static void ClearInteractPending(GoldSaucerArcadeMachine machine) =>
        GetState(machine).InteractPending = false;

    public static bool IsInteractPending(GoldSaucerArcadeMachine machine) =>
        GetState(machine).InteractPending;

    public static void SetInteractPending(GoldSaucerArcadeMachine machine, bool value) =>
        GetState(machine).InteractPending = value;

    public static void MarkPlayingFinalRound(GoldSaucerArcadeMachine machine) =>
        GetState(machine).PlayingFinalRound = true;

    public static bool IsPlayingFinalRound(GoldSaucerArcadeMachine machine) =>
        GetState(machine).PlayingFinalRound;

    public static bool IsPendingShutdown(GoldSaucerArcadeMachine machine) =>
        GetState(machine).PendingShutdown;

    private static RunState GetState(GoldSaucerArcadeMachine machine) =>
        machine switch
        {
            GoldSaucerArcadeMachine.Cuff => States[0],
            GoldSaucerArcadeMachine.Limb => States[1],
            var _ => throw new ArgumentOutOfRangeException(nameof(machine))
        };

    private sealed class RunState
    {
        public Action? CompleteShutdown;
        public bool CountedCurrentReward;
        public bool DeclinedStartMenu;
        public bool InteractPending;
        public bool PendingShutdown;
        public bool PlayingFinalRound;
        public DateTime? ShutdownArmedUtc;
    }
}
