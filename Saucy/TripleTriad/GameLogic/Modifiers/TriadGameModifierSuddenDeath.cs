#nullable disable
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierSuddenDeath : TriadGameModifier
{
    public TriadGameModifierSuddenDeath()
    {
        RuleName = "Sudden Death";
        RuleIndex = 4;
        bHasLastRedReminder = true;
        Features = EFeature.AllPlaced;
    }

    public override void OnAllCardsPlaced(TriadGameSimulationState gameData)
    {
        if (gameData.state == ETriadGameState.BlueDraw && gameData.numRestarts < 3)
        {
            if (gameData.deckBlue is TriadDeckInstanceManual deckBlueEx && gameData.deckRed is TriadDeckInstanceManual deckRedEx)
            {
                List<TriadCard> blueCards = [];
                List<TriadCard> redCards = [];
                List<TriadCard> redUnknownCards = [];
                var redCardsDebug = "";

                for (var Idx = 0; Idx < gameData.board.Length; Idx++)
                {
                    if (gameData.board[Idx].owner == ETriadCardOwner.Blue)
                    {
                        blueCards.Add(gameData.board[Idx].card);
                    }
                    else
                    {
                        redCards.Add(gameData.board[Idx].card);
                    }

                    gameData.board[Idx] = null;
                }

                if (deckBlueEx.numPlaced < deckRedEx.numPlaced)
                {
                    for (var Idx = 0; Idx < deckBlueEx.deck.knownCards.Count; Idx++)
                    {
                        var bIsAvailable = !deckBlueEx.IsPlaced(Idx);
                        if (bIsAvailable)
                        {
                            blueCards.Add(deckBlueEx.deck.knownCards[Idx]);
                            break;
                        }
                    }

                    gameData.state = ETriadGameState.InProgressBlue;
                }
                else
                {
                    for (var Idx = 0; Idx < deckRedEx.deck.knownCards.Count; Idx++)
                    {
                        var bIsAvailable = !deckRedEx.IsPlaced(Idx);
                        if (bIsAvailable)
                        {
                            redCards.Add(deckRedEx.deck.knownCards[Idx]);
                            redCardsDebug += deckRedEx.deck.knownCards[Idx].Name + ":K, ";
                            break;
                        }
                    }

                    if (redCards.Count < blueCards.Count)
                    {
                        for (var Idx = 0; Idx < deckRedEx.deck.unknownCardPool.Count; Idx++)
                        {
                            var cardIdx = Idx + deckRedEx.deck.knownCards.Count;
                            var bIsAvailable = !deckRedEx.IsPlaced(cardIdx);
                            if (bIsAvailable)
                            {
                                redUnknownCards.Add(deckRedEx.deck.unknownCardPool[Idx]);
                                redCardsDebug += deckRedEx.deck.unknownCardPool[Idx].Name + ":U, ";
                            }
                        }
                    }

                    gameData.state = ETriadGameState.InProgressRed;
                }

                gameData.deckBlue = new TriadDeckInstanceManual(new TriadDeck(blueCards));
                gameData.deckRed = new TriadDeckInstanceManual(new TriadDeck(redCards, redUnknownCards));
                gameData.numCardsPlaced = 0;
                gameData.numRestarts++;

                for (var Idx = 0; Idx < gameData.typeMods.Length; Idx++)
                {
                    gameData.typeMods[Idx] = 0;
                }

                if (gameData.bDebugRules)
                {
                    redCardsDebug = (redCardsDebug.Length > 0) ? redCardsDebug[..^2] : "(board only)";
                    var nextTurnOwner = (gameData.state == ETriadGameState.InProgressBlue) ? ETriadCardOwner.Blue : ETriadCardOwner.Red;
                    Logger.WriteLine(">> " + RuleName + "! next turn:" + nextTurnOwner + ", red:" + redCardsDebug);
                }
            }
        }
    }
}
