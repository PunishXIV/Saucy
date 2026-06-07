#nullable disable
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierDescension : TriadGameModifier
{
    public TriadGameModifierDescension()
    {
        RuleName = "Descension";
        RuleIndex = 10;
        Features = EFeature.CardPlaced | EFeature.PostCapture;
    }

    public override void OnCardPlaced(TriadGameSimulationState gameData, int boardPos)
    {
        var checkCard = gameData.board[boardPos];
        if (checkCard.card.Type != ETriadCardType.None)
        {
            var scoreMod = gameData.typeMods[(int)checkCard.card.Type];
            if (scoreMod != 0)
            {
                checkCard.scoreModifier = scoreMod;

                if (gameData.bDebugRules)
                {
                    Logger.WriteLine(">> " + RuleName + "! [" + boardPos + "] " + checkCard.card.Name + " is: " + ((scoreMod > 0) ? "+" : "") + scoreMod);
                }
            }
        }
    }

    public override void OnPostCaptures(TriadGameSimulationState gameData, int boardPos)
    {
        var checkCard = gameData.board[boardPos];
        if (checkCard.card.Type != ETriadCardType.None)
        {
            var scoreMod = checkCard.scoreModifier - 1;
            gameData.typeMods[(int)checkCard.card.Type] = scoreMod;

            for (var Idx = 0; Idx < gameData.board.Length; Idx++)
            {
                var otherCard = gameData.board[Idx];
                if ((otherCard != null) && (checkCard.card.Type == otherCard.card.Type))
                {
                    otherCard.scoreModifier = scoreMod;
                    if (gameData.bDebugRules)
                    {
                        Logger.WriteLine(">> " + RuleName + "! [" + Idx + "] " + otherCard.card.Name + " is: " + ((scoreMod > 0) ? "+" : "") + scoreMod);
                    }
                }
            }
        }
    }

    public override void OnScreenUpdate(TriadGameSimulationState gameData)
    {
        for (var Idx = 0; Idx < gameData.typeMods.Length; Idx++)
        {
            gameData.typeMods[Idx] = 0;
        }

        for (var Idx = 0; Idx < gameData.board.Length; Idx++)
        {
            var checkCard = gameData.board[Idx];
            if (checkCard != null && checkCard.card.Type != ETriadCardType.None)
            {
                gameData.typeMods[(int)checkCard.card.Type] -= 1;
            }
        }

        for (var Idx = 0; Idx < gameData.board.Length; Idx++)
        {
            var checkCard = gameData.board[Idx];
            if (checkCard != null && checkCard.card.Type != ETriadCardType.None)
            {
                checkCard.scoreModifier = gameData.typeMods[(int)checkCard.card.Type];
            }
        }
    }

    public override void OnScoreCard(TriadCard card, ref float score)
    {
        const float ScoreMult = 0.5f;
        score *= ScoreMult;

        var bNoType = card.Type == ETriadCardType.None;
        if (bNoType)
        {
            score += (1.0f - ScoreMult);
        }
    }
}
