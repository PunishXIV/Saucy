using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin;

namespace TriadBuddyPlugin;

internal class Service
{
    public static Plugin plugin = null!;
    public static IDalamudPluginInterface pluginInterface = null!;
    public static Configuration pluginConfig = null!;
    [PluginService] public static SigScanner sigScanner { get; private set; } = null!;
}
