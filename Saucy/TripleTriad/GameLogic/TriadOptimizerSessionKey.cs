using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace Saucy.TripleTriad.GameLogic;

internal static class TriadOptimizerSessionKey
{
    public static string Build(TriadNpc? npc, IEnumerable<TriadGameModifier> regionMods)
    {
        if (npc is null)
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
        string[] leftSignatures = [.. CollectModSignatures(left).OrderBy(sig => sig, StringComparer.Ordinal)];
        string[] rightSignatures = [.. CollectModSignatures(right).OrderBy(sig => sig, StringComparer.Ordinal)];
        return leftSignatures.SequenceEqual(rightSignatures, StringComparer.Ordinal);
    }

    public static string[] GetModSignatures(IEnumerable<TriadGameModifier> regionMods) =>
        [.. CollectModSignatures(regionMods).OrderBy(sig => sig, StringComparer.Ordinal)];

    public static List<TriadGameModifier> RegionModsFromSignatures(IEnumerable<string> signatures)
    {
        var result = new List<TriadGameModifier>();
        var modDb = TriadGameModifierDB.Get();
        foreach (var signature in signatures)
        {
            if (string.IsNullOrEmpty(signature))
            {
                continue;
            }

            var dotIdx = signature.IndexOf('.');
            if (dotIdx > 0 &&
                int.TryParse(signature[..dotIdx], out var rouletteId) &&
                int.TryParse(signature[(dotIdx + 1)..], out var resolvedId) &&
                rouletteId >= 0 &&
                rouletteId < modDb.mods.Count &&
                modDb.mods[rouletteId] is TriadGameModifierRoulette rouletteTemplate)
            {
                var roulette = (TriadGameModifierRoulette)rouletteTemplate.Clone();
                if (resolvedId >= 0 && resolvedId < modDb.mods.Count)
                {
                    roulette.SetRuleInstance(modDb.mods[resolvedId].Clone());
                }

                result.Add(roulette);
                continue;
            }

            if (!int.TryParse(signature, out var modId) ||
                modId < 0 ||
                modId >= modDb.mods.Count)
            {
                continue;
            }

            var clone = modDb.mods[modId].Clone();
            result.Add(clone);
        }

        return result;
    }

    public static bool ShouldSkipDeckCache(TriadNpc? npc, IReadOnlyList<TriadGameModifier> regionMods)
    {
        foreach (var mod in regionMods)
        {
            if (mod is TriadGameModifierRoulette roulette && roulette.GetResolvedRule() is null)
            {
                return true;
            }
        }

        if (regionMods.Count != 0)
        {
            return false;
        }

        return npc?.Rules?.Any(mod => mod is TriadGameModifierRoulette) == true;
    }

    private static IEnumerable<string> CollectModSignatures(IEnumerable<TriadGameModifier> regionMods)
    {
        foreach (var mod in regionMods)
        {
            if (mod is null)
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
