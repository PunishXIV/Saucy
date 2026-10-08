using ECommons.EzIpcManager;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Saucy.IPC;

internal static class IPCNames
{
    public const string Lifestream = "Lifestream";
    public const string BossMod = "BossMod";
    public const string Vnavmesh = "vnavmesh";
    public const string Questionable = "Questionable";
    public const string AutoRetainer = "AutoRetainer";
}

internal static class SubscriptionManager
{
    private static readonly IpcEntry[] IpcEntries =
    [
        new(typeof(AutoRetainerIpc), IPCNames.AutoRetainer),
        new(typeof(BossMod), IPCNames.BossMod),
        new(typeof(Lifestream), IPCNames.Lifestream),
        new(typeof(Questionable), IPCNames.Questionable),
        new(typeof(Vnavmesh), IPCNames.Vnavmesh),
    ];

    private static readonly Dictionary<string, EzIPCDisposalToken[]> InitializedIpcs = [];
    private static int _subscribeTick;

    internal static bool IsInitialized(string plugin) =>
        InitializedIpcs.ContainsKey(plugin) && IsLoaded(plugin);

    internal static bool IsLoaded(string pluginName) =>
        Svc.PluginInterface.InstalledPlugins.Any(x => x.InternalName == pluginName && x.IsLoaded);

    internal static void Subscribe()
    {
        try
        {
            var entries = IpcEntries;
            var allInitialized = InitializedIpcs.Count == entries.Length;
            _subscribeTick++;

            if (allInitialized)
            {
                if (_subscribeTick % 120 != 0)
                {
                    return;
                }
            }
            else if (_subscribeTick % 10 != 0)
            {
                return;
            }

            foreach (var entry in entries)
            {
                if (!IsInitialized(entry.PluginName))
                {
                    if (!IsLoaded(entry.PluginName))
                    {
                        continue;
                    }

                    InitializedIpcs[entry.PluginName] = EzIPC.Init(entry.Type, entry.PluginName);
                }
                else if (!IsLoaded(entry.PluginName))
                {
                    foreach (var token in InitializedIpcs[entry.PluginName])
                    {
                        token.Dispose();
                    }

                    InitializedIpcs.Remove(entry.PluginName);
                }
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Could not subscribe to IPCs");
        }
    }

    internal static void DisposeAll()
    {
        foreach (var tokens in InitializedIpcs.Values)
        {
            foreach (var token in tokens)
            {
                token.Dispose();
            }
        }

        InitializedIpcs.Clear();
        QuestionableTriad.ClearUnsupportedCache();
    }
    private sealed record IpcEntry(Type Type, string PluginName);
}
