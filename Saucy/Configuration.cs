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
    public bool AnyWayTheWindBlowsModuleEnabled = false;
    public bool EnableAutoMiniCactpot = false;

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
    public int Version { get; set; } = 0;

    public void UpdateStats(Action<Stats> updateAction)
    {
        updateAction(Stats);
        updateAction(SessionStats);
    }

    public void Save() => EzConfig.Save();

    public void MigrateModuleSettings()
    {
        if (Version >= 1)
        {
            return;
        }

        SyncModuleFlag(EnableAutoMiniCactpot, "MiniCactpot");
        SyncModuleFlag(SliceIsRightModuleEnabled, "SliceIsRight");
        SyncModuleFlag(AnyWayTheWindBlowsModuleEnabled, "AnyWayTheWindBlows");

        EnableAutoMiniCactpot = EnabledModules.Contains("MiniCactpot");
        SliceIsRightModuleEnabled = EnabledModules.Contains("SliceIsRight");
        AnyWayTheWindBlowsModuleEnabled = EnabledModules.Contains("AnyWayTheWindBlows");

        Version = 1;
        Save();
    }

    private void SyncModuleFlag(bool enabled, string moduleName)
    {
        if (enabled && !EnabledModules.Contains(moduleName))
        {
            EnabledModules.Add(moduleName);
        }
    }
}
