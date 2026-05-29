using Saucy.Framework;

namespace Saucy.OutOnALimb;

public class OutOnALimbModule : Module
{
    public override string Name => "Out on a Limb";

    public override void Enable() => P.LimbManager.Cfg.EnableLimb = true;

    public override void Disable() => P.LimbManager.Cfg.EnableLimb = false;
}
