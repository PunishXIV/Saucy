#nullable disable
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierOrder : TriadGameModifier
{
    public TriadGameModifierOrder()
    {
        RuleName = "Order";
        RuleIndex = 11;
        bIsDeckOrderImportant = true;
        Features = EFeature.FilterNext;
    }

    public override void OnFilterNextCards(TriadGameSimulationState gameData, ref int allowedCardsMask)
    {
        if ((gameData.state == ETriadGameState.InProgressBlue) && (allowedCardsMask != 0))
        {
            var firstBlueIdx = gameData.deckBlue.GetFirstAvailableCardFast();
            allowedCardsMask = (firstBlueIdx < 0) ? 0 : (1 << firstBlueIdx);

            if (gameData.bDebugRules)
            {
                var firstBlueCard = gameData.deckBlue.GetCard(firstBlueIdx);
                Logger.WriteLine(">> " + RuleName + "! next card: " + (firstBlueCard != null ? firstBlueCard.Name : "none"));
            }
        }
    }
}
