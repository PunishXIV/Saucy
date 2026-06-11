using System;
namespace Saucy;

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
