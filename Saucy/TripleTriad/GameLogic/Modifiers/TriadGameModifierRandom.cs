#nullable disable
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierRandom : TriadGameModifier
{
    public TriadGameModifierRandom()
    {
        RuleName = "Random";
        RuleIndex = 14;
        SpecialMod = ETriadGameSpecialMod.RandomizeBlueDeck;
    }

    public static void StaticRandomized(TriadGameSimulationState gameData)
    {
        if (gameData.bDebugRules)
        {
            var DummyOb = new TriadGameModifierRandom();
            Logger.WriteLine(">> " + DummyOb.RuleName + "! blue deck:" + gameData.deckBlue);
        }
    }
}
