using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public partial class TriadGameScreenMemory
{
    private void UpdateAvailableRedCards(TriadDeckInstanceScreen redDeck,
        TriadCard[] screenCardsRed, TriadCard[] screenCardsBlue, TriadCard[] screenBoard,
        TriadCard[] prevCardsBlue, TriadCardInstance[] prevBoard, bool bContinuePrevState)
    {
        var hiddenCardId = TriadCardDB.Get().hiddenCard.Id;
        var numVisibleCards = deckRed.cards.Length;

        redDeck.numPlaced = 0;
        if (!bContinuePrevState)
        {
            redDeck.numUnknownPlaced = 0;
        }

        var maxUnknownToUse = redDeck.cards.Length - redDeck.deck.knownCards.Count;
        var firstUnknownPoolIdx = redDeck.cards.Length + redDeck.deck.knownCards.Count;
        if (redDeck.deck.unknownCardPool.Count > 0)
        {
            redDeck.unknownPoolMask = ((1 << redDeck.deck.unknownCardPool.Count) - 1) << firstUnknownPoolIdx;

            for (var Idx = 0; Idx < screenCardsRed.Length; Idx++)
            {
                if (screenCardsRed[Idx] == null)
                {
                    continue;
                }

                if (screenCardsRed[Idx].Id != hiddenCardId && redDeck.deck.unknownCardPool.Contains(screenCardsRed[Idx]))
                {
                    redDeck.unknownPoolMask |= (1 << Idx);
                }
            }
        }

        var allDeckAvailableMask = ((1 << (redDeck.deck.knownCards.Count + redDeck.deck.unknownCardPool.Count)) - 1) << numVisibleCards;

        var bCanCompareWithPrevData = (screenCardsRed.Length == redDeck.cards.Length) && (screenCardsBlue.Length == prevCardsBlue.Length) && (screenBoard.Length == prevBoard.Length);
        if (bCanCompareWithPrevData && !bContinuePrevState)
        {
            var numCardsOnBoard = 0;
            for (var Idx = 0; Idx < screenBoard.Length; Idx++)
            {
                if (screenBoard[Idx] != null)
                {
                    numCardsOnBoard++;
                }
            }

            if (numCardsOnBoard <= 1)
            {
                bCanCompareWithPrevData = true;
                prevBoard = new TriadCardInstance[screenBoard.Length];
                prevCardsBlue = new TriadCard[numVisibleCards];
                deckRed.cards = new TriadCard[numVisibleCards];
                deckRed.availableCardMask = allDeckAvailableMask;
                deckRed.numPlaced = 0;
                deckRed.numUnknownPlaced = 0;
            }
            else
            {
                bCanCompareWithPrevData = false;
            }
        }

        if (bCanCompareWithPrevData)
        {
            List<int> usedCardsIndices = [];
            List<TriadCard> usedCardsOther = [];

            var numKnownOnHand = 0;
            var numUnknownOnHand = 0;
            var numHidden = 0;
            var numOnHand = 0;
            for (var Idx = 0; Idx < deckRed.cards.Length; Idx++)
            {
                if (screenCardsRed[Idx] == null)
                {
                    var prevCard = deckRed.cards[Idx];
                    if (prevCard != null && prevCard.Id != hiddenCardId)
                    {
                        usedCardsIndices.Add(Idx);
                    }

                    deckRed.availableCardMask &= ~(1 << Idx);
                    deckRed.numPlaced++;
                }
                else
                {
                    if (screenCardsRed[Idx].Id != hiddenCardId)
                    {
                        var bIsUnknown = (deckRed.unknownPoolMask & (1 << Idx)) != 0;
                        numUnknownOnHand += bIsUnknown ? 1 : 0;
                        numKnownOnHand += bIsUnknown ? 0 : 1;
                        numOnHand++;
                        deckRed.availableCardMask |= (1 << Idx);

                        var knownCardIdx = deckRed.deck.knownCards.IndexOf(screenCardsRed[Idx]);
                        var unknownCardIdx = deckRed.deck.unknownCardPool.IndexOf(screenCardsRed[Idx]);
                        if (knownCardIdx >= 0)
                        {
                            deckRed.availableCardMask &= ~(1 << (knownCardIdx + deckRed.cards.Length));
                        }
                        else if (unknownCardIdx >= 0)
                        {
                            deckRed.availableCardMask &= ~(1 << (unknownCardIdx + deckRed.cards.Length + deckRed.deck.knownCards.Count));
                        }
                    }
                    else
                    {
                        // face-down card: do NOT mark the slot playable (the placeholder card is 0/0/0/0)
                        // and do NOT count it as unknown-on-hand; red's options stay modeled by the
                        // real card pool (mask bits >= numVisibleCards), matching upstream FFTriadBuddy
                        numHidden++;
                    }
                }
            }

            for (var Idx = 0; Idx < prevCardsBlue.Length; Idx++)
            {
                if ((prevCardsBlue[Idx] != null) && (screenCardsBlue[Idx] == null))
                {
                    usedCardsOther.Add(prevCardsBlue[Idx]);
                }
            }

            for (var Idx = 0; Idx < prevBoard.Length; Idx++)
            {
                var testCard = screenBoard[Idx];
                if ((prevBoard[Idx] == null || prevBoard[Idx].card == null) && (testCard != null))
                {
                    var testCardIdx = deckRed.GetCardIndex(testCard);
                    if (!usedCardsOther.Contains(testCard) && (testCardIdx >= 0))
                    {
                        usedCardsIndices.Add(testCardIdx);
                    }
                }
            }

            Array.Copy(screenCardsRed, deckRed.cards, 5);

            for (var Idx = 0; Idx < usedCardsIndices.Count; Idx++)
            {
                var cardMask = 1 << usedCardsIndices[Idx];
                deckRed.availableCardMask &= ~cardMask;

                var bIsUnknownPool = (deckRed.unknownPoolMask & cardMask) != 0;
                if (bIsUnknownPool)
                {
                    deckRed.numUnknownPlaced++;
                }
            }

            if ((numHidden == 0) && ((numOnHand + deckRed.numPlaced) == numVisibleCards))
            {
                deckRed.availableCardMask &= (1 << numVisibleCards) - 1;
            }
            else if ((deckRed.numUnknownPlaced + numUnknownOnHand) >= maxUnknownToUse ||
                     ((numKnownOnHand >= (numVisibleCards - maxUnknownToUse)) && (numHidden == 0)))
            {
                deckRed.availableCardMask &= (1 << (numVisibleCards + deckRed.deck.knownCards.Count)) - 1;
            }
        }
        else
        {
            deckRed.UpdateAvailableCards(screenCardsRed);
            deckRed.availableCardMask = allDeckAvailableMask;
        }
    }

    public void UpdatePlayerDeck(TriadDeck playerDeck) => playerDeckPattern = [.. playerDeck.knownCards];
}
