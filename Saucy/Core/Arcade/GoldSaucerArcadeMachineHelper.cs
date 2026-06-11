using Saucy.Framework;
using System;
using System.Collections.Generic;
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
        var disabled = new List<string>();

        if (keeping != GoldSaucerArcadeMachine.Cuff &&
            IsEnabled(GoldSaucerArcadeMachine.Cuff))
        {
            C.SetModuleEnabled(ModuleNames.CuffACur, false);
            disabled.Add("Cuff-a-Cur");
        }

        if (keeping != GoldSaucerArcadeMachine.Limb &&
            IsEnabled(GoldSaucerArcadeMachine.Limb))
        {
            C.SetModuleEnabled(ModuleNames.OutOnALimb, false);
            disabled.Add("Out on a Limb");
        }

        if (keeping != null && TriadRunSession.ModuleEnabled)
        {
            TriadRunSession.ModuleEnabled = false;
            disabled.Add("Triple Triad");
        }

        if (disabled.Count == 0)
        {
            return;
        }

        var enabledLabel = keeping switch
        {
            GoldSaucerArcadeMachine.Cuff => "Cuff-a-Cur",
            GoldSaucerArcadeMachine.Limb => "Out on a Limb",
            var _ => "Triple Triad"
        };

        DuoLog.Warning($"Disabled {string.Join(" and ", disabled)} to enable {enabledLabel}.");
    }
}
