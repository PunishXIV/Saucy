using Dalamud.Plugin.Services;
using ECommons;
using ECommons.EzIpcManager;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Saucy.IPC;

internal static class SubscriptionManager
{
    private static readonly Dictionary<string, EzIPCDisposalToken[]> InitializedIpcs = new();

    internal static bool IsInitialized(string plugin) =>
        InitializedIpcs.ContainsKey(plugin) && IsLoaded(plugin);

    internal static bool IsLoaded(string pluginName) =>
        Svc.PluginInterface.InstalledPlugins.Any(x => x.InternalName == pluginName && x.IsLoaded);

    internal static void Subscribe(IFramework framework)
    {
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes().Where(type => type.GetCustomAttribute<IPCAttribute>() != null))
        {
            var attr = type.GetCustomAttribute<IPCAttribute>()!;
            if (!IsInitialized(attr.Name))
            {
                if (!IsLoaded(attr.Name))
                    continue;

                var disposals = EzIPC.Init(type, attr.Name);
                InitializedIpcs.Add(attr.Name, disposals);
            }
            else if (!IsLoaded(attr.Name))
            {
                foreach (var token in InitializedIpcs[attr.Name])
                    token.Dispose();

                InitializedIpcs.Remove(attr.Name);
            }
        }
    }

    internal static void DisposeAll()
    {
        foreach (var tokens in InitializedIpcs.Values)
        {
            foreach (var token in tokens)
                token.Dispose();
        }

        InitializedIpcs.Clear();
        QuestionableTriad.ClearUnsupportedCache();
    }
}
