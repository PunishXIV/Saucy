using FFTriadBuddy;

namespace TriadBuddyPlugin
{
    public class SolverUtils
    {
        public static SolverGame? solverGame;
        public static SolverDeckOptimize? solverDeckOptimize;
        public static SolverPreGameDecks? solverPreGameDecks;

        public delegate void SolveDeckDelegate(SolverResult winChance);

        public class DeckSolverContext
        {
            public TriadGameSolver? solver;
            public TriadGameSimulationState? gameState;
            public SolveDeckDelegate? callback;
            public int deckId;
            public int passId;
        }

        public static void CreateSolvers()
        {
            solverGame = new SolverGame();
            solverDeckOptimize = new SolverDeckOptimize();
            solverPreGameDecks = new SolverPreGameDecks();

            TriadGameSimulation.StaticInitialize();
        }
    }
}
