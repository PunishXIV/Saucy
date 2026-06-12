using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public static class TriadDeckEvaluator
{
    private static SolverResult EvaluateOpeningMove(
        TriadDeck playerDeck,
        TriadNpc npc,
        IEnumerable<TriadGameModifier> rules)
    {
        if (playerDeck == null || npc?.Deck == null)
        {
            return SolverResult.Zero;
        }

        var solver = TriadGameSolver.CreateLive();
        TriadNpcSimulationRules.InitializeSimulation(solver, npc, rules);
        var state = solver.StartSimulation(playerDeck, npc.Deck, ETriadGameState.InProgressBlue);
        solver.FindNextMove(state, out var _, out var _, out var result);
        return result;
    }

    public static SolverResult EvaluateOpeningMoveThrottled(
        TriadDeck playerDeck,
        TriadNpc npc,
        IEnumerable<TriadGameModifier> rules)
    {
        if (TriadUiState.IsBoardVisible())
        {
            return SolverResult.Zero;
        }

        var gate = SaucyParallelism.EvalConcurrency;
        gate.Wait();
        try
        {
            return EvaluateOpeningMove(playerDeck, npc, rules);
        }
        finally
        {
            gate.Release();
        }
    }
}
