#nullable disable
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public enum ETriadGameState
{
    InProgressBlue,
    InProgressRed,
    BlueWins,
    BlueDraw,
    BlueLost
}

public class TriadGameSimulationState
{
    public const int boardSize = 3;
    public const int boardSizeSq = boardSize * boardSize;
    public bool bDebugRules;
    public TriadCardInstance[] board;
    public TriadDeckInstance deckBlue;
    public TriadDeckInstance deckRed;
    public int forcedCardIdx;
    public int numCardsPlaced;
    public int numRestarts;
    public ETriadGameSpecialMod resolvedSpecial;
    public ETriadGameState state;
    public int[] typeMods;

    public TriadGameSimulationState()
    {
        board = new TriadCardInstance[boardSizeSq];
        typeMods = new int[Enum.GetNames(typeof(ETriadCardType)).Length];
        state = ETriadGameState.InProgressBlue;
        resolvedSpecial = ETriadGameSpecialMod.None;
        numCardsPlaced = 0;
        numRestarts = 0;
        forcedCardIdx = -1;
        bDebugRules = false;

        for (var Idx = 0; Idx < typeMods.Length; Idx++)
        {
            typeMods[Idx] = 0;
        }
    }

    public TriadGameSimulationState(TriadGameSimulationState copyFrom)
    {
        board = new TriadCardInstance[copyFrom.board.Length];
        for (var Idx = 0; Idx < board.Length; Idx++)
        {
            board[Idx] = (copyFrom.board[Idx] == null) ? null : new TriadCardInstance(copyFrom.board[Idx]);
        }

        typeMods = new int[copyFrom.typeMods.Length];
        for (var Idx = 0; Idx < typeMods.Length; Idx++)
        {
            typeMods[Idx] = copyFrom.typeMods[Idx];
        }

        deckBlue = copyFrom.deckBlue.CreateCopy();
        deckRed = copyFrom.deckRed.CreateCopy();
        state = copyFrom.state;
        numCardsPlaced = copyFrom.numCardsPlaced;
        numRestarts = copyFrom.numRestarts;
        resolvedSpecial = copyFrom.resolvedSpecial;
        forcedCardIdx = copyFrom.forcedCardIdx;
        bDebugRules = copyFrom.bDebugRules;
    }
}

public class TriadGameSimulation
{
    public static int[][] cachedNeis = new int[9][];
    public TriadGameModifier.EFeature modFeatures = TriadGameModifier.EFeature.None;
    public List<TriadGameModifier> modifiers = [];
    public ETriadGameSpecialMod specialRules;

    public TriadGameSimulationState StartGame(TriadDeck deckBlue, TriadDeck deckRed, ETriadGameState state)
    {
        foreach (var mod in modifiers)
        {
            mod.OnMatchInit();
        }

        return new()
        {
            state = state, deckBlue = new TriadDeckInstanceManual(deckBlue), deckRed = new TriadDeckInstanceManual(deckRed)
        };
    }

    public void Initialize(IEnumerable<TriadGameModifier> modsA, IEnumerable<TriadGameModifier> modsB = null)
    {
        modifiers.Clear();

        if (modsA != null)
        {
            foreach (var mod in modsA)
            {
                var modCopy = (TriadGameModifier)Activator.CreateInstance(mod.GetType());
                modifiers.Add(modCopy);
            }
        }

        if (modsB != null)
        {
            foreach (var mod in modsB)
            {
                var modCopy = (TriadGameModifier)Activator.CreateInstance(mod.GetType());
                modifiers.Add(modCopy);
            }
        }

        UpdateSpecialRules();
    }

    public void UpdateSpecialRules()
    {
        specialRules = ETriadGameSpecialMod.None;
        modFeatures = TriadGameModifier.EFeature.None;
        foreach (var mod in modifiers)
        {
            specialRules |= mod.GetSpecialRules();
            modFeatures |= mod.GetFeatures();
        }
    }

    public bool HasSpecialRule(ETriadGameSpecialMod specialRule) => (specialRules & specialRule) != ETriadGameSpecialMod.None;

    public bool PlaceCard(TriadGameSimulationState gameState, int cardIdx, TriadDeckInstance cardDeck, ETriadCardOwner owner, int boardPos)
    {
        var bResult = false;

        var bIsAllowedOwner =
            ((owner == ETriadCardOwner.Blue) && (gameState.state == ETriadGameState.InProgressBlue)) ||
            ((owner == ETriadCardOwner.Red) && (gameState.state == ETriadGameState.InProgressRed));

        var card = cardDeck.GetCard(cardIdx);
        if (bIsAllowedOwner && (boardPos >= 0) && (gameState.board[boardPos] == null) && (card != null))
        {
            gameState.board[boardPos] = new(card, owner);
            gameState.numCardsPlaced++;

            if (owner == ETriadCardOwner.Blue)
            {
                gameState.deckBlue.OnCardPlacedFast(cardIdx);
                gameState.state = ETriadGameState.InProgressRed;
            }
            else
            {
                gameState.deckRed.OnCardPlacedFast(cardIdx);
                gameState.state = ETriadGameState.InProgressBlue;
            }

            bResult = (owner == ETriadCardOwner.Red) || !HasSpecialRule(ETriadGameSpecialMod.IgnoreOwnedCheck);

            var bAllowCombo = false;
            if ((modFeatures & TriadGameModifier.EFeature.CardPlaced) != 0)
            {
                foreach (var mod in modifiers)
                {
                    mod.OnCardPlaced(gameState, boardPos);
                    bAllowCombo = bAllowCombo || mod.AllowsCombo();
                }
            }

            List<int> comboList = [];
            var comboCounter = 0;
            CheckCaptures(gameState, boardPos, comboList, comboCounter);

            while (bAllowCombo && comboList.Count > 0)
            {
                if (gameState.bDebugRules) { Logger.WriteLine(">> combo step: {0}", string.Join(",", comboList)); }

                List<int> nextCombo = [];
                comboCounter++;
                foreach (var pos in comboList)
                {
                    CheckCaptures(gameState, pos, nextCombo, comboCounter);
                }

                comboList = nextCombo;
            }

            if ((modFeatures & TriadGameModifier.EFeature.PostCapture) != 0)
            {
                foreach (var mod in modifiers)
                {
                    mod.OnPostCaptures(gameState, boardPos);
                }
            }

            if (gameState.numCardsPlaced == gameState.board.Length)
            {
                OnAllCardsPlaced(gameState);
            }
        }

        return bResult;
    }

    public bool PlaceCard(TriadGameSimulationState gameState, TriadCard card, ETriadCardOwner owner, int boardPos)
    {
        var useDeck = (owner == ETriadCardOwner.Blue) ? gameState.deckBlue : gameState.deckRed;
        var cardIdx = useDeck.GetCardIndex(card);

        return PlaceCard(gameState, cardIdx, useDeck, owner, boardPos);
    }

    public static int GetBoardPos(int x, int y) => x + (y * TriadGameSimulationState.boardSize);

    public static void GetBoardXY(int pos, out int x, out int y)
    {
        x = pos % TriadGameSimulationState.boardSize;
        y = pos / TriadGameSimulationState.boardSize;
    }

    public static int[] GetNeighbors(TriadGameSimulationState gameState, int boardPos)
    {
        GetBoardXY(boardPos, out var boardPosX, out var boardPosY);

        var resultNeis = new int[4];
        resultNeis[(int)ETriadGameSide.Up] = (boardPosY > 0) ? GetBoardPos(boardPosX, boardPosY - 1) : -1;
        resultNeis[(int)ETriadGameSide.Down] = (boardPosY < (TriadGameSimulationState.boardSize - 1)) ? GetBoardPos(boardPosX, boardPosY + 1) : -1;
        resultNeis[(int)ETriadGameSide.Right] = (boardPosX > 0) ? GetBoardPos(boardPosX - 1, boardPosY) : -1;
        resultNeis[(int)ETriadGameSide.Left] = (boardPosX < (TriadGameSimulationState.boardSize - 1)) ? GetBoardPos(boardPosX + 1, boardPosY) : -1;

        return resultNeis;
    }

    private void CheckCaptures(TriadGameSimulationState gameState, int boardPos, List<int> comboList, int comboCounter)
    {
        var neis = cachedNeis[boardPos];
        var allowMods = comboCounter == 0;
        if (allowMods && (modFeatures & TriadGameModifier.EFeature.CaptureNei) != 0)
        {
            foreach (var mod in modifiers)
            {
                mod.OnCheckCaptureNeis(gameState, boardPos, neis, comboList);
            }
        }

        var isReverseActive = allowMods && ((modFeatures & TriadGameModifier.EFeature.CaptureMath) != 0);

        var checkCard = gameState.board[boardPos];
        for (var sideIdx = 0; sideIdx < 4; sideIdx++)
        {
            var neiPos = neis[sideIdx];
            if (neiPos >= 0 && gameState.board[neiPos] != null)
            {
                var neiCard = gameState.board[neiPos];
                if (checkCard.owner != neiCard.owner)
                {
                    var numPos = checkCard.GetNumber((ETriadGameSide)sideIdx);
                    var numOther = neiCard.GetOppositeNumber((ETriadGameSide)sideIdx);

                    if (allowMods && (modFeatures & TriadGameModifier.EFeature.CaptureWeights) != 0)
                    {
                        foreach (var mod in modifiers)
                        {
                            mod.OnCheckCaptureCardWeights(gameState, boardPos, neiPos, isReverseActive, ref numPos, ref numOther);
                        }
                    }

                    var bIsCaptured = (numPos > numOther);
                    if (allowMods && (modFeatures & TriadGameModifier.EFeature.CaptureMath) != 0)
                    {
                        foreach (var mod in modifiers)
                        {
                            mod.OnCheckCaptureCardMath(gameState, boardPos, neiPos, numPos, numOther, ref bIsCaptured);
                        }
                    }

                    if (bIsCaptured)
                    {
                        neiCard.owner = checkCard.owner;
                        if (comboCounter > 0)
                        {
                            comboList.Add(neiPos);
                        }

                        if (gameState.bDebugRules)
                        {
                            Logger.WriteLine(">> " + (comboCounter > 0 ? "combo!" : "") + " [" + neiPos + "] " + neiCard.card.Name + " => " + neiCard.owner);
                        }
                    }
                }
            }
        }
    }

    private void OnAllCardsPlaced(TriadGameSimulationState gameState)
    {
        var numBlue = (gameState.deckBlue.availableCardMask != 0) ? 1 : 0;
        foreach (var card in gameState.board)
        {
            if (card.owner == ETriadCardOwner.Blue)
            {
                numBlue++;
            }
        }

        var numBlueToWin = (gameState.board.Length / 2) + 1;
        gameState.state = (numBlue > numBlueToWin) ? ETriadGameState.BlueWins :
            (numBlue == numBlueToWin) ? ETriadGameState.BlueDraw :
            ETriadGameState.BlueLost;

        if (gameState.bDebugRules)
        {
            var availBlueCard = gameState.deckBlue.GetFirstAvailableCard();
            Logger.WriteLine(">> blue:" + numBlue + " (in deck:" + ((availBlueCard != null) ? availBlueCard.Name : "none") + "), required:" + numBlueToWin + " => " + gameState.state);
        }

        if ((modFeatures & TriadGameModifier.EFeature.AllPlaced) != 0)
        {
            foreach (var mod in modifiers)
            {
                mod.OnAllCardsPlaced(gameState);
            }
        }
    }

    public static void StaticInitialize()
    {
        for (var idxPos = 0; idxPos < 9; idxPos++)
        {
            cachedNeis[idxPos] = GetNeighbors(null, idxPos);
        }
    }
}
