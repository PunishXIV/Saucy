using Saucy.Framework;
namespace Saucy.AirForce;

public class AirForceOne : Module
{
    public override string Name => "Air Force One";

    public override void Enable() => Svc.Framework.Update += OnFrameworkUpdate;

    public override void Disable()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        AirForceAutomation.ClearRewardTracking();
    }

    private static void OnFrameworkUpdate(IFramework _) => AirForceAutomation.OnUpdate();
}
