using Saucy.Framework;

namespace Saucy.CuffACur;

public class CuffACurModule : Module
{
    public override string Name => "Cuff-a-Cur";

    public override void Enable() => CufModule.ModuleEnabled = true;

    public override void Disable() => CufModule.ModuleEnabled = false;
}
