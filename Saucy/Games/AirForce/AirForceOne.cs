using Saucy.Framework;
using System.Runtime.InteropServices;

namespace Saucy.AirForce;

public class AirForceOne : Module
{
    private const   string FireCachedTargetSignature = "48 8B C4 53 48 81 EC ?? ?? ?? ?? 0F 29 70 ?? 0F 57 C9 0F 29 78 ?? 48 8B D9 F3 0F 10 B9 ?? ?? ?? ?? BA FF FF 00 00";
    public override string Name => "Air Force One";

    public override void Enable()
    {
        Svc.Framework.Update += OnFrameworkUpdate;
        if (Svc.SigScanner.TryScanText(FireCachedTargetSignature, out var fireCachedTargetAddress))
        {
            AirForceAutomation.FireCachedTarget = Marshal.GetDelegateForFunctionPointer<AirForceAutomation.FireCachedTargetDelegate>(fireCachedTargetAddress);
        }
    }

    public override void Disable()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        AirForceAutomation.ClearRewardTracking();
    }

    private static void OnFrameworkUpdate(IFramework _) => AirForceAutomation.OnUpdate();
}
