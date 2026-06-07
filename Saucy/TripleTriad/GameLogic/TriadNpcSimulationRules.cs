using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

internal static class TriadNpcSimulationRules
{
    public static void InitializeSimulation(
        TriadGameSolver solver,
        TriadNpc npc,
        IEnumerable<TriadGameModifier> regionMods)
    {
        if (solver == null || npc == null)
        {
            return;
        }

        var regionArray = BuildRegionMods(npc, regionMods);
        if (regionArray.Length > 0)
        {
            solver.InitializeSimulation(npc.Rules, regionArray);
        }
        else
        {
            solver.InitializeSimulation(npc.Rules);
        }
    }

    public static TriadGameModifier[] BuildRegionMods(TriadNpc npc, IEnumerable<TriadGameModifier> regionMods)
    {
        if (npc == null || regionMods == null)
        {
            return [];
        }

        var result = new List<TriadGameModifier>();
        var removedNpcMod = new bool[2];

        foreach (var mod in regionMods)
        {
            if (mod == null)
            {
                continue;
            }

            var npcModIdx = npc.Rules.FindIndex(x => x.GetLocalizationId() == mod.GetLocalizationId());
            if (npcModIdx >= 0 && npcModIdx < removedNpcMod.Length && !removedNpcMod[npcModIdx])
            {
                removedNpcMod[npcModIdx] = true;
                continue;
            }

            var modOb = mod.Clone();
            if (modOb != null)
            {
                result.Add(modOb);
            }
        }

        return [.. result];
    }
}
