using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin;
using ECommons.DalamudServices;

namespace TriadBuddyPlugin;

internal class Service
{
    public static Plugin plugin = null!;
    public static IDalamudPluginInterface pluginInterface = null!;
    public static Configuration pluginConfig = null!;

    [PluginService] public static IDataManager dataManager => Svc.Data;

    [PluginService] public static ICommandManager commandManager => Svc.Commands;

    [PluginService] public static ISigScanner sigScanner => Svc.SigScanner;

    [PluginService] public static IFramework framework => Svc.Framework;

    [PluginService] public static IGameGui gameGui => Svc.GameGui;

    [PluginService] public static ITextureProvider textureProvider => Svc.Texture;

    [PluginService] public static IPluginLog logger => Svc.Log;
}