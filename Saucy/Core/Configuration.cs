using Dalamud.Configuration;
using ECommons.Configuration;
using Newtonsoft.Json;
using Saucy.OutOnALimb;
using System;
using System.Collections.ObjectModel;
namespace Saucy;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public bool AirForceEnabled = false;
    public bool AnyWayTheWindBlowsModuleEnabled;
    public bool EnableAutoMiniCactpot;
    public bool EnableCuffModule;

    public ObservableCollection<string> EnabledModules = [];

    public bool SliceIsRightModuleEnabled;

    public bool UseRecommendedDeck { get; set; } = false;

    public bool LogTriadDeckOptimizerToChat { get; set; } = false;

    public int SelectedDeckIndex { get; set; } = -1;

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
    public bool SaucyThemeEnabled { get; set; } = true;

    [JsonProperty("TriadBuddyCollectionUiEnabled")]
    public bool CollectionUiEnabled { get; set; } = true;

    [JsonProperty("TriadBuddySettings")]
    public TriadCollectionSettings TriadCollection { get; set; } = new();
    public int Version { get; set; }

    public void UpdateStats(Action<Stats> updateAction)
    {
        updateAction(Stats);
        updateAction(SessionStats);
    }

    public void Save() => EzConfig.Save();

    public void MigrateModuleSettings()
    {
        var changed = false;

        if (Version < 1)
        {
            SyncModuleFlag(EnableAutoMiniCactpot, "MiniCactpot");
            SyncModuleFlag(SliceIsRightModuleEnabled, "SliceIsRight");
            SyncModuleFlag(AnyWayTheWindBlowsModuleEnabled, "AnyWayTheWindBlows");

            EnableAutoMiniCactpot = EnabledModules.Contains("MiniCactpot");
            SliceIsRightModuleEnabled = EnabledModules.Contains("SliceIsRight");
            AnyWayTheWindBlowsModuleEnabled = EnabledModules.Contains("AnyWayTheWindBlows");

            Version = 1;
            changed = true;
        }

        if (Version < 2)
        {
            SyncModuleFlag(LimbConfig.EnableLimb, "OutOnALimbModule");
            SyncModuleFlag(EnableCuffModule, "CuffACurModule");

            LimbConfig.EnableLimb = EnabledModules.Contains("OutOnALimbModule");
            EnableCuffModule = EnabledModules.Contains("CuffACurModule");

            Version = 2;
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    private void SyncModuleFlag(bool enabled, string moduleName)
    {
        if (enabled && !EnabledModules.Contains(moduleName))
        {
            EnabledModules.Add(moduleName);
        }
    }
}
