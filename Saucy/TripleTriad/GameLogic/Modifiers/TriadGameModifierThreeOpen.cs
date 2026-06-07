#nullable disable
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierThreeOpen : TriadGameModifier
{
    public TriadGameModifierThreeOpen()
    {
        RuleName = "Three Open";
        RuleIndex = 3;
        SpecialMod = ETriadGameSpecialMod.SelectVisible3;
    }
}
