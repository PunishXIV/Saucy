using Saucy.Framework;
namespace Saucy.OutOnALimb;

public class OutOnALimbModule : Module
{
    public override string Name => "Out on a Limb";

    public override void Enable() =>
        GoldSaucerArcadeLifecycle.OnModuleEnabled(GoldSaucerArcadeMachine.Limb);

    public override void Disable() =>
        GoldSaucerArcadeLifecycle.OnModuleDisabled(GoldSaucerArcadeMachine.Limb);
}
