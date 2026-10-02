using ECommons;
using ECommons.Automation.NeoTaskManager;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Saucy.Framework;

public interface IModule
{
    string InternalName { get; }
    string Name { get; }
    bool IsEnabled { get; }
    void Enable();
    void Disable();
}

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
        TaskManagerConfiguration = CreateTaskManagerConfiguration();
        TaskManager = new(TaskManagerConfiguration);
    }
    public bool InSaucer => GateDirector.InSaucer;

    public abstract string InternalName { get; }
    public abstract string Name { get; }
    public virtual bool IsEnabled { get; protected set; }
    public virtual void Enable() { }
    public virtual void Disable() { }

    protected bool IsInGate(GateType gate) => GateDirector.IsInGate(gate);

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
    public void LogVerbose(string message) => PluginLog.Verbose($"[{InternalName}] {message}");
    public void LogError(string message) => PluginLog.Error($"[{InternalName}] {message}");
}

public class ModuleManager : IDisposable
{
    private readonly List<Module> _modules = [];

    public ModuleManager()
    {
        Func<Module>[] factories =
        [
            () => new MiniCactpot.MiniCactpot(),
            () => new JumboCactpot.JumboCactpot(),
            () => new OtherGames.SliceIsRight(),
            () => new OtherGames.AnyWayTheWindBlows(),
            () => new OutOnALimb.OutOnALimbModule(),
            () => new CuffACur.CuffACurModule(),
            () => new AirForce.AirForceOne(),
        ];
        foreach (var factory in factories)
        {
            try
            {
                _modules.Add(factory());
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, $"[{nameof(ModuleManager)}] Failed to create module");
            }
        }

        foreach (var m in _modules)
        {
            if (!C.EnabledModules.Contains(m.InternalName))
            {
                continue;
            }
            GenericHelpers.TryExecute(m.EnableInternal);
        }
    }

    public IReadOnlyList<Module> Modules => _modules.AsReadOnly();

    public void Dispose()
    {
        _modules.ForEach(m => m.DisableInternal());
        _modules.Clear();
    }

    public T? GetModule<T>() where T : class, IModule => _modules.OfType<T>().FirstOrDefault();
}
