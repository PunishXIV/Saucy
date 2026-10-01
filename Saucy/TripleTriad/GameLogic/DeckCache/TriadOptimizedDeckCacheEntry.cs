using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace Saucy.TripleTriad.GameLogic;

[Serializable]
public sealed class TriadOptimizedDeckCacheEntry
{
    public const int DeckSize = 5;

    public string SessionKey { get; set; } = string.Empty;

    public int NpcId { get; set; }

    public string NpcName { get; set; } = string.Empty;

    public ushort[] CardIds { get; set; } = new ushort[DeckSize];

    public long BuiltUtcTicks { get; set; }

    public int[] OwnedCardIdsAtBuild { get; set; } = [];

    public float EstWinChance { get; set; }
}

[Serializable]
public sealed class TriadOptimizedDeckCacheFile : IPluginConfiguration
{
    public ulong ContentId { get; set; }

    public string CharacterName { get; set; } = string.Empty;

    public uint HomeWorldRowId { get; set; }

    public int Version { get; set; } = TriadOptimizedDeckCacheStore.SchemaVersion;

#pragma warning disable IDE0028 // StringComparer cannot use collection expressions without losing comparer semantics
    public Dictionary<string, TriadOptimizedDeckCacheEntry> Entries { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<int, string[]> RegionalRuleSignaturesByNpcId { get; set; } = [];
#pragma warning restore IDE0028
}

public sealed class TriadOptimizedDeckCacheCharacterView
{
    public ulong ContentId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public bool IsCurrentCharacter { get; init; }

    public IReadOnlyList<TriadOptimizedDeckCacheEntry> Entries { get; init; } = [];
}

internal static class TriadEvalCacheKey
{
    private const string RulesVersion = "v5";

    public static string Build(TriadNpc? npc, IEnumerable<TriadGameModifier> regionMods) =>
        npc is null ? string.Empty : Build(npc.Name, regionMods);

    public static string Build(string npcName, IEnumerable<TriadGameModifier> rules)
    {
        if (string.IsNullOrEmpty(npcName))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(npcName);
        builder.Append('|').Append(RulesVersion);
        foreach (var ruleIndex in rules.Select(mod => mod.GetLocalizationId()).Order())
        {
            builder.Append('|');
            builder.Append(ruleIndex);
        }

        return builder.ToString();
    }
}
