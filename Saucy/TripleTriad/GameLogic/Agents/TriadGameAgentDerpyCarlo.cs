#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameAgentDerpyCarlo : TriadGameAgentGraphExplorer
{
    private const int MaxWorkers = 2000;
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
        var workerCount = Math.Max(RolloutBatchSize, MaxWorkers);
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
        var numWinningWorkers = 0;
        var numDrawingWorkers = 0;
        var completedWorkers = 0;

        while (completedWorkers < MaxWorkers)
        {
            var batchEnd = Math.Min(completedWorkers + RolloutBatchSize, MaxWorkers);
            Parallel.For(completedWorkers, batchEnd, RunWorkerRollout);
            completedWorkers = batchEnd;

            void RunWorkerRollout(int workerIdx)
            {
                var gameStateCopy = new TriadGameSimulationState(gameState);
                var agent = workerAgents[workerIdx % workerAgents.Length];
                solver.RunSimulation(gameStateCopy, agent, agent);

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

        return new SolverResult(numWinningWorkers, numDrawingWorkers, MaxWorkers);
    }
}
