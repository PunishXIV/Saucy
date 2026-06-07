using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public sealed class TriadOptimizedDeckCacheCharacterView
{
    public ulong ContentId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public bool IsCurrentCharacter { get; init; }

    public IReadOnlyList<TriadOptimizedDeckCacheEntry> Entries { get; init; } = [];
}
