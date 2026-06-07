#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
namespace Saucy.TripleTriad.GameLogic;

public partial class TriadGameScreenMemory
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
        SwapHints = 32
    }

    private readonly List<TriadCard[]> blueDeckHistory;
    private bool bHasOpenRule;
    private bool bHasRestartRule;
    private bool bHasSwapRule;
    private bool bSwapStartChecked;
    public TriadDeckInstanceScreen deckBlue;
    public TriadDeckInstanceScreen deckRed;
    public TriadGameSolver gameSolver;

    public TriadGameSimulationState gameState;
    private TriadNpc lastScanNpc;
    private TriadCard[] playerDeckPattern;
    public int swappedBlueCardIdx;
    public TriadGameScreenMemory()
    {
        gameSolver = TriadGameSolver.CreateLive();
        gameState = new();
        deckBlue = new();
        deckRed = new();
        blueDeckHistory = [];
        bHasSwapRule = false;
        swappedBlueCardIdx = -1;
        lastScanNpc = null;
    }

    public EUpdateFlags OnNewScan(TriadBoardScanner.GameState screenGame, TriadNpc selectedNpc)
    {
        var updateFlags = EUpdateFlags.None;
        if (screenGame == null)
        {
            return updateFlags;
        }

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
                }
            }
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

            deckRed.SetSwappedCard(null, -1);

            gameSolver.agent?.OnSimulationStart();
        }

        var bRemoveBlueHistory = bModsChanged || (lastScanNpc != selectedNpc);
        if (bRemoveBlueHistory)
        {
            blueDeckHistory.Clear();
            bSwapStartChecked = false;
        }

        var bRedDeckChanged = (lastScanNpc != selectedNpc) || !IsDeckMatching(deckRed, screenGame.redDeck) || (deckRed.deck != selectedNpc.Deck);
        var bNpcChanged = lastScanNpc != selectedNpc;
        if (bRedDeckChanged)
        {
            updateFlags |= EUpdateFlags.RedDeck;
            deckRed.deck = selectedNpc.Deck;
            lastScanNpc = selectedNpc;

            if (bNpcChanged)
            {
                gameSolver.agent?.OnSimulationStart();
            }

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
        gameState.forcedCardIdx = screenGame.forcedBlueCard != null
            ? deckBlue.GetCardIndex(screenGame.forcedBlueCard)
            : -1;

        if (gameState.forcedCardIdx >= 0 &&
            (deckBlue.availableCardMask & (1 << gameState.forcedCardIdx)) == 0)
        {
            gameState.forcedCardIdx = -1;
        }

        var bBoardChanged = false;
        for (var Idx = 0; Idx < gameState.board.Length; Idx++)
        {
            var bWasNull = gameState.board[Idx] == null;
            var bIsNull = screenGame.board[Idx] == null;

            if (bWasNull && !bIsNull)
            {
                bBoardChanged = true;
                gameState.board[Idx] = new(screenGame.board[Idx], screenGame.boardOwner[Idx]);
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
                    gameState.board[Idx] = new(screenGame.board[Idx], screenGame.boardOwner[Idx]);
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

        SyncBlueHandMaskWithBoard(deckBlue, screenGame);

        if (bHasSwapRule && !bSwapStartChecked && gameState.numCardsPlaced <= 1)
        {
            bSwapStartChecked = true;
            updateFlags |= DetectSwapOnGameStart();
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

    private static void SyncBlueHandMaskWithBoard(TriadDeckInstanceScreen deckBlue, TriadBoardScanner.GameState screenGame)
    {
        if (deckBlue?.cards == null || screenGame?.board == null)
        {
            return;
        }

        for (var handIdx = 0; handIdx < deckBlue.cards.Length; handIdx++)
        {
            if ((deckBlue.availableCardMask & (1 << handIdx)) == 0)
            {
                continue;
            }

            var handCard = deckBlue.GetCard(handIdx);
            if (handCard == null)
            {
                continue;
            }

            for (var boardIdx = 0; boardIdx < screenGame.board.Length; boardIdx++)
            {
                if (screenGame.board[boardIdx] == null ||
                    screenGame.boardOwner[boardIdx] != ETriadCardOwner.Blue)
                {
                    continue;
                }

                if (!HandCardMatchesBoardCard(handCard, screenGame.board[boardIdx]))
                {
                    continue;
                }

                deckBlue.availableCardMask &= ~(1 << handIdx);
                if (deckBlue.cards[handIdx] != null)
                {
                    deckBlue.cards[handIdx] = null;
                    deckBlue.numPlaced++;
                }

                break;
            }
        }
    }

    private static bool HandCardMatchesBoardCard(TriadCard hand, TriadCard board)
    {
        if (hand == null || board == null)
        {
            return false;
        }

        if (hand.Id != 0 && hand.Id == board.Id)
        {
            return true;
        }

        return !string.IsNullOrEmpty(hand.Name) && hand.Name == board.Name;
    }
}
