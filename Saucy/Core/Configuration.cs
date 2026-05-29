using Dalamud.Configuration;
using ECommons.Configuration;
using Newtonsoft.Json;
using Saucy.OutOnALimb;
using Saucy.TripleTriad;
using System;
using System.Collections.ObjectModel;
namespace Saucy;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public bool AnyWayTheWindBlowsModuleEnabled = false;
    public bool EnableAutoMiniCactpot = false;
    public bool EnableCuffModule = false;

    public ObservableCollection<string> EnabledModules = [];

    public bool SliceIsRightModuleEnabled = false;

    public bool UseRecommendedDeck { get; set; } = false;

    public int SelectedDeckIndex { get; set; } = -1;

    public Stats Stats { get; set; } = new();

    [JsonIgnore]
    public Stats SessionStats { get; set; } = new();

    public bool PlaySound { get; set; } = false;
    public string SelectedSound { get; set; } = "Moogle";
    public bool OnlyUnobtainedCards { get; set; } = false;
    public bool OpenAutomatically { get; set; } = false;

    public LimbConfig LimbConfig { get; set; } = new();
    public bool SaucyThemeEnabled { get; set; } = true;
    public int Version { get; set; } = 0;

    public bool AirForceEnabled = false;

    [JsonProperty("TriadBuddyCollectionUiEnabled")]
    public bool CollectionUiEnabled { get; set; } = true;

    [JsonProperty("TriadBuddySettings")]
    public TriadCollectionSettings TriadCollection { get; set; } = new();

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
