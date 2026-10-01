#nullable disable
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierNone : TriadGameModifier
{
    public TriadGameModifierNone()
    {
        RuleName = "None";
        RuleIndex = 0;
    }
}

public class TriadGameModifierRoulette : TriadGameModifier
{
    protected TriadGameModifier RuleInst;

    public TriadGameModifierRoulette()
    {
        RuleName = "Roulette";
        RuleIndex = 1;
        SpecialMod = ETriadGameSpecialMod.RandomizeRule;
    }

    public override string GetCodeName() => base.GetCodeName() + (RuleInst != null ? (" (" + RuleInst.GetCodeName() + ")") : "");
    public override string GetLocalizedName() => base.GetLocalizedName() + (RuleInst != null ? (" (" + RuleInst.GetLocalizedName() + ")") : "");
    public override bool AllowsCombo() => (RuleInst != null) ? RuleInst.AllowsCombo() : base.AllowsCombo();
    public override bool IsDeckOrderImportant() => (RuleInst != null) ? RuleInst.IsDeckOrderImportant() : base.IsDeckOrderImportant();
    public override ETriadGameSpecialMod GetSpecialRules() => base.GetSpecialRules() | ((RuleInst != null) ? RuleInst.GetSpecialRules() : ETriadGameSpecialMod.None);
    public override EFeature GetFeatures() => (RuleInst != null) ? RuleInst.GetFeatures() : EFeature.None;
    public override bool HasLastRedReminder() => (RuleInst != null) ? RuleInst.HasLastRedReminder() : base.HasLastRedReminder();

    public override void OnCardPlaced(TriadGameSimulationState gameData, int boardPos) => RuleInst?.OnCardPlaced(gameData, boardPos);

    public override void OnCheckCaptureNeis(TriadGameSimulationState gameData, int boardPos, int[] neiPos, List<int> captureList) => RuleInst?.OnCheckCaptureNeis(gameData, boardPos, neiPos, captureList);

    public override void OnCheckCaptureCardWeights(TriadGameSimulationState gameData, int boardPos, int neiPos, bool isReverseActive, ref int cardNum, ref int neiNum) => RuleInst?.OnCheckCaptureCardWeights(gameData, boardPos, neiPos, isReverseActive, ref cardNum, ref neiNum);

    public override void OnCheckCaptureCardMath(TriadGameSimulationState gameData, int boardPos, int neiPos, int cardNum, int neiNum, ref bool isCaptured) => RuleInst?.OnCheckCaptureCardMath(gameData, boardPos, neiPos, cardNum, neiNum, ref isCaptured);

    public override void OnPostCaptures(TriadGameSimulationState gameData, int boardPos) => RuleInst?.OnPostCaptures(gameData, boardPos);

    public override void OnAllCardsPlaced(TriadGameSimulationState gameData) => RuleInst?.OnAllCardsPlaced(gameData);

    public override void OnFilterNextCards(TriadGameSimulationState gameData, ref int allowedCardsMask) => RuleInst?.OnFilterNextCards(gameData, ref allowedCardsMask);

    public override void OnMatchInit() => SetRuleInstance(null);

    public void SetRuleInstance(TriadGameModifier RuleInstance) => RuleInst = RuleInstance;

    public TriadGameModifier GetResolvedRule() => RuleInst;
}

public class TriadGameModifierAllOpen : TriadGameModifier
{
    public TriadGameModifierAllOpen()
    {
        RuleName = "All Open";
        RuleIndex = 2;
        SpecialMod = ETriadGameSpecialMod.SelectVisible5;
    }

    public static void StaticMakeKnown(TriadGameSimulationState gameData, List<int> redIndices)
    {
        const int deckSize = 5;

        if (gameData.deckRed is TriadDeckInstanceManual deckRedEx && redIndices.Count <= deckSize)
        {
            if (gameData.bDebugRules)
            {
                Logger.WriteLine(">> Open:{0}! red indices:{1}", redIndices.Count, string.Join(", ", redIndices));
            }

            var redDeckVisible = new TriadDeck(deckRedEx.deck.knownCards, deckRedEx.deck.unknownCardPool);
            for (var idx = 0; idx < redIndices.Count; idx++)
            {
                var cardIdx = redIndices[idx];
                if (cardIdx < deckRedEx.deck.knownCards.Count)
                {
                }
                else
                {
                    var idxU = cardIdx - deckRedEx.deck.knownCards.Count;
                    var cardOb = deckRedEx.deck.unknownCardPool[idxU];
                    redDeckVisible.knownCards.Add(cardOb);
                    redDeckVisible.unknownCardPool.Remove(cardOb);
                }
            }

            for (var idx = 0; (idx < redDeckVisible.knownCards.Count) && (redDeckVisible.knownCards.Count > deckSize); idx++)
            {
                var cardOb = redDeckVisible.knownCards[idx];
                var orgIdx = deckRedEx.GetCardIndex(cardOb);
                if (!redIndices.Contains(orgIdx))
                {
                    redDeckVisible.knownCards.RemoveAt(idx);
                    idx--;
                }
            }

            gameData.deckRed = new TriadDeckInstanceManual(redDeckVisible);
        }
    }
}

public class TriadGameModifierThreeOpen : TriadGameModifier
{
    public TriadGameModifierThreeOpen()
    {
        RuleName = "Three Open";
        RuleIndex = 3;
        SpecialMod = ETriadGameSpecialMod.SelectVisible3;
    }
}

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

public class TriadGameModifierChaos : TriadGameModifier
{
    public TriadGameModifierChaos()
    {
        RuleName = "Chaos";
        RuleIndex = 12;
        SpecialMod = ETriadGameSpecialMod.BlueCardSelection;
    }
}

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

public class TriadGameModifierSame : TriadGameModifier
{
    public TriadGameModifierSame()
    {
        RuleName = "Same";
        RuleIndex = 7;
        bAllowCombo = true;
        Features = EFeature.CaptureNei | EFeature.CardPlaced;
    }

    public override void OnCheckCaptureNeis(TriadGameSimulationState gameData, int boardPos, int[] neiPos, List<int> captureList)
    {
        var checkCard = gameData.board[boardPos];
        var numSame = 0;
        var neiCaptureMask = 0;
        for (var sideIdx = 0; sideIdx < 4; sideIdx++)
        {
            var testNeiPos = neiPos[sideIdx];
            if (testNeiPos >= 0 && gameData.board[testNeiPos] != null)
            {
                var neiCard = gameData.board[testNeiPos];

                var numPos = checkCard.GetNumber((ETriadGameSide)sideIdx);
                var numOther = neiCard.GetOppositeNumber((ETriadGameSide)sideIdx);
                if (numPos == numOther)
                {
                    numSame++;

                    if (neiCard.owner != checkCard.owner)
                    {
                        neiCaptureMask |= (1 << sideIdx);
                    }
                }
            }
        }

        if (numSame >= 2)
        {
            for (var sideIdx = 0; sideIdx < 4; sideIdx++)
            {
                var testNeiPos = neiPos[sideIdx];
                if ((neiCaptureMask & (1 << sideIdx)) != 0)
                {
                    var neiCard = gameData.board[testNeiPos];
                    neiCard.owner = checkCard.owner;
                    captureList.Add(testNeiPos);

                    if (gameData.bDebugRules)
                    {
                        Logger.WriteLine(">> " + RuleName + "! [" + testNeiPos + "] " + neiCard.card.Name + " => " + neiCard.owner);
                    }
                }
            }
        }
    }
}

public class TriadGameModifierPlus : TriadGameModifier
{
    public TriadGameModifierPlus()
    {
        RuleName = "Plus";
        RuleIndex = 8;
        bAllowCombo = true;
        Features = EFeature.CaptureNei | EFeature.CardPlaced;
    }

    public override void OnCheckCaptureNeis(TriadGameSimulationState gameData, int boardPos, int[] neiPos, List<int> captureList)
    {
        var checkCard = gameData.board[boardPos];
        for (var sideIdx = 0; sideIdx < 4; sideIdx++)
        {
            var testNeiPos = neiPos[sideIdx];
            if (testNeiPos >= 0 && gameData.board[testNeiPos] != null)
            {
                var neiCard = gameData.board[testNeiPos];
                if (checkCard.owner != neiCard.owner)
                {
                    var numPosPattern = checkCard.GetNumber((ETriadGameSide)sideIdx);
                    var numOtherPattern = neiCard.GetOppositeNumber((ETriadGameSide)sideIdx);
                    var sumPattern = numPosPattern + numOtherPattern;
                    var bIsCaptured = false;

                    for (var vsSideIdx = 0; vsSideIdx < 4; vsSideIdx++)
                    {
                        var vsNeiPos = neiPos[vsSideIdx];
                        if (vsNeiPos >= 0 && sideIdx != vsSideIdx && gameData.board[vsNeiPos] != null)
                        {
                            var vsCard = gameData.board[vsNeiPos];

                            var numPosVs = checkCard.GetNumber((ETriadGameSide)vsSideIdx);
                            var numOtherVs = vsCard.GetOppositeNumber((ETriadGameSide)vsSideIdx);
                            var sumVs = numPosVs + numOtherVs;

                            if (sumPattern == sumVs)
                            {
                                bIsCaptured = true;

                                if (vsCard.owner != checkCard.owner)
                                {
                                    vsCard.owner = checkCard.owner;
                                    captureList.Add(vsNeiPos);

                                    if (gameData.bDebugRules)
                                    {
                                        Logger.WriteLine(">> " + RuleName + "! [" + vsNeiPos + "] " + vsCard.card.Name + " => " + vsCard.owner);
                                    }
                                }
                            }
                        }
                    }

                    if (bIsCaptured)
                    {
                        neiCard.owner = checkCard.owner;
                        captureList.Add(testNeiPos);

                        if (gameData.bDebugRules)
                        {
                            Logger.WriteLine(">> " + RuleName + "! [" + testNeiPos + "] " + neiCard.card.Name + " => " + neiCard.owner);
                        }
                    }
                }
            }
        }
    }
}

public class TriadGameModifierAscension : TriadGameModifier
{
    public TriadGameModifierAscension()
    {
        RuleName = "Ascension";
        RuleIndex = 9;
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
            var scoreMod = checkCard.scoreModifier + 1;
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
                gameData.typeMods[(int)checkCard.card.Type] += 1;
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
        const float ScoreMult = 0.8f;
        score *= ScoreMult;

        var bHasType = card.Type != ETriadCardType.None;
        if (bHasType)
        {
            score += (1.0f - ScoreMult);
        }
    }
}

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

public class TriadGameModifierSwap : TriadGameModifier
{
    public TriadGameModifierSwap()
    {
        RuleName = "Swap";
        RuleIndex = 13;
        SpecialMod = ETriadGameSpecialMod.SwapCards;
    }

    public static void StaticSwapCards(TriadGameSimulationState gameData, TriadCard swapFromBlue, int blueSlotIdx, TriadCard swapFromRed, int redSlotIdx)
    {
        if (gameData.deckBlue is TriadDeckInstanceManual deckBlueEx && gameData.deckRed is TriadDeckInstanceManual deckRedEx)
        {
            var bIsRedFromKnown = redSlotIdx < deckRedEx.deck.knownCards.Count;
            if (gameData.bDebugRules)
            {
                var DummyOb = new TriadGameModifierSwap();
                Logger.WriteLine(">> " + DummyOb.RuleName + "! blue[" + blueSlotIdx + "]:" + swapFromBlue.Name +
                                 " <-> red[" + redSlotIdx + (bIsRedFromKnown ? "" : ":Opt") + "]:" + swapFromRed.Name);
            }

            var blueDeckSwapped = new TriadDeck(deckBlueEx.deck.knownCards, deckBlueEx.deck.unknownCardPool);
            var redDeckSwapped = new TriadDeck(deckRedEx.deck.knownCards, deckRedEx.deck.unknownCardPool);

            redDeckSwapped.knownCards.Add(swapFromBlue);
            redDeckSwapped.knownCards.Remove(swapFromRed);
            redDeckSwapped.unknownCardPool.Remove(swapFromRed);

            blueDeckSwapped.knownCards[blueSlotIdx] = swapFromRed;

            gameData.deckBlue = new TriadDeckInstanceManual(blueDeckSwapped);
            gameData.deckRed = new TriadDeckInstanceManual(redDeckSwapped);
        }
    }
}

public class TriadGameModifierDraft : TriadGameModifier
{
    public TriadGameModifierDraft()
    {
        RuleName = "Draft";
        RuleIndex = 15;
        SpecialMod = ETriadGameSpecialMod.IgnoreOwnedCheck;
    }
}
