using System.Linq;

namespace Saucy.TripleTriad;

internal static class TriadBuddyIntegration
{
    private const string PluginInternalName = "TriadBuddy";

    public static bool IsLoaded() =>
        Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == PluginInternalName && p.IsLoaded);
}
