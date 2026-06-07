#nullable disable
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public partial class TriadGameScreenMemory
{
    private bool FindSwappedCard(TriadCard[] screenCards, TriadCard[] expectedCards, TriadDeckInstanceScreen otherDeck, out int swappedCardIdx, out int swappedOtherIdx, out TriadCard swappedCard)
    {
        swappedCardIdx = -1;
        swappedOtherIdx = -1;
        swappedCard = null;

        if (screenCards == null || expectedCards == null || otherDeck == null)
        {
            return false;
        }

        TriadCard swappedBlueCard = null;

        var numDiffs = 0;
        var numPotentialSwaps = 0;
        var len = Math.Min(screenCards.Length, expectedCards.Length);
        for (var Idx = 0; Idx < len; Idx++)
        {
            var screenCard = screenCards[Idx];
            var expectedCard = expectedCards[Idx];
            if (screenCard == expectedCard || screenCard == null)
            {
                continue;
            }

            numDiffs++;
            swappedCardIdx = Idx;
            swappedOtherIdx = otherDeck.GetCardIndex(screenCard);
            swappedBlueCard = screenCard;
            swappedCard = expectedCard;
            Logger.WriteLine("FindSwappedCard[" + Idx + "]: screen:" + screenCard.Name + ", expected:" + (expectedCard?.Name ?? "??") + ", redIdxScreen:" + swappedOtherIdx);

            if (swappedOtherIdx >= 0)
            {
                numPotentialSwaps++;
            }
        }

        var bHasSwapped = (numDiffs == 1) && (numPotentialSwaps == 1);
        Logger.WriteLine("FindSwappedCard: blue[" + swappedCardIdx + "]:" + (swappedBlueCard != null ? swappedBlueCard.Name : "??") +
                         " <=> red[" + swappedOtherIdx + "]:" + (swappedCard != null ? swappedCard.Name : "??") +
                         ", diffs:" + numDiffs + ", potentialSwaps:" + numPotentialSwaps +
                         " => " + (bHasSwapped ? "SWAP" : "ignore"));

        return bHasSwapped;
    }

    private bool FindSwappedCardVisible(TriadCard[] screenCards, TriadCardInstance[] board, TriadDeckInstanceScreen otherDeck, out int swappedCardIdx, out int swappedOtherIdx, out TriadCard swappedCard)
    {
        swappedCardIdx = -1;
        swappedOtherIdx = -1;
        swappedCard = null;

        var numDiffs = 0;
        var numOnHand = 0;

        var hiddenCardId = TriadCardDB.Get().hiddenCard.Id;
        for (var Idx = 0; Idx < otherDeck.cards.Length; Idx++)
        {
            if (otherDeck.cards[Idx] != null && otherDeck.cards[Idx].Id != hiddenCardId)
            {
                var cardIdx = otherDeck.deck.GetCardIndex(otherDeck.cards[Idx]);
                if (cardIdx < 0)
                {
                    swappedOtherIdx = Idx;
                    swappedCard = otherDeck.cards[Idx];
                    for (var ScreenIdx = 0; ScreenIdx < screenCards.Length; ScreenIdx++)
                    {
                        cardIdx = otherDeck.deck.GetCardIndex(screenCards[ScreenIdx]);
                        if (cardIdx >= 0)
                        {
                            swappedCardIdx = ScreenIdx;
                            numDiffs++;
                        }
                    }
                }
            }

            numOnHand += (otherDeck.cards[Idx] != null) ? 1 : 0;
        }

        var bBoardMode = false;
        if (numOnHand < screenCards.Length)
        {
            for (var Idx = 0; Idx < board.Length; Idx++)
            {
                if (board[Idx] != null && board[Idx].owner == ETriadCardOwner.Red)
                {
                    var cardIdx = otherDeck.deck.GetCardIndex(board[Idx].card);
                    if (cardIdx < 0)
                    {
                        swappedCard = board[Idx].card;
                        swappedOtherIdx = 100;

                        for (var ScreenIdx = 0; ScreenIdx < screenCards.Length; ScreenIdx++)
                        {
                            cardIdx = otherDeck.deck.GetCardIndex(screenCards[ScreenIdx]);
                            if (cardIdx >= 0)
                            {
                                swappedCardIdx = ScreenIdx;
                                bBoardMode = true;
                                numDiffs++;
                            }
                        }
                    }
                }
            }
        }

        var bHasSwapped = (numDiffs == 1);
        var swappedBlueName = swappedCardIdx >= 0 && swappedCardIdx < screenCards.Length && screenCards[swappedCardIdx] != null
            ? screenCards[swappedCardIdx].Name
            : "??";
        Logger.WriteLine("FindSwappedCardVisible: blue[" + swappedCardIdx + "]:" + swappedBlueName +
                         " <=> red[" + swappedOtherIdx + "]:" + (swappedCard != null ? swappedCard.Name : "??") +
                         ", boardMode:" + bBoardMode + ", diffs:" + numDiffs + " => " + (bHasSwapped ? "SWAP" : "ignore"));

        return bHasSwapped;
    }

    private TriadCard[] FindCommonCards(List<TriadCard[]> deckHistory)
    {
        TriadCard[] result = null;
        if (deckHistory.Count > 1)
        {
            result = new TriadCard[deckHistory[0].Length];
            for (var SlotIdx = 0; SlotIdx < result.Length; SlotIdx++)
            {
                Dictionary<TriadCard, int> slotCounter = [];
                TriadCard bestSlotCard = null;
                var bestSlotCount = 0;

                for (var HistoryIdx = 0; HistoryIdx < deckHistory.Count; HistoryIdx++)
                {
                    var testCard = deckHistory[HistoryIdx][SlotIdx];
                    if (testCard == null)
                    {
                        continue;
                    }

                    if (slotCounter.ContainsKey(testCard))
                    {
                        slotCounter[testCard] += 1;
                    }
                    else
                    {
                        slotCounter.Add(testCard, 1);
                    }

                    if (slotCounter[testCard] > bestSlotCount)
                    {
                        bestSlotCount = slotCounter[testCard];
                        bestSlotCard = testCard;
                    }
                }

                Logger.WriteLine("FindCommonCards[" + SlotIdx + "]: " + (bestSlotCard?.Name ?? "??") + " x" + bestSlotCount + (bestSlotCount < 2 ? " => not enough to decide!" : ""));
                if (bestSlotCount >= 2 && bestSlotCard != null)
                {
                    result[SlotIdx] = bestSlotCard;
                }
                else
                {
                    result = null;
                    break;
                }
            }
        }

        return result;
    }

    private EUpdateFlags DetectSwapOnGameStart()
    {
        var updateFlags = EUpdateFlags.None;

        deckRed.SetSwappedCard(null, -1);

        for (var Idx = 0; Idx < deckBlue.cards.Length; Idx++)
        {
            if (deckBlue.cards[Idx] == null)
            {
                Logger.WriteLine("DetectSwapOnGameStart: found empty blue card, skipping");
                return updateFlags;
            }
        }

        var bDetectedSuddenDeath = bHasRestartRule && IsSuddenDeathRestart(deckRed);
        if (bDetectedSuddenDeath)
        {
            Logger.WriteLine(">> ignore swap checks");
            return updateFlags;
        }

        if (playerDeckPattern == null)
        {
            var deckCards = new TriadCard[deckBlue.cards.Length];
            Array.Copy(deckBlue.cards, deckCards, deckCards.Length);
            if (Array.TrueForAll(deckCards, card => card != null))
            {
                playerDeckPattern = deckCards;
            }
        }

        {
            if (blueDeckHistory.Count > 10)
            {
                blueDeckHistory.RemoveAt(0);
            }

            var copyCards = new TriadCard[deckBlue.cards.Length];
            Array.Copy(deckBlue.cards, copyCards, copyCards.Length);

            blueDeckHistory.Add(copyCards);
            Logger.WriteLine("Storing blue deck at[" + blueDeckHistory.Count + "]: " + deckBlue);
        }

        var bHasSwappedCard = FindSwappedCardVisible(deckBlue.cards, gameState.board, deckRed, out var blueSwappedCardIdx, out var redSwappedCardIdx, out var blueSwappedCard);
        if (!bHasSwappedCard && playerDeckPattern != null)
        {
            bHasSwappedCard = FindSwappedCard(deckBlue.cards, playerDeckPattern, deckRed, out blueSwappedCardIdx, out redSwappedCardIdx, out blueSwappedCard);
            if (!bHasSwappedCard)
            {
                var commonCards = FindCommonCards(blueDeckHistory);
                if (commonCards != null)
                {
                    bHasSwappedCard = FindSwappedCard(deckBlue.cards, commonCards, deckRed, out blueSwappedCardIdx, out redSwappedCardIdx, out blueSwappedCard);
                }
            }
        }

        if (bHasSwappedCard)
        {
            deckRed.SetSwappedCard(blueSwappedCard, redSwappedCardIdx);
            swappedBlueCardIdx = blueSwappedCardIdx;
            updateFlags |= EUpdateFlags.SwapHints;
        }
        else
        {
            swappedBlueCardIdx = -1;
            updateFlags |= EUpdateFlags.SwapWarning;
        }

        return updateFlags;
    }

    private bool IsSuddenDeathRestart(TriadDeckInstanceScreen deck)
    {
        var numMismatchedCards = 0;
        var numVisibleCards = 0;
        var hiddenCardId = TriadCardDB.Get().hiddenCard.Id;

        for (var Idx = 0; Idx < deck.cards.Length; Idx++)
        {
            var npcCardIdx = deck.deck.GetCardIndex(deck.cards[Idx]);
            if (npcCardIdx < 0)
            {
                numMismatchedCards++;
            }

            if (deck.cards[Idx] != null && deck.cards[Idx].Id != hiddenCardId)
            {
                numVisibleCards++;
            }
        }

        var bHasOpenAndMismatched = (numVisibleCards >= 4 && numMismatchedCards > 1);
        var bHasOpenAndShouldnt = (numVisibleCards >= 4 && !bHasOpenRule);

        Logger.WriteLine("IsSuddenDeathRestart? numMismatchedCards:" + numMismatchedCards + ", numVisibleCards:" + numVisibleCards);
        return bHasOpenAndMismatched || bHasOpenAndShouldnt;
    }
}
