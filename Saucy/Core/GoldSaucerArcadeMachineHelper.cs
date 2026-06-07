using Saucy.Framework;
using System;
namespace Saucy;

internal static class GoldSaucerArcadeMachineHelper
{
    internal static readonly GoldSaucerArcadeMachine[] All = [GoldSaucerArcadeMachine.Cuff, GoldSaucerArcadeMachine.Limb];

    public static bool IsEnabled(GoldSaucerArcadeMachine machine) =>
        machine switch
        {
            GoldSaucerArcadeMachine.Cuff => C.IsModuleEnabled(ModuleNames.CuffACur),
            GoldSaucerArcadeMachine.Limb => C.IsModuleEnabled(ModuleNames.OutOnALimb),
            var _ => false
        };

    public static bool AnyEnabled()
    {
        foreach (var machine in All)
        {
            if (IsEnabled(machine))
            {
                return true;
            }
        }

        return false;
    }

    public static string GetModuleName(GoldSaucerArcadeMachine machine) =>
        machine switch
        {
            GoldSaucerArcadeMachine.Cuff => ModuleNames.CuffACur,
            GoldSaucerArcadeMachine.Limb => ModuleNames.OutOnALimb,
            var _ => throw new ArgumentOutOfRangeException(nameof(machine))
        };

    public static string GetScope(GoldSaucerArcadeMachine machine) =>
        machine switch
        {
            GoldSaucerArcadeMachine.Cuff => ArcadeMachineScopes.Cuff,
            GoldSaucerArcadeMachine.Limb => ArcadeMachineScopes.Limb,
            var _ => throw new ArgumentOutOfRangeException(nameof(machine))
        };

    public static string GetDeclineStartThrottleKey(GoldSaucerArcadeMachine machine) =>
        machine switch
        {
            GoldSaucerArcadeMachine.Cuff => "Saucy.CuffACur.DeclineStart",
            GoldSaucerArcadeMachine.Limb => "Saucy.OutOnALimb.DeclineStart",
            var _ => throw new ArgumentOutOfRangeException(nameof(machine))
        };

    public static void DisableConflictingModules(GoldSaucerArcadeMachine? keeping = null)
    {
        if (keeping != GoldSaucerArcadeMachine.Cuff &&
            IsEnabled(GoldSaucerArcadeMachine.Cuff))
        {
            C.SetModuleEnabled(ModuleNames.CuffACur, false);
        }

        if (keeping != GoldSaucerArcadeMachine.Limb &&
            IsEnabled(GoldSaucerArcadeMachine.Limb))
        {
            C.SetModuleEnabled(ModuleNames.OutOnALimb, false);
        }

        if (keeping != null && TriadRunSession.ModuleEnabled)
        {
            TriadRunSession.ModuleEnabled = false;
        }
    }
}
