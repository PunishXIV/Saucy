#nullable disable
using System;
namespace Saucy.TripleTriad.GameLogic;

public abstract class TriadGameAgentGraphExplorer : TriadGameAgent
{
    private Random failsafeRandStream;
    protected int sessionSeed;
    protected virtual bool SkipDuplicateCardIds => true;

    public override void Initialize(TriadGameSolver solver, int sessionSeed) => this.sessionSeed = sessionSeed;

    public override bool FindNextMove(TriadGameSolver solver, TriadGameSimulationState gameState, out int cardIdx, out int boardPos, out SolverResult solverResult)
    {
        cardIdx = -1;
        boardPos = -1;

        var isFinished = IsFinished(gameState, out solverResult);
        if (!isFinished && IsInitialized())
        {
            _ = SearchActionSpace(solver, gameState, 0, out cardIdx, out boardPos, out solverResult);
        }

        return (cardIdx >= 0) && (boardPos >= 0);
    }

    protected bool IsFinished(TriadGameSimulationState gameState, out SolverResult gameResult)
    {
        switch (gameState.state)
        {
            case ETriadGameState.BlueWins:
                gameResult = new(1, 0, 1);
                return true;

            case ETriadGameState.BlueDraw:
                gameResult = new(0, 1, 1);
                return true;

            case ETriadGameState.BlueLost:
                gameResult = new(0, 0, 1);
                return true;
        }

        gameResult = SolverResult.Zero;
        return false;
    }

    protected virtual SolverResult SearchActionSpace(TriadGameSolver solver, TriadGameSimulationState gameState, int searchLevel, out int bestCardIdx, out int bestBoardPos, out SolverResult bestActionResult)
    {
        bestCardIdx = -1;
        bestBoardPos = -1;
        bestActionResult = SolverResult.Zero;

        float numWinsTotal = 0;
        float numDrawsTotal = 0;
        long numGamesTotal = 0;

        solver.FindAvailableActions(gameState, out var availBoardMask, out var numAvailBoard, out var availCardsMask, out var numAvailCards);
        if (numAvailCards > 0 && numAvailBoard > 0)
        {
            var turnOwner = (gameState.state == ETriadGameState.InProgressBlue) ? ETriadCardOwner.Blue : ETriadCardOwner.Red;
            var currentDeck = (gameState.state == ETriadGameState.InProgressBlue) ? gameState.deckBlue : gameState.deckRed;
            var hasValidPlacements = false;

            for (var cardIdx = 0; cardIdx < TriadDeckInstance.maxAvailableCards; cardIdx++)
            {
                var cardNotAvailable = (availCardsMask & (1 << cardIdx)) == 0;
                if (cardNotAvailable)
                {
                    continue;
                }

                if (SkipDuplicateCardIds)
                {
                    var cardDef = currentDeck.GetCard(cardIdx);
                    var alreadyEvaluated = false;
                    for (var priorIdx = 0; priorIdx < cardIdx; priorIdx++)
                    {
                        if ((availCardsMask & (1 << priorIdx)) == 0)
                        {
                            continue;
                        }

                        var priorCard = currentDeck.GetCard(priorIdx);
                        if (priorCard != null && cardDef != null && priorCard.Id == cardDef.Id)
                        {
                            alreadyEvaluated = true;
                            break;
                        }
                    }

                    if (alreadyEvaluated)
                    {
                        continue;
                    }
                }

                for (var boardIdx = 0; boardIdx < gameState.board.Length; boardIdx++)
                {
                    var boardNotAvailable = (availBoardMask & (1 << boardIdx)) == 0;
                    if (boardNotAvailable)
                    {
                        continue;
                    }

                    var gameStateCopy = new TriadGameSimulationState(gameState);
                    var useDeck = (gameStateCopy.state == ETriadGameState.InProgressBlue) ? gameStateCopy.deckBlue : gameStateCopy.deckRed;

                    var isPlaced = solver.simulation.PlaceCard(gameStateCopy, cardIdx, useDeck, turnOwner, boardIdx);
                    if (isPlaced)
                    {
                        var isFinished = IsFinished(gameStateCopy, out var branchResult);
                        if (!isFinished)
                        {
                            gameStateCopy.forcedCardIdx = -1;
                            branchResult = SearchActionSpace(solver, gameStateCopy, searchLevel + 1, out var _, out var _, out var _);
                        }

                        if (branchResult.IsBetterThan(bestActionResult) || !hasValidPlacements)
                        {
                            bestActionResult = branchResult;
                            bestCardIdx = cardIdx;
                            bestBoardPos = boardIdx;
                        }

                        numWinsTotal += branchResult.numWins;
                        numDrawsTotal += branchResult.numDraws;
                        numGamesTotal += branchResult.numGames;
                        hasValidPlacements = true;
                    }
                }
            }

            if (!hasValidPlacements)
            {
                failsafeRandStream ??= new(sessionSeed);

                bestCardIdx = TriadGameAgentRandom.PickRandomBitFromMask(availCardsMask, failsafeRandStream.Next(numAvailCards));
                bestBoardPos = TriadGameAgentRandom.PickRandomBitFromMask(availBoardMask, failsafeRandStream.Next(numAvailBoard));
            }
        }

        var isOwnerTurn = (searchLevel % 2) == 0;
        return isOwnerTurn ? bestActionResult : new(numWinsTotal, numDrawsTotal, numGamesTotal);
    }
}
