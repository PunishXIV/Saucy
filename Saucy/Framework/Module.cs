namespace Saucy.Framework;
public abstract partial class Module : IModule
{
    public Module()
    {
        InternalName = GetType().Name;
    }

    public string InternalName { get; init; }
    public abstract string Name { get; }
    public virtual bool IsEnabled
    {
        get;
        set
        {
            field = value;
            if (value)
                Enable();
            else
                Disable();
        }
    }
    public virtual void Enable() { }
    public virtual void Disable() { }
}

public abstract partial class Module
{
    public void Log(string message) => PluginLog.Information($"[{InternalName}] {message}");
    public void LogDebug(string message) => PluginLog.Debug($"[{InternalName}] {message}");
    public void LogVerbose(string message) => PluginLog.Verbose($"[{InternalName}] {message}");
    public void LogWarning(string message) => PluginLog.Warning($"[{InternalName}] {message}");
    public void LogError(string message) => PluginLog.Error($"[{InternalName}] {message}");
}
