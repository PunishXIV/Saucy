#nullable disable
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierChaos : TriadGameModifier
{
    public TriadGameModifierChaos()
    {
        RuleName = "Chaos";
        RuleIndex = 12;
        SpecialMod = ETriadGameSpecialMod.BlueCardSelection;
    }
}
