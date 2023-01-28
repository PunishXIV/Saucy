using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace TriadBuddyPlugin
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public bool ShowSolverHintsInGame { get; set; } = true;
        public bool CanUseProfileReader { get; set; } = true;
        public float DeckOptimizerCPU { get; set; } = 1.0f;

        public bool CheckCardNpcMatchOnly { get; set; } = false;
        public bool CheckCardNotOwnedOnly { get; set; } = false;
        public bool CheckNpcHideBeaten { get; set; } = false;
        public bool CheckNpcHideCompleted { get; set; } = false;

        [Serializable]
        public class NpcStatInfo
        {
            public Dictionary<int, int> Cards { get; set; } = new();
            public int NumCoins { get; set; } = 0;
            public int NumWins { get; set; } = 0;
            public int NumDraws { get; set; } = 0;
            public int NumLosses { get; set; } = 0;

            public int GetNumMatches() => (NumWins + NumDraws + NumLosses);
        }

        public Dictionary<int, NpcStatInfo> NpcStats { get; set; } = new();

        [NonSerialized]
        private DalamudPluginInterface pluginInterface;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            pluginInterface?.SavePluginConfig(this);
        }
    }
}
