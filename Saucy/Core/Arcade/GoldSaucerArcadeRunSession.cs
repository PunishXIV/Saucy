using System;
namespace Saucy;

internal static class GoldSaucerArcadeRunSession
{
    private static readonly MachineRunState[] States = [new(), new()];

    public static GoldSaucerArcadeRunSettings GetSettings(GoldSaucerArcadeMachine machine) =>
        machine switch
        {
            GoldSaucerArcadeMachine.Cuff => C.CuffArcadeRun,
            GoldSaucerArcadeMachine.Limb => C.LimbArcadeRun,
            var _ => throw new ArgumentOutOfRangeException(nameof(machine))
        };

    public static bool PlayXTimes(GoldSaucerArcadeMachine machine) => GetSettings(machine).PlayXTimes;

    public static void BeginSession(GoldSaucerArcadeMachine machine)
    {
        GoldSaucerArcadeFakeBreak.ResetPlayWindow(machine);
        SyncSessionCount(machine);
    }

    public static void SyncSessionCount(GoldSaucerArcadeMachine machine)
    {
        if (!PlayXTimes(machine))
        {
            SetRemaining(machine, 0);
            return;
        }

        SetRemaining(machine, Math.Max(1, GetSettings(machine).MatchCount));
    }

    public static void ArmStopForDutyFinder(GoldSaucerArcadeMachine machine)
    {
        SetRemaining(machine, 0);
        SetStopForDutyFinder(machine, true);
    }

    public static void ClearStopForDutyFinder(GoldSaucerArcadeMachine machine) =>
        SetStopForDutyFinder(machine, false);

    public static bool IsStopForDutyFinder(GoldSaucerArcadeMachine machine) =>
        GetState(machine).StopForDutyFinder;

    public static bool ShouldContinue(GoldSaucerArcadeMachine machine)
    {
        if (GetState(machine).StopForDutyFinder)
        {
            return false;
        }

        if (!PlayXTimes(machine))
        {
            return true;
        }

        return GetRemaining(machine) > 0;
    }

    public static bool TryCompleteGame(GoldSaucerArcadeMachine machine)
    {
        if (!PlayXTimes(machine))
        {
            return false;
        }

        var remaining = GetRemaining(machine) - 1;
        SetRemaining(machine, remaining);
        return remaining <= 0;
    }

    public static int GetRemaining(GoldSaucerArcadeMachine machine) =>
        GetState(machine).Remaining;

    private static void SetRemaining(GoldSaucerArcadeMachine machine, int value) =>
        GetState(machine).Remaining = value;

    private static void SetStopForDutyFinder(GoldSaucerArcadeMachine machine, bool value) =>
        GetState(machine).StopForDutyFinder = value;

    private static MachineRunState GetState(GoldSaucerArcadeMachine machine) =>
        machine switch
        {
            GoldSaucerArcadeMachine.Cuff => States[0],
            GoldSaucerArcadeMachine.Limb => States[1],
            var _ => throw new ArgumentOutOfRangeException(nameof(machine))
        };

    private sealed class MachineRunState
    {
        public int Remaining;
        public bool StopForDutyFinder;
    }
}
