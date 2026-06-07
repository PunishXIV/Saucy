#nullable disable
using System;
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameAgentRandom : TriadGameAgent
{
    private Random randGen;

    public TriadGameAgentRandom(TriadGameSolver solver, int sessionSeed) => Initialize(solver, sessionSeed);

    public override void Initialize(TriadGameSolver solver, int sessionSeed) => randGen = new(sessionSeed);

    public override bool IsInitialized() => randGen != null;

    public override bool FindNextMove(TriadGameSolver solver, TriadGameSimulationState gameState, out int cardIdx, out int boardPos, out SolverResult solverResult)
    {
        cardIdx = -1;
        boardPos = -1;
        solverResult = SolverResult.Zero;

        if (!IsInitialized())
        {
            return false;
        }

        const int boardPosMax = TriadGameSimulationState.boardSizeSq;
        if (gameState.numCardsPlaced < TriadGameSimulationState.boardSizeSq)
        {
            var testPos = randGen.Next(boardPosMax);
            for (var passIdx = 0; passIdx < boardPosMax; passIdx++)
            {
                testPos = (testPos + 1) % boardPosMax;
                if (gameState.board[testPos] == null)
                {
                    boardPos = testPos;
                    break;
                }
            }
        }

        cardIdx = -1;
        var useDeck = (gameState.state == ETriadGameState.InProgressBlue) ? gameState.deckBlue : gameState.deckRed;
        if (useDeck.availableCardMask > 0)
        {
            var testIdx = randGen.Next(TriadDeckInstance.maxAvailableCards);
            for (var passIdx = 0; passIdx < TriadDeckInstance.maxAvailableCards; passIdx++)
            {
                testIdx = (testIdx + 1) % TriadDeckInstance.maxAvailableCards;
                if ((useDeck.availableCardMask & (1 << testIdx)) != 0)
                {
                    cardIdx = testIdx;
                    break;
                }
            }
        }

        return (boardPos >= 0) && (cardIdx >= 0);
    }

    public static int PickRandomBitFromMask(int mask, int randStep)
    {
        var bitIdx = 0;
        var testMask = 1 << bitIdx;
        while (testMask <= mask)
        {
            if ((testMask & mask) != 0)
            {
                randStep--;
                if (randStep < 0)
                {
                    return bitIdx;
                }
            }

            bitIdx++;
            testMask <<= 1;
        }

        return -1;
    }
}
