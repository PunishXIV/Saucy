using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace Saucy.TripleTriad.GameLogic;

internal static class TriadEvalCacheKey
{
    private const string RulesVersion = "v4";

    public static string Build(TriadNpc npc, IEnumerable<TriadGameModifier> regionMods) =>
        npc == null ? string.Empty : Build(npc.Name, regionMods);

    public static string Build(string npcName, IEnumerable<TriadGameModifier> rules)
    {
        if (string.IsNullOrEmpty(npcName))
        {
            return string.Empty;
        }

        if (rules == null)
        {
            return npcName;
        }

        var builder = new StringBuilder(npcName);
        builder.Append('|').Append(RulesVersion);
        if (rules != null)
        {
            foreach (var mod in rules.OrderBy(mod => mod.GetType().FullName))
            {
                builder.Append('|');
                builder.Append(mod.GetType().Name);
            }
        }

        return builder.ToString();
    }
}
