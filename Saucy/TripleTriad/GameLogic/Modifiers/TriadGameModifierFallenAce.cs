#nullable disable
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierFallenAce : TriadGameModifier
{
    public TriadGameModifierFallenAce()
    {
        RuleName = "Fallen Ace";
        RuleIndex = 6;
        Features = EFeature.CaptureWeights;
    }

    public override void OnCheckCaptureCardWeights(TriadGameSimulationState gameData, int boardPos, int neiPos, bool isReverseActive, ref int cardNum, ref int neiNum)
    {
        if (isReverseActive)
        {
            if ((cardNum == 10) && (neiNum == 1))
            {
                cardNum = 0;
            }
        }
        else
        {
            if ((cardNum == 1) && (neiNum == 10))
            {
                neiNum = 0;
            }
        }
    }
}
