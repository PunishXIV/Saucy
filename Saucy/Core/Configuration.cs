using Dalamud.Configuration;
using ECommons.Configuration;
using Newtonsoft.Json;
using Saucy.OutOnALimb;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace Saucy;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int ConfigVersionBackgroundCpuCores = 1;

    public const int GameRecommendedDeckIndex = -2;
    public ObservableCollection<string> EnabledModules = [];

    public bool UseSimmedDeck { get; set; } = false;

    public bool AlwaysBuildOptimizedDeck { get; set; } = false;

    public bool UseCachedOptimizedDeckIfAvailable { get; set; } = false;
    public bool ShowOptimizerChatSpam { get; set; } = false;

    public Dictionary<int, long> TriadOptimizedDeckBuiltUtcTicksByNpcId { get; set; } = [];

    public int SelectedDeckIndex { get; set; } = -1;

    [JsonIgnore]
    public TriadRunMode TriadRunMode { get; set; } = TriadRunMode.None;

    public int TriadMatchCount { get; set; } = 1;

    public bool LogOutAfterTriadRun { get; set; }

    public Stats Stats { get; set; } = new();

    [JsonIgnore]
    public Stats SessionStats { get; set; } = new();

    [JsonIgnore]
    public DateTime SessionStartTime { get; set; } = DateTime.UtcNow;

    public bool PlaySound { get; set; } = false;
    public string SelectedSound { get; set; } = "Moogle";
    public bool OnlyUnobtainedCards { get; set; } = false;
    public bool OpenAutomatically { get; set; } = false;

    public LimbConfig LimbConfig { get; set; } = new();

    public GoldSaucerArcadeRunSettings CuffArcadeRun { get; set; } = new();

    public GoldSaucerArcadeRunSettings LimbArcadeRun { get; set; } = new();

    public bool SaucyThemeEnabled { get; set; } = true;

    public bool CollectionUiEnabled { get; set; } = true;

    [JsonProperty("BackgroundWorkCpuCores")]
    public int DeckOptimizerMaxThreads { get; set; }

    public int DeckOptimizerTimeoutMinutes { get; set; } = 2;

    [JsonProperty("CpuUsagePercent")]
    private int LegacyCpuUsagePercent { get; set; } = 100;

    public TriadCollectionSettings TriadCollection { get; set; } = new();

    public GoldSaucerGateSettings GoldSaucerGates { get; set; } = new();

    public bool PauseForAutoRetainer { get; set; }

    public int Version { get; set; }

    public void MigrateToBackgroundCpuCores()
    {
        if (Version < ConfigVersionBackgroundCpuCores)
        {
            if (DeckOptimizerMaxThreads <= 0)
            {
                var pct = Math.Clamp(LegacyCpuUsagePercent, 10, 100);
                DeckOptimizerMaxThreads = pct >= 100
                    ? 0
                    : Math.Max(1, Environment.ProcessorCount * pct / 100);
            }
            else
            {
                DeckOptimizerMaxThreads = ClampDeckOptimizerMaxThreads(DeckOptimizerMaxThreads);
            }

            Version = ConfigVersionBackgroundCpuCores;
        }
        else
        {
            DeckOptimizerMaxThreads = ClampDeckOptimizerMaxThreads(DeckOptimizerMaxThreads);
        }
    }

    public static int ClampDeckOptimizerMaxThreads(int threads) =>
        Math.Clamp(threads, 0, Environment.ProcessorCount);

    public bool IsModuleEnabled(string moduleName) => EnabledModules.Contains(moduleName);

    public void SetModuleEnabled(string moduleName, bool enabled)
    {
        if (enabled)
        {
            if (!EnabledModules.Contains(moduleName))
            {
                EnabledModules.Add(moduleName);
            }
        }
        else
        {
            EnabledModules.Remove(moduleName);
        }
    }

    public void UpdateStats(Action<Stats> updateAction)
    {
        updateAction(Stats);
        updateAction(SessionStats);
    }

    public void Save() => EzConfig.Save();
}

[Serializable]
public class GoldSaucerGateSettings
{
    public bool SliceIsRightAutoMovement { get; set; }

    public bool WindBlowsAutoMovement { get; set; }
}
