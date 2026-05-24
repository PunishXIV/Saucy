using FFTriadBuddy;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace TriadBuddyPlugin;

public class SolverDeckOptimize
{
    public TriadDeckOptimizer deckOptimizer = new();
    private bool pauseOptimizerForDeckEval;
    private bool pauseOptimizerForOptimizedEval;

    private bool pauseOptimizerForSolver;

    public void SolveOptimizedDeck(TriadDeck? deck, TriadNpc? npc, List<TriadGameModifier> regionMods, SolverUtils.SolveDeckDelegate callback)
    {
        if (npc == null || deck == null)
        {
            return;
        }

        var deckSolver = new TriadGameSolver();
        deckSolver.InitializeSimulation(npc.Rules, regionMods);

        var gameState = deckSolver.StartSimulation(deck, npc.Deck, ETriadGameState.InProgressBlue);
        var calcContext = new SolverUtils.DeckSolverContext
        {
            solver = deckSolver, gameState = gameState, callback = callback
        };

        SetPauseForOptimizedDeck(true);

        void solverAction(object ctxOb)
        {
            if (ctxOb is SolverUtils.DeckSolverContext ctx && ctx.solver != null && ctx.gameState != null)
            {
                ctx.solver.FindNextMove(ctx.gameState, out var _, out var _, out var solverResult);
                callback?.Invoke(solverResult);
            }

            SetPauseForOptimizedDeck(false);
        }

        new TaskFactory().StartNew(solverAction, calcContext);
    }

    public void SetPauseForGameSolver(bool wantsPause)
    {
        pauseOptimizerForSolver = wantsPause;
        UpdateDeckOptimizerPause();
    }

    public void SetPauseForPreGameDecks(bool wantsPause)
    {
        pauseOptimizerForDeckEval = wantsPause;
        UpdateDeckOptimizerPause();
    }

    private void SetPauseForOptimizedDeck(bool wantsPause)
    {
        pauseOptimizerForOptimizedEval = wantsPause;
        UpdateDeckOptimizerPause();
    }

    private void UpdateDeckOptimizerPause() => deckOptimizer.SetPaused(pauseOptimizerForSolver || pauseOptimizerForDeckEval || pauseOptimizerForOptimizedEval);
}
