using System;
using System.Collections.Generic;

namespace Saucy;

[Serializable]
public class TriadCollectionSettings
{
    public bool CheckCardNpcMatchOnly { get; set; }
    public bool CheckCardNotOwnedOnly { get; set; }
    public bool CheckNpcHideBeaten { get; set; }
    public bool CheckNpcHideCompleted { get; set; }
    public Dictionary<int, TriadNpcStatRecord> NpcStats { get; set; } = [];
}

[Serializable]
public class TriadNpcStatRecord
{
    public Dictionary<int, int> Cards { get; set; } = [];
    public int NumCoins { get; set; }
    public int NumWins { get; set; }
    public int NumDraws { get; set; }
    public int NumLosses { get; set; }
    public int GetNumMatches() => NumWins + NumDraws + NumLosses;
}
