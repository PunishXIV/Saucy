using ECommons.EzIpcManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace Saucy.IPC;

internal static class SubscriptionManager
{
    private static IpcEntry[]? _ipcEntries;
    private static readonly Dictionary<string, EzIPCDisposalToken[]> InitializedIpcs = [];
    private static int _subscribeTick;

    internal static void Prepare()
    {
        if (_ipcEntries != null)
        {
            return;
        }

        _ipcEntries =
        [
            .. Assembly.GetExecutingAssembly()
                .GetTypes()
                .Select(type => (Type: type, Attr: type.GetCustomAttribute<IPCAttribute>()))
                .Where(entry => entry.Attr != null)
                .Select(entry => new IpcEntry(entry.Type, entry.Attr!.Name))
        ];
    }

    internal static bool IsInitialized(string plugin) =>
        InitializedIpcs.ContainsKey(plugin) && IsLoaded(plugin);

    internal static bool IsLoaded(string pluginName) =>
        Svc.PluginInterface.InstalledPlugins.Any(x => x.InternalName == pluginName && x.IsLoaded);

    internal static void Subscribe()
    {
        try
        {
            Prepare();
            var entries = _ipcEntries!;
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
