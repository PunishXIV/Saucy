using Dalamud.Configuration;
using System;
using System.Collections.Generic;
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

    public Dictionary<string, TriadOptimizedDeckCacheEntry> Entries { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = TriadOptimizedDeckCacheStore.SchemaVersion;
}
