#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.GameLogic;

public abstract class TriadGameAgent
{
    public virtual void Initialize(TriadGameSolver solver, int sessionSeed) { }
    public virtual bool IsInitialized() => true;
    public virtual void OnSimulationStart() { }

    public abstract bool FindNextMove(TriadGameSolver solver, TriadGameSimulationState gameState, out int cardIdx, out int boardPos, out SolverResult solverResult);
}

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

public class TriadGameAgentDerpyCarlo : TriadGameAgentGraphExplorer
{
    private const int BackgroundMaxWorkers = 2000;
    private const int RolloutBatchSize = 64;

    protected TriadGameAgentRandom[] workerAgents;

    public override void Initialize(TriadGameSolver solver, int sessionSeed)
    {
        base.Initialize(solver, sessionSeed);
        EnsureWorkers(sessionSeed);
        for (var idx = 0; idx < workerAgents.Length; idx++)
        {
            workerAgents[idx].Initialize(solver, sessionSeed + idx);
        }
    }

    protected void EnsureWorkers(int sessionSeed)
    {
        var workerCount = Math.Max(RolloutBatchSize, BackgroundMaxWorkers);
        if (workerAgents != null && workerAgents.Length == workerCount)
        {
            return;
        }

        workerAgents = new TriadGameAgentRandom[workerCount];
        for (var idx = 0; idx < workerCount; idx++)
        {
            workerAgents[idx] = new(null, sessionSeed + idx);
        }
    }

    public override bool IsInitialized() => workerAgents != null;

    protected override SolverResult SearchActionSpace(TriadGameSolver solver, TriadGameSimulationState gameState, int searchLevel, out int bestCardIdx, out int bestBoardPos, out SolverResult bestActionResult)
    {
        var runWorkers = CanRunRandomExploration(solver, gameState, searchLevel);
        if (runWorkers)
        {
            bestCardIdx = -1;
            bestBoardPos = -1;
            bestActionResult = FindWinningProbability(solver, gameState);

            return bestActionResult;
        }

        return base.SearchActionSpace(solver, gameState, searchLevel, out bestCardIdx, out bestBoardPos, out bestActionResult);
    }

    protected virtual bool CanRunRandomExploration(TriadGameSolver solver, TriadGameSimulationState gameState, int searchLevel) => searchLevel > 0;

    protected virtual SolverResult FindWinningProbability(TriadGameSolver solver, TriadGameSimulationState gameState)
    {
        var maxWorkers = SaucyParallelism.RolloutWorkerCount;
        var numWinningWorkers = 0;
        var numDrawingWorkers = 0;
        var completedWorkers = 0;
        var parallelOptions = SaucyParallelism.RolloutParallelOptions;
        using var threadSolvers = new ThreadLocal<TriadGameSolver>(solver.CreateWorkerCopy);
        using var threadAgents = new ThreadLocal<TriadGameAgentRandom>(() =>
            new(null, sessionSeed + Environment.CurrentManagedThreadId));

        while (completedWorkers < maxWorkers)
        {
            var batchEnd = Math.Min(completedWorkers + RolloutBatchSize, maxWorkers);
            Parallel.For(completedWorkers, batchEnd, parallelOptions, RunWorkerRollout);
            completedWorkers = batchEnd;

            void RunWorkerRollout(int workerIdx)
            {
                var gameStateCopy = new TriadGameSimulationState(gameState);
                var rolloutAgent = threadAgents.Value!;
                rolloutAgent.Initialize(threadSolvers.Value, sessionSeed + workerIdx);
                threadSolvers.Value.RunSimulation(gameStateCopy, rolloutAgent, rolloutAgent);

                if (gameStateCopy.state == ETriadGameState.BlueWins)
                {
                    Interlocked.Increment(ref numWinningWorkers);
                }
                else if (gameStateCopy.state == ETriadGameState.BlueDraw)
                {
                    Interlocked.Increment(ref numDrawingWorkers);
                }
            }
        }

        return new(1.0f * numWinningWorkers / maxWorkers, 1.0f * numDrawingWorkers / maxWorkers, 1);
    }
}

public class TriadGameAgentCarloTheExplorer : TriadGameAgentDerpyCarlo
{
    public const long MaxStatesToExplore = 10 * 1000;

    private int minPlacedToExplore = 10;
    private int minPlacedToExploreWithForced = 10;

    public override void Initialize(TriadGameSolver solver, int sessionSeed)
    {
        base.Initialize(solver, sessionSeed);

        long numStatesForced = 1;
        long numStates = 1;

        const int maxToPlace = TriadGameSimulationState.boardSizeSq;
        for (var numToPlace = 1; numToPlace <= maxToPlace; numToPlace++)
        {
            var numPlaced = maxToPlace - numToPlace;

            numStatesForced *= numToPlace;
            if (numStatesForced <= MaxStatesToExplore)
            {
                minPlacedToExploreWithForced = numPlaced;
            }

            numStates *= numToPlace * ((numToPlace + 2) / 2) * ((numToPlace + 1) / 2);
            if (numStates <= MaxStatesToExplore)
            {
                minPlacedToExplore = numPlaced;
            }
        }
    }

    protected override bool CanRunRandomExploration(TriadGameSolver solver, TriadGameSimulationState gameState, int searchLevel)
    {
        var numPlacedThr = (gameState.forcedCardIdx < 0) ? minPlacedToExplore : minPlacedToExploreWithForced;

        return (searchLevel > 0) && (gameState.numCardsPlaced < numPlacedThr);
    }
}

public abstract class TriadGameAgentGraphExplorer : TriadGameAgent
{
    // Sudden Death can recurse; cap depth so Chaos matches cannot 0xC00000FD the game thread.
    private const int MaxSearchDepth = 20;

    private Random failsafeRandStream;
    protected int sessionSeed;

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

        if (searchLevel > MaxSearchDepth)
        {
            return bestActionResult;
        }

        float numWinsTotal = 0;
        float numDrawsTotal = 0;
        long numGamesTotal = 0;

        solver.FindAvailableActions(gameState, out var availBoardMask, out var numAvailBoard, out var availCardsMask, out var numAvailCards);
        if (numAvailCards > 0 && numAvailBoard > 0)
        {
            var turnOwner = (gameState.state == ETriadGameState.InProgressBlue) ? ETriadCardOwner.Blue : ETriadCardOwner.Red;
            var hasValidPlacements = false;

            for (var cardIdx = 0; cardIdx < TriadDeckInstance.maxAvailableCards; cardIdx++)
            {
                var cardNotAvailable = (availCardsMask & (1 << cardIdx)) == 0;
                if (cardNotAvailable)
                {
                    continue;
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
