using ECommons.Automation.NeoTaskManager;
using System;
namespace Saucy.Framework;

public abstract partial class Module : IModule
{
    public enum GatePositionType : byte
    {
        WonderSquareEast = 1,
        EventSquare = 2,
        RoundSquare = 3,
        TheCactpotBoard = 4
    }

    public enum GateType : byte
    {
        None = 0,
        Cliffhanger = 1,
        VaseOff = 2,
        SkinchangeWeCanBelieveIn = 3,
        TheTimeOfMyLife = 4,
        AnyWayTheWindBlows = 5,
        LeapOfFaith = 6,
        AirForceOne = 7,
        SliceIsRight = 8
    }

    protected TaskManager TaskManager;
    protected TaskManagerConfiguration TaskManagerConfiguration;

    public Module()
    {
        InternalName = GetType().Name;
        TaskManagerConfiguration = CreateTaskManagerConfiguration();
        TaskManager = new(TaskManagerConfiguration);
    }
    public bool InSaucer => GateDirector.InSaucer;

    public bool PlayerOnStage => GateDirector.IsPlayerOnStage();

    public GateType CurrentGate => GateDirector.GetCurrentGate();

    protected bool IsInGate(GateType gate) => GateDirector.IsInGate(gate);

    public string InternalName { get; init; }
    public abstract string Name { get; }
    public virtual bool IsEnabled { get; protected set; }
    public virtual void Enable() { }
    public virtual void Disable() { }

    protected virtual TaskManagerConfiguration CreateTaskManagerConfiguration() => new()
    {
        ShowDebug = false, TimeLimitMS = 5000, AbortOnTimeout = true
    };
}

public abstract partial class Module
{
    internal virtual void EnableInternal()
    {
        try
        {
            Log($"Enabling module {InternalName}");
            IsEnabled = true;
            Enable();
        }
        catch (Exception ex)
        {
            LogError($"Failed to enable module: {ex}");
            IsEnabled = false;
        }
    }

    internal virtual void DisableInternal()
    {
        try
        {
            Log($"Disabling module {InternalName}");
            Disable();
        }
        catch (Exception ex)
        {
            LogError($"Failed to disable module: {ex}");
            return;
        }

        IsEnabled = false;
    }
}

public abstract partial class Module
{
    public void Log(string message) => PluginLog.Information($"[{InternalName}] {message}");
    public void LogDebug(string message) => PluginLog.Debug($"[{InternalName}] {message}");
    public void LogVerbose(string message) => PluginLog.Verbose($"[{InternalName}] {message}");
    public void LogWarning(string message) => PluginLog.Warning($"[{InternalName}] {message}");
    public void LogError(string message) => PluginLog.Error($"[{InternalName}] {message}");
}
