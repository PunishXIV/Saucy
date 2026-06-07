namespace Saucy.TripleTriad;

internal static class TriadTargetNpc
{
    public static TriadNpc? FromWorldTarget()
    {
        var name = Svc.Targets.Target?.Name.TextValue;
        return TriadNpcDB.Get().FindMatchingName(name);
    }

    public static TriadNpc? FromSolverContext() =>
        TriadRun.preGameNpc ?? TriadRun.currentNpc ?? TriadRun.lastGameNpc;

    public static TriadNpc? FromRunContext(GameNpcInfo? runTargetNpc)
    {
        if (runTargetNpc != null)
        {
            return TriadNpcDB.Get().FindByID(runTargetNpc.npcId);
        }

        return FromSolverContext();
    }
}
