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
    public TriadOptimizedDeckCacheFile()
    {
        Entries = new(StringComparer.Ordinal);
    }
    public ulong ContentId { get; set; }

    public string CharacterName { get; set; } = string.Empty;

    public uint HomeWorldRowId { get; set; }

    public Dictionary<string, TriadOptimizedDeckCacheEntry> Entries { get; set; }

    public int Version { get; set; } = TriadOptimizedDeckCacheStore.SchemaVersion;
}

public sealed class TriadOptimizedDeckCacheCharacterView
{
    public ulong ContentId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public bool IsCurrentCharacter { get; init; }

    public IReadOnlyList<TriadOptimizedDeckCacheEntry> Entries { get; init; } = [];
}
