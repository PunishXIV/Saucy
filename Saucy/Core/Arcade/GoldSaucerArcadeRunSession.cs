using Saucy.CuffACur;
using Saucy.OutOnALimb;
using System;
namespace Saucy;

internal enum GoldSaucerArcadeMachine
{
    Cuff,
    Limb
}

[Serializable]
public class GoldSaucerArcadeRunSettings
{
    public bool PlayXTimes { get; set; }

    public int MatchCount { get; set; } = 1;

    public bool EnableFakeBreak { get; set; }

    public int FakeBreakPlayMinutes { get; set; } = 60;

    public int FakeBreakMinutes { get; set; } = 5;
}

internal static class GoldSaucerArcadeLifecycle
{
    public static void OnModuleEnabled(GoldSaucerArcadeMachine machine)
    {
        switch (machine)
        {
            case GoldSaucerArcadeMachine.Cuff:
                CuffACurAutomation.PrepareSession();
                break;
            case GoldSaucerArcadeMachine.Limb:
                LimbManager.PrepareSession();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(machine));
        }

        GoldSaucerArcadeRunSession.BeginSession(machine);
    }

    public static void OnModuleDisabled(GoldSaucerArcadeMachine machine)
    {
        switch (machine)
        {
            case GoldSaucerArcadeMachine.Cuff:
                CuffACurAutomation.ResetSession();
                break;
            case GoldSaucerArcadeMachine.Limb:
                LimbManager.ResetSession();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(machine));
        }
    }
}

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

internal static class GoldSaucerArcadeFakeBreak
{
    private static readonly DateTime?[] PlayWindowStartUtc = new DateTime?[2];
    private static readonly DateTime?[] BreakEndsUtc = new DateTime?[2];

    public static void ResetPlayWindow(GoldSaucerArcadeMachine machine)
    {
        var index = ToIndex(machine);
        PlayWindowStartUtc[index] = DateTime.UtcNow;
        BreakEndsUtc[index] = null;
    }

    public static void Clear(GoldSaucerArcadeMachine machine)
    {
        var index = ToIndex(machine);
        PlayWindowStartUtc[index] = null;
        BreakEndsUtc[index] = null;
    }

    public static bool IsActive(GoldSaucerArcadeMachine machine)
    {
        var settings = GoldSaucerArcadeRunSession.GetSettings(machine);
        if (!settings.EnableFakeBreak)
        {
            return false;
        }

        var index = ToIndex(machine);
        if (PlayWindowStartUtc[index] == null)
        {
            ResetPlayWindow(machine);
            return false;
        }

        var breakEnd = BreakEndsUtc[index];
        if (breakEnd != null)
        {
            if (DateTime.UtcNow < breakEnd)
            {
                return true;
            }

            BreakEndsUtc[index] = null;
            PlayWindowStartUtc[index] = DateTime.UtcNow;
        }

        var playMinutes = Math.Max(1, settings.FakeBreakPlayMinutes);
        if ((DateTime.UtcNow - PlayWindowStartUtc[index]!.Value).TotalMinutes < playMinutes)
        {
            return false;
        }

        var breakMinutes = Math.Max(1, settings.FakeBreakMinutes);
        BreakEndsUtc[index] = DateTime.UtcNow.AddMinutes(breakMinutes);
        return true;
    }

    public static bool TryGetStatusLine(GoldSaucerArcadeMachine machine, out string line)
    {
        line = string.Empty;
        var settings = GoldSaucerArcadeRunSession.GetSettings(machine);
        if (!settings.EnableFakeBreak)
        {
            return false;
        }

        var index = ToIndex(machine);
        var breakEnd = BreakEndsUtc[index];
        if (breakEnd == null || DateTime.UtcNow >= breakEnd)
        {
            return false;
        }

        var remaining = breakEnd.Value - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        line = $"On break — {remaining.Minutes:D2}:{remaining.Seconds:D2} remaining";
        return true;
    }

    private static int ToIndex(GoldSaucerArcadeMachine machine) =>
        machine switch
        {
            GoldSaucerArcadeMachine.Cuff => 0,
            GoldSaucerArcadeMachine.Limb => 1,
            var _ => throw new ArgumentOutOfRangeException(nameof(machine))
        };
}
