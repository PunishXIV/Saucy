using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;
using static ECommons.GenericHelpers;

namespace Saucy.TripleTriad.UI;

internal static unsafe class TriadLocalClientStructs
{
    public static bool TryGetRequest(out AddonRequest* addon, bool requireVisible = true) =>
        TryGetVisible("TripleTriadRequest", out addon, requireVisible);

    public static bool TryGetSelDeck(out AddonTripleTriadSelDeck* addon, bool requireVisible = true) =>
        TryGetVisible("TripleTriadSelDeck", out addon, requireVisible);

    public static bool TryGetResult(out AddonTripleTriadResult* addon, bool requireVisible = true) =>
        TryGetVisible("TripleTriadResult", out addon, requireVisible);

    public static bool TryGetBoard(out AddonTripleTriad* addon, bool requireVisible = true)
    {
        if (!TryGetAddonByName("TripleTriad", out addon))
        {
            return false;
        }

        return !requireVisible || addon->AtkUnitBase.IsVisible;
    }

    public static AgentTripleTriad* TryGetAgent() => AgentTripleTriad.TryGet();

    private static bool TryGetVisible<T>(string addonName, out T* addon, bool requireVisible)
    where T : unmanaged
    {
        if (!TryGetAddonByName(addonName, out addon))
        {
            return false;
        }

        if (!requireVisible)
        {
            return true;
        }

        return ((AtkUnitBase*)addon)->IsVisible;
    }
}

[StructLayout(LayoutKind.Explicit, Size = 0x1D0)]
internal unsafe struct AgentTripleTriad
{
    [FieldOffset(0x00)] public AgentInterface AgentInterface;
    [FieldOffset(0x1C8)] public uint RewardItemId;

    internal static AgentTripleTriad* TryGet()
    {
        var module = AgentModule.Instance();
        if (module == null)
        {
            return null;
        }

        return (AgentTripleTriad*)module->GetAgentByInternalId(AgentId.TripleTriad);
    }
}

[StructLayout(LayoutKind.Explicit)]
internal struct AddonTripleTriadSelDeck
{
    [FieldOffset(0)] public AtkUnitBase AtkUnitBase;
}

[StructLayout(LayoutKind.Explicit)]
internal struct AddonTripleTriadResult
{
    [FieldOffset(0)] public AtkUnitBase AtkUnitBase;
}
