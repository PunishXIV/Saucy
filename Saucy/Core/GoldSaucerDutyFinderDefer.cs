using Dalamud.Game.ClientState.Conditions;
namespace Saucy;

internal static class GoldSaucerDutyFinderDefer
{
    public static bool IsWaiting
        => Svc.Condition[ConditionFlag.WaitingForDutyFinder];

    public static void Tick()
    {
        if (!IsWaiting)
        {
            return;
        }

        foreach (var machine in GoldSaucerArcadeMachineHelper.All)
        {
            if (GoldSaucerArcadeMachineHelper.IsEnabled(machine))
            {
                GoldSaucerArcadeRunSession.ArmStopForDutyFinder(machine);
            }
        }

        TriadRunSession.ApplyDutyFinderDeferIfNeeded();
    }
}
