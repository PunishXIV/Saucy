using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;
using Saucy.OutOnALimb;
using System;
using System.Collections.ObjectModel;

namespace Saucy;
[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool UseRecommendedDeck { get; set; } = false;

    public int SelectedDeckIndex { get; set; } = -1;

    public Stats Stats { get; set; } = new Stats();

    [JsonIgnore]
    public Stats SessionStats { get; set; } = new Stats();

    public bool PlaySound { get; set; } = false;
    public string SelectedSound { get; set; } = "Moogle";
    public bool OnlyUnobtainedCards { get; set; } = false;
    public bool OpenAutomatically { get; set; } = false;

    public ObservableCollection<string> EnabledModules = [];

    public bool SliceIsRightModuleEnabled { get; set; }
    public bool AnyWayTheWindowBlowsModuleEnabled = false;

    public LimbConfig LimbConfig { get; set; } = new();
    public bool EnableAutoMiniCactpot = false;

    public int LittleBitchDelay = 250;

    // the below exist just to make saving less cumbersome

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void UpdateStats(Action<Stats> updateAction)
    {
        updateAction(Stats);
        updateAction(SessionStats);
    }

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        pluginInterface!.SavePluginConfig(this);
    }
}
