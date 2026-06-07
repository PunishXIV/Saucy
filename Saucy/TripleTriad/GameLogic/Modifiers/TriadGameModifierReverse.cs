#nullable disable
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierReverse : TriadGameModifier
{
    public TriadGameModifierReverse()
    {
        RuleName = "Reverse";
        RuleIndex = 5;
        Features = EFeature.CaptureMath;
    }

    public override void OnCheckCaptureCardMath(TriadGameSimulationState gameData, int boardPos, int neiPos, int cardNum, int neiNum, ref bool isCaptured) => isCaptured = cardNum < neiNum;

    public override void OnScoreCard(TriadCard card, ref float score)
    {
        const float MaxSum = 40.0f;
        var numberSum = card.Sides[0] + card.Sides[1] + card.Sides[2] + card.Sides[3];
        score = 1.0f - (numberSum / MaxSum);
    }
}
