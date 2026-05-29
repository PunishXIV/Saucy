using Dalamud.Plugin.Services;
using ECommons.EzIpcManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace Saucy.IPC;

internal static class SubscriptionManager
{
    private static readonly Type[] IpcTypes = Assembly.GetExecutingAssembly()
        .GetTypes()
        .Where(type => type.GetCustomAttribute<IPCAttribute>() != null)
        .ToArray();

    private static readonly Dictionary<string, EzIPCDisposalToken[]> InitializedIpcs = new();
    private static int _subscribeTick;

    internal static bool IsInitialized(string plugin) =>
        InitializedIpcs.ContainsKey(plugin) && IsLoaded(plugin);

    internal static bool IsLoaded(string pluginName) =>
        Svc.PluginInterface.InstalledPlugins.Any(x => x.InternalName == pluginName && x.IsLoaded);

    internal static void Subscribe(IFramework framework)
    {
        try
        {
            var checkUnloadsOnly = InitializedIpcs.Count == IpcTypes.Length &&
                                   ++_subscribeTick % 60 != 0;

            foreach (var type in IpcTypes)
            {
                var attr = type.GetCustomAttribute<IPCAttribute>()!;
                if (!IsInitialized(attr.Name))
                {
                    if (checkUnloadsOnly || !IsLoaded(attr.Name))
                    {
                        continue;
                    }

                    InitializedIpcs[attr.Name] = EzIPC.Init(type, attr.Name);
                }
                else if (!IsLoaded(attr.Name))
                {
                    foreach (var token in InitializedIpcs[attr.Name])
                    {
                        token.Dispose();
                    }

                    InitializedIpcs.Remove(attr.Name);
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
}
