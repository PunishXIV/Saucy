using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;
namespace Saucy.AirForce;

[StructLayout(LayoutKind.Explicit, Size = 0x38)]
internal unsafe struct AgentRideShooting
{
    [FieldOffset(0x00)] public AgentInterface AgentInterface;

    [FieldOffset(0x30)] public RideShootingHandler* Handler;

    internal static AgentRideShooting* TryGet()
    {
        var module = AgentModule.Instance();
        if (module == null)
        {
            return null;
        }

        return (AgentRideShooting*)module->GetAgentByInternalId(AgentId.RideShooting);
    }
}

[StructLayout(LayoutKind.Explicit, Size = 0xC78)]
internal struct RideShootingHandler
{
    [FieldOffset(0xC70)] public float AimScreenX;
    [FieldOffset(0xC74)] public float AimScreenY;
}

[StructLayout(LayoutKind.Explicit)]
internal struct AddonRideShooting
{
    [FieldOffset(0)] public AtkUnitBase AtkUnitBase;
}
