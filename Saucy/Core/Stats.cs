using System;
using System.Collections.Generic;
using System.Linq;
namespace Saucy;

public class Stats
{
    public int AirForceGamesPlayed = 0;

    public int AirForceMGP = 0;
    public int CardsDroppedWithSaucy = 0;

    public Dictionary<uint, int> CardsWon = [];

    public int CuffBruisings = 0;

    public int CuffBrutals = 0;

    public int CuffGamesPlayed = 0;

    public int CuffMGP = 0;

    public int CuffPunishings = 0;

    public int GamesDrawnWithSaucy = 0;

    public int GamesLostWithSaucy = 0;
    public int GamesPlayedWithSaucy = 0;

    public int GamesWonWithSaucy = 0;

    public int LimbGamesPlayed = 0;

    public int LimbMGP = 0;

    public int MGPWon = 0;

    public Dictionary<string, int> NPCsPlayed = [];
}

public static class StatsBonusHelper
{
    public static int ApplyMgpBonus(int numMgp)
    {
        double multiplier = 1;
        var localPlayer = Svc.Objects.LocalPlayer;
        if (localPlayer == null)
        {
            return numMgp;
        }

        var jackpot = localPlayer.StatusList.FirstOrDefault(x => x.StatusId == 902);
        if (jackpot != null)
        {
            multiplier += (double)jackpot.Param / 100;
        }

        if (localPlayer.StatusList.Any(x => x.StatusId == 1079))
        {
            multiplier += 0.15;
        }

        return (int)Math.Round(Math.Ceiling(numMgp * multiplier), 0);
    }
}

internal static class StatsSessionClock
{
    private static DateTime? triadSinceUtc;
    private static DateTime? cuffSinceUtc;
    private static DateTime? limbSinceUtc;
    private static DateTime? airForceSinceUtc;

    public static void MarkTriadActive() => Mark(ref triadSinceUtc);

    public static void MarkCuffActive() => Mark(ref cuffSinceUtc);

    public static void MarkLimbActive() => Mark(ref limbSinceUtc);

    public static void MarkAirForceActive() => Mark(ref airForceSinceUtc);

    public static void ResetAll()
    {
        triadSinceUtc = null;
        cuffSinceUtc = null;
        limbSinceUtc = null;
        airForceSinceUtc = null;
    }

    public static double GetTriadElapsedHours() => GetElapsedHours(triadSinceUtc);

    public static double GetCuffElapsedHours() => GetElapsedHours(cuffSinceUtc);

    public static double GetLimbElapsedHours() => GetElapsedHours(limbSinceUtc);

    public static double GetAirForceElapsedHours() => GetElapsedHours(airForceSinceUtc);

    private static void Mark(ref DateTime? sinceUtc) => sinceUtc ??= DateTime.UtcNow;

    private static double GetElapsedHours(DateTime? sinceUtc)
    {
        var start = sinceUtc ?? C.SessionStartTime;
        if (start == default || start.Year < 2020 || start > DateTime.UtcNow)
        {
            start = DateTime.UtcNow;
        }

        return Math.Max((DateTime.UtcNow - start).TotalHours, 1.0 / 60.0);
    }
}
