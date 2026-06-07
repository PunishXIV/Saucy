using Saucy.Framework;
namespace Saucy.CuffACur;

public class CuffACurModule : Module
{
    public override string Name => "Cuff-a-Cur";

    public override void Enable() =>
        GoldSaucerArcadeLifecycle.OnModuleEnabled(GoldSaucerArcadeMachine.Cuff);

    public override void Disable() =>
        GoldSaucerArcadeLifecycle.OnModuleDisabled(GoldSaucerArcadeMachine.Cuff);
}
