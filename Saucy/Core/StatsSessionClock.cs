using System;
namespace Saucy;

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
