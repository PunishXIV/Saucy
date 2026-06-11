using Saucy.CuffACur;
using Saucy.OutOnALimb;
using System;
namespace Saucy;

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
