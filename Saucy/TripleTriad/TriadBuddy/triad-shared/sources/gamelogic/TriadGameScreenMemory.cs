using MgAl2O4.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFTriadBuddy;

public class TriadGameScreenMemory
{
    [Flags]
    public enum EUpdateFlags
    {
        None = 0,
        Modifiers = 1,
        Board = 2,
        RedDeck = 4,
        BlueDeck = 8,
        SwapWarning = 16,
        SwapHints = 32,
    }

    public TriadGameSimulationState gameState;
    public TriadGameSolver gameSolver;
    public TriadDeckInstanceScreen deckBlue;
    public TriadDeckInstanceScreen deckRed;
    private readonly List<TriadCard[]> blueDeckHistory;
    private TriadCard[] playerDeckPattern;
    private TriadNpc lastScanNpc;
    private bool bHasSwapRule;
    private bool bHasRestartRule;
    private bool bHasOpenRule;
    public int swappedBlueCardIdx;
    public bool logScan;

    public TriadGameScreenMemory()
    {
        gameSolver = new TriadGameSolver();
        gameState = new TriadGameSimulationState();
        deckBlue = new TriadDeckInstanceScreen();
        deckRed = new TriadDeckInstanceScreen();
        blueDeckHistory = [];
        bHasSwapRule = false;
        swappedBlueCardIdx = -1;
        lastScanNpc = null;
        logScan = true;
    }

    public EUpdateFlags OnNewScan(ScannerTriad.GameState screenGame, TriadNpc selectedNpc)
    {
        var updateFlags = EUpdateFlags.None;
        if (screenGame == null)
        {
            return updateFlags;
        }

        // check if game from screenshot can be continuation of cached one
        // is current state a continuation of last one?
        // ideally each blue turn is a capture until game resets = any card disappears from board
        // guess work for adding sense of persistence to screen decks
        var bContinuesPrevState = (deckRed.deck == selectedNpc.Deck) && (lastScanNpc == selectedNpc);
        if (bContinuesPrevState)
        {
            for (var Idx = 0; Idx < gameState.board.Length; Idx++)
            {
                var bWasNull = gameState.board[Idx] == null;
                var bIsNull = screenGame.board[Idx] == null;

                if (!bWasNull && bIsNull)
                {
                    bContinuesPrevState = false;
                    if (logScan) { Logger.WriteLine("Can't continue previous state: board[" + Idx + "] disappeared "); }
                }
            }
        }
        else
        {
            if (logScan) { Logger.WriteLine("Can't continue previous state: npc changed"); }
        }

        var bModsChanged = (gameSolver.simulation.modifiers.Count != screenGame.mods.Count) || !gameSolver.simulation.modifiers.All(screenGame.mods.Contains);
        if (bModsChanged)
        {
            bHasSwapRule = false;
            bHasRestartRule = false;
            bHasOpenRule = false;
            gameSolver.simulation.modifiers.Clear();
            gameSolver.simulation.modifiers.AddRange(screenGame.mods);
            gameSolver.simulation.specialRules = ETriadGameSpecialMod.None;
            gameSolver.simulation.modFeatures = TriadGameModifier.EFeature.None;
            foreach (var mod in gameSolver.simulation.modifiers)
            {
                gameSolver.simulation.modFeatures |= mod.GetFeatures();

                // swap rule is bad for screenshot based analysis, no good way of telling what is out of place
                if (mod is TriadGameModifierSwap)
                {
                    bHasSwapRule = true;
                }
                else if (mod is TriadGameModifierSuddenDeath)
                {
                    bHasRestartRule = true;
                }
                else if (mod is TriadGameModifierAllOpen)
                {
                    bHasOpenRule = true;
                }
            }

            updateFlags |= EUpdateFlags.Modifiers;
            bContinuesPrevState = false;
            if (logScan) { Logger.WriteLine("Can't continue previous state: modifiers changed"); }

            deckRed.SetSwappedCard(null, -1);
        }

        // wipe blue deck history when playing with new npc (or region modifiers have changed)
        var bRemoveBlueHistory = bModsChanged || (lastScanNpc != selectedNpc);
        if (bRemoveBlueHistory)
        {
            blueDeckHistory.Clear();
            if (bHasSwapRule && logScan) { Logger.WriteLine("Blue deck history cleared"); }
        }

        var bRedDeckChanged = (lastScanNpc != selectedNpc) || !IsDeckMatching(deckRed, screenGame.redDeck) || (deckRed.deck != selectedNpc.Deck);
        if (bRedDeckChanged)
        {
            updateFlags |= EUpdateFlags.RedDeck;
            deckRed.deck = selectedNpc.Deck;
            lastScanNpc = selectedNpc;

            // needs to happen before any changed to board (gameState)
            UpdateAvailableRedCards(deckRed, screenGame.redDeck, screenGame.blueDeck,
                screenGame.board, deckBlue.cards, gameState.board, bContinuesPrevState);
        }

        var bBlueDeckChanged = !IsDeckMatching(deckBlue, screenGame.blueDeck);
        if (bBlueDeckChanged)
        {
            updateFlags |= EUpdateFlags.BlueDeck;
            deckBlue.UpdateAvailableCards(screenGame.blueDeck);
        }

        gameState.state = ETriadGameState.InProgressBlue;
        gameState.deckBlue = deckBlue;
        gameState.deckRed = deckRed;
        gameState.numCardsPlaced = 0;
        gameState.forcedCardIdx = deckBlue.GetCardIndex(screenGame.forcedBlueCard);

        var bBoardChanged = false;
        for (var Idx = 0; Idx < gameState.board.Length; Idx++)
        {
            var bWasNull = gameState.board[Idx] == null;
            var bIsNull = screenGame.board[Idx] == null;

            if (bWasNull && !bIsNull)
            {
                bBoardChanged = true;
                gameState.board[Idx] = new TriadCardInstance(screenGame.board[Idx], screenGame.boardOwner[Idx]);
                if (logScan) { Logger.WriteLine("  board update: [" + Idx + "] " + gameState.board[Idx].owner + ": " + gameState.board[Idx].card.Name.GetCodeName()); }
            }
            else if (!bWasNull && bIsNull)
            {
                bBoardChanged = true;
                gameState.board[Idx] = null;
            }
            else if (!bWasNull && !bIsNull)
            {
                if (gameState.board[Idx].owner != screenGame.boardOwner[Idx] ||
                    gameState.board[Idx].card != screenGame.board[Idx])
                {
                    bBoardChanged = true;
                    gameState.board[Idx] = new TriadCardInstance(screenGame.board[Idx], screenGame.boardOwner[Idx]);
                }
            }

            gameState.numCardsPlaced += (gameState.board[Idx] != null) ? 1 : 0;
        }

        if (bBoardChanged)
        {
            updateFlags |= EUpdateFlags.Board;

            foreach (var mod in gameSolver.simulation.modifiers)
            {
                mod.OnScreenUpdate(gameState);
            }
        }

        // start of game, do additional checks when swap rule is active
        if (bHasSwapRule && gameState.numCardsPlaced <= 1)
        {
            updateFlags |= DetectSwapOnGameStart();
        }

        if (logScan)
        {
            Logger.WriteLine("OnNewScan> board:" + (bBoardChanged ? "changed" : "same") +
                ", blue:" + (bBlueDeckChanged ? "changed" : "same") +
                ", red:" + (bRedDeckChanged ? "changed" : "same") +
                ", mods:" + (bModsChanged ? "changed" : "same") +
                ", continuePrev:" + bContinuesPrevState +
                " => " + ((updateFlags != EUpdateFlags.None) ? ("UPDATE[" + updateFlags + "]") : "skip"));
        }

        return updateFlags;
    }

    private bool IsDeckMatching(TriadDeckInstanceScreen deckInstance, TriadCard[] cards)
    {
        var bIsMatching = false;
        if ((deckInstance != null) && (cards != null) && (deckInstance.cards.Length >= cards.Length))
        {
            bIsMatching = true;
            for (var Idx = 0; Idx < cards.Length; Idx++)
            {
                bIsMatching = bIsMatching && (cards[Idx] == deckInstance.cards[Idx]);
            }
        }

        return bIsMatching;
    }

    private void UpdateAvailableRedCards(TriadDeckInstanceScreen redDeck,
        TriadCard[] screenCardsRed, TriadCard[] screenCardsBlue, TriadCard[] screenBoard,
        TriadCard[] prevCardsBlue, TriadCardInstance[] prevBoard, bool bContinuePrevState)
    {
        var bDebugMode = false;
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
                if ((screenCardsRed[Idx] != null) && (screenCardsRed[Idx].Id != hiddenCardId) && redDeck.deck.unknownCardPool.Contains(screenCardsRed[Idx]))
                {
                    redDeck.unknownPoolMask |= (1 << Idx);
                }
            }
        }

        var allDeckAvailableMask = ((1 << (redDeck.deck.knownCards.Count + redDeck.deck.unknownCardPool.Count)) - 1) << numVisibleCards;

        var bCanCompareWithPrevData = (screenCardsRed.Length == redDeck.cards.Length) && (screenCardsBlue.Length == prevCardsBlue.Length) && (screenBoard.Length == prevBoard.Length);
        if (bCanCompareWithPrevData && !bContinuePrevState)
        {
            // special case: 1st turn

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

        if (bDebugMode)
        {
            Logger.WriteLine("Red deck update, diff mode check... " +
                "bContinuePrevState:" + bContinuePrevState +
                ", cards(screen:" + screenCardsRed.Length + ", prev:" + deckRed.cards.Length + ")=" + ((screenCardsRed.Length == deckRed.cards.Length) ? "ok" : "nope") +
                ", other(screen:" + screenCardsBlue.Length + ", prev:" + prevCardsBlue.Length + ")=" + ((screenCardsBlue.Length == prevCardsBlue.Length) ? "ok" : "nope") +
                ", board(screen:" + screenBoard.Length + ", prev:" + prevBoard.Length + ")=" + ((screenBoard.Length == prevBoard.Length) ? "ok" : "nope"));
        }

        if (bCanCompareWithPrevData)
        {
            // create diffs, hopefully prev state comes from last turn and is just 2 cards away
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
                    if ((prevCard != null) && (prevCard.Id != hiddenCardId))
                    {
                        if (bDebugMode) { Logger.WriteLine("  card[" + Idx + "]:" + prevCard.Name.GetCodeName() + " => mark as used, disappeared from prev state"); }
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

                        if (bDebugMode)
                        {
                            var cardOb = screenCardsRed[Idx];
                            Logger.WriteLine(" card[" + Idx + "]:" + (cardOb != null ? cardOb.Name.GetCodeName() : "??") +
                                " => numUnknown:" + numUnknownOnHand + ", numKnown:" + numKnownOnHand + ", numHidden:" + numHidden);
                        }
                    }
                    else
                    {
                        numHidden++;
                    }
                }
            }

            for (var Idx = 0; Idx < prevCardsBlue.Length; Idx++)
            {
                if ((prevCardsBlue[Idx] != null) && (screenCardsBlue[Idx] == null))
                {
                    usedCardsOther.Add(prevCardsBlue[Idx]);
                    if (bDebugMode) { Logger.WriteLine("  blue[" + Idx + "]:" + prevCardsBlue[Idx].Name.GetCodeName() + " => mark as used"); }
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
                        if (bDebugMode) { Logger.WriteLine("  card[" + testCardIdx + "]:" + testCard.Name.GetCodeName() + " => mark as used, appeared on board[" + Idx + "], not used by blue"); }
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

                if (bDebugMode)
                {
                    var cardOb = deckRed.GetCard(usedCardsIndices[Idx]);
                    Logger.WriteLine(" card[" + usedCardsIndices[Idx] + "]:" + (cardOb != null ? cardOb.Name.GetCodeName() : "??") + " => used");
                }
            }

            if ((numHidden == 0) && ((numOnHand + deckRed.numPlaced) == numVisibleCards))
            {
                deckRed.availableCardMask &= (1 << numVisibleCards) - 1;
                if (bDebugMode) { Logger.WriteLine("   all cards are on hand and visible"); }
            }
            else if ((deckRed.numUnknownPlaced + numUnknownOnHand) >= maxUnknownToUse ||
                ((numKnownOnHand >= (numVisibleCards - maxUnknownToUse)) && (numHidden == 0)))
            {
                deckRed.availableCardMask &= (1 << (numVisibleCards + deckRed.deck.knownCards.Count)) - 1;

                if (bDebugMode)
                {
                    Logger.WriteLine("   removing all unknown cards, numUnknownPlaced:" + deckRed.numUnknownPlaced +
                        ", numUnknownOnHand:" + numUnknownOnHand + ", numKnownOnHand:" + numKnownOnHand +
                        ", numHidden:" + numHidden + ", maxUnknownToUse:" + maxUnknownToUse);
                }
            }
        }
        else
        {
            // TriadDeckInstanceScreen is mostly stateless (created from scratch on screen capture)
            // this makes guessing which cards were placed hard, especially when there's no good
            // history data to compare with. 
            // Ignore board data here, cards could be placed by blue and are still available for red deck

            deckRed.UpdateAvailableCards(screenCardsRed);
            deckRed.availableCardMask = allDeckAvailableMask;
        }

        if (bDebugMode)
        {
            redDeck.LogAvailableCards("Red deck");
        }
    }

    public void UpdatePlayerDeck(TriadDeck playerDeck)
    {
        playerDeckPattern = [.. playerDeck.knownCards];
    }

    private bool FindSwappedCard(TriadCard[] screenCards, TriadCard[] expectedCards, TriadDeckInstanceScreen otherDeck, out int swappedCardIdx, out int swappedOtherIdx, out TriadCard swappedCard)
    {
        swappedCardIdx = -1;
        swappedOtherIdx = -1;
        swappedCard = null;
        TriadCard swappedBlueCard = null;

        var numDiffs = 0;
        var numPotentialSwaps = 0;
        for (var Idx = 0; Idx < screenCards.Length; Idx++)
        {
            if ((screenCards[Idx] != expectedCards[Idx]) && (screenCards[Idx] != null))
            {
                numDiffs++;
                swappedCardIdx = Idx;
                swappedOtherIdx = otherDeck.GetCardIndex(screenCards[Idx]);
                swappedBlueCard = screenCards[Idx];
                swappedCard = expectedCards[Idx];
                Logger.WriteLine("FindSwappedCard[" + Idx + "]: screen:" + screenCards[Idx].Name.GetCodeName() + ", expected:" + expectedCards[Idx].Name.GetCodeName() + ", redIdxScreen:" + swappedOtherIdx);

                if (swappedOtherIdx >= 0)
                {
                    numPotentialSwaps++;
                }
            }
        }

        var bHasSwapped = (numDiffs == 1) && (numPotentialSwaps == 1);
        Logger.WriteLine("FindSwappedCard: blue[" + swappedCardIdx + "]:" + (swappedBlueCard != null ? swappedBlueCard.Name.GetCodeName() : "??") +
            " <=> red[" + swappedOtherIdx + "]:" + (swappedCard != null ? swappedCard.Name.GetCodeName() : "??") +
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
                // find in source deck, not in instance
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
                    // find in source deck, not in instance
                    var cardIdx = otherDeck.deck.GetCardIndex(board[Idx].card);
                    if (cardIdx < 0)
                    {
                        swappedCard = board[Idx].card;
                        swappedOtherIdx = 100;                  // something way outside, it's not going to be used directly as card was already placed

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
        Logger.WriteLine("FindSwappedCardVisible: blue[" + swappedCardIdx + "]:" + (swappedCardIdx >= 0 ? screenCards[swappedCardIdx].Name.GetCodeName() : "??") +
            " <=> red[" + swappedOtherIdx + "]:" + (swappedCard != null ? swappedCard.Name.GetCodeName() : "??") +
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

                Logger.WriteLine("FindCommonCards[" + SlotIdx + "]: " + bestSlotCard.Name.GetCodeName() + " x" + bestSlotCount + (bestSlotCount < 2 ? " => not enough to decide!" : ""));
                if (bestSlotCount >= 2)
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

        // store initial blue deck
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
        if (!bHasSwappedCard)
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
            // deck blue doesn't need updates, it already has all cards visible
            // deck red needs to know which card is not longer available and which one is new
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
        // sudden death: all red cards visible, not matching npc deck at all
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
