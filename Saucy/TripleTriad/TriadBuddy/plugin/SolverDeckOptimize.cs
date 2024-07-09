using FFTriadBuddy;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TriadBuddyPlugin
{
    public class SolverDeckOptimize
    {
        public TriadDeckOptimizer deckOptimizer = new();

        private bool pauseOptimizerForSolver = false;
        private bool pauseOptimizerForOptimizedEval = false;
        private bool pauseOptimizerForDeckEval = false;

        public void SolveOptimizedDeck(TriadDeck? deck, TriadNpc? npc, List<TriadGameModifier> regionMods, SolverUtils.SolveDeckDelegate callback)
        {
            if (npc == null || deck == null)
            {
                return;
            }

            var deckSolver = new TriadGameSolver();
            deckSolver.InitializeSimulation(npc.Rules, regionMods);

            var gameState = deckSolver.StartSimulation(deck, npc.Deck, ETriadGameState.InProgressBlue);
            var calcContext = new SolverUtils.DeckSolverContext() { solver = deckSolver, gameState = gameState, callback = callback };

            SetPauseForOptimizedDeck(true);

            Action<object?> solverAction = (ctxOb) =>
            {
                var ctx = ctxOb as SolverUtils.DeckSolverContext;
                if (ctx != null && ctx.solver != null && ctx.gameState != null)
                {
                    ctx.solver.FindNextMove(ctx.gameState, out _, out _, out var solverResult);
                    callback?.Invoke(solverResult);
                }

                SetPauseForOptimizedDeck(false);
            };

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

        private void UpdateDeckOptimizerPause()
        {
            deckOptimizer.SetPaused(pauseOptimizerForSolver || pauseOptimizerForDeckEval || pauseOptimizerForOptimizedEval);
        }
    }
}