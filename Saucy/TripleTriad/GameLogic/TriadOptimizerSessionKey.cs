using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace Saucy.TripleTriad.GameLogic;

internal static class TriadOptimizerSessionKey
{
    public static string Build(TriadNpc npc, IEnumerable<TriadGameModifier> regionMods)
    {
        if (npc == null)
        {
            return string.Empty;
        }

        var key = new StringBuilder(npc.Id.ToString());
        foreach (var signature in CollectModSignatures(regionMods).OrderBy(sig => sig, StringComparer.Ordinal))
        {
            key.Append(':').Append(signature);
        }

        return key.ToString();
    }

    public static bool RegionModsEqual(IReadOnlyList<TriadGameModifier> left, IReadOnlyList<TriadGameModifier> right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        var leftSignatures = CollectModSignatures(left).OrderBy(sig => sig, StringComparer.Ordinal).ToArray();
        var rightSignatures = CollectModSignatures(right).OrderBy(sig => sig, StringComparer.Ordinal).ToArray();
        return leftSignatures.SequenceEqual(rightSignatures, StringComparer.Ordinal);
    }

    public static bool ShouldSkipDeckCache(TriadNpc npc, IReadOnlyList<TriadGameModifier> regionMods)
    {
        if (regionMods != null)
        {
            foreach (var mod in regionMods)
            {
                if (mod is TriadGameModifierRoulette roulette && roulette.GetResolvedRule() == null)
                {
                    return true;
                }
            }
        }

        if (regionMods == null || regionMods.Count != 0)
        {
            return false;
        }

        return npc?.Rules?.Any(mod => mod is TriadGameModifierRoulette) == true;
    }

    private static IEnumerable<string> CollectModSignatures(IEnumerable<TriadGameModifier> regionMods)
    {
        if (regionMods == null)
        {
            yield break;
        }

        foreach (var mod in regionMods)
        {
            if (mod == null)
            {
                continue;
            }

            yield return BuildModSignature(mod);
        }
    }

    private static string BuildModSignature(TriadGameModifier mod)
    {
        if (mod is TriadGameModifierRoulette roulette && roulette.GetResolvedRule() is { } resolvedRule)
        {
            return $"{mod.GetLocalizationId()}.{resolvedRule.GetLocalizationId()}";
        }

        return mod.GetLocalizationId().ToString();
    }
}
