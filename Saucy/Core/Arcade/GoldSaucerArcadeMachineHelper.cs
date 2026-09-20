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
            disabled.Add(ModuleDisplayNames.CuffACur);
        }

        if (keeping != GoldSaucerArcadeMachine.Limb &&
            IsEnabled(GoldSaucerArcadeMachine.Limb))
        {
            C.SetModuleEnabled(ModuleNames.OutOnALimb, false);
            disabled.Add(ModuleDisplayNames.OutOnALimb);
        }

        if (keeping != null && TriadRunSession.ModuleEnabled)
        {
            TriadRunSession.ModuleEnabled = false;
            disabled.Add(ModuleDisplayNames.TripleTriad);
        }

        if (disabled.Count == 0)
        {
            return;
        }

        var enabledLabel = keeping switch
        {
            GoldSaucerArcadeMachine.Cuff => ModuleDisplayNames.CuffACur,
            GoldSaucerArcadeMachine.Limb => ModuleDisplayNames.OutOnALimb,
            var _ => ModuleDisplayNames.TripleTriad
        };

        // Two whole sentences rather than joining a list: CJK wants 、 where English wants
        // " and ", and a punctuation-only key is not something a translator can act on.
        // At most two modules can be disabled here — whichever of the three is being kept
        // is skipped, and the Triple Triad branch only runs when an arcade machine is kept.
        DuoLog.Warning(disabled.Count == 1
            ? Loc.T("Disabled {0} to enable {1}.", Loc.T(disabled[0]), Loc.T(enabledLabel))
            : Loc.T("Disabled {0} and {1} to enable {2}.", Loc.T(disabled[0]), Loc.T(disabled[1]), Loc.T(enabledLabel)));
    }
}
