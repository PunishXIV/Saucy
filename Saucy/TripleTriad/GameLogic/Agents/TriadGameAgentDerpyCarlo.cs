#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.GameLogic;

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
        using var threadSolvers = new ThreadLocal<TriadGameSolver>(() => solver.CreateWorkerCopy());
        using var threadAgents = new ThreadLocal<TriadGameAgentRandom>(() =>
            new TriadGameAgentRandom(null, sessionSeed + Environment.CurrentManagedThreadId));

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

        // normalized so the result weighs the same as a single-game branch when summed at opponent levels (upstream parity)
        return new(1.0f * numWinningWorkers / maxWorkers, 1.0f * numDrawingWorkers / maxWorkers, 1);
    }
}
