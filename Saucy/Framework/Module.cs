using ECommons.Automation.NeoTaskManager;
using FFXIVClientStructs.FFXIV.Client.Game.GoldSaucer;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;

namespace Saucy.Framework;
public abstract partial class Module : IModule
{
    public Module()
    {
        InternalName = GetType().Name;
        TaskManager = new();
    }

    public string InternalName { get; init; }
    public abstract string Name { get; }
    public virtual bool IsEnabled { get; protected set; }
    public virtual void Enable() { }
    public virtual void Disable() { }

    protected TaskManager TaskManager;

    public void ExecuteTask(Action action)
    {
        if (C.LittleBitchDelay > 0)
        {
            TaskManager.EnqueueDelay(C.LittleBitchDelay);
            TaskManager.Enqueue(action);
        }
        else
            action();
    }

    public bool InSaucer => Svc.ClientState.TerritoryType is 144;

    public unsafe bool PlayerOnStage
    {
        get
        {
            var mgr = GoldSaucerManager.Instance();
            if (mgr is null) return false;
            var dir = mgr->CurrentGFateDirector;
            return dir is not null && dir->Flags.HasFlag(GFateDirectorFlag.IsJoined) && !dir->Flags.HasFlag(GFateDirectorFlag.IsFinished);
        }
    }

    public unsafe GateType CurrentGate
    {
        get
        {
            var mgr = GoldSaucerManager.Instance();
            if (mgr is null) return GateType.None;
            var dir = mgr->CurrentGFateDirector;
            return dir is not null ? (GateType)dir->GateType : GateType.None;
        }
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
        SliceIsRight = 8,
    }

    public enum GatePositionType : byte
    {
        WonderSquareEast = 1,
        EventSquare = 2,
        RoundSquare = 3,
        TheCactpotBoard = 4,
    }
}

public abstract partial class Module
{
    internal virtual void EnableInternal()
    {
        try
        {
            Log($"Enabling module {InternalName}");
            Enable();
        }
        catch (Exception ex)
        {
            LogError($"Failed to enable module: {ex}");
            return;
        }

        IsEnabled = true;
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
