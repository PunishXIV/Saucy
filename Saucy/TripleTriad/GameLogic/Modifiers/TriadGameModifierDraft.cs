#nullable disable
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierDraft : TriadGameModifier
{
    public TriadGameModifierDraft()
    {
        RuleName = "Draft";
        RuleIndex = 15;
        SpecialMod = ETriadGameSpecialMod.IgnoreOwnedCheck;
    }
}
