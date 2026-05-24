using FFTriadBuddy;
namespace TriadBuddyPlugin;

public class SolverUtils
{
    public delegate void SolveDeckDelegate(SolverResult winChance);
    public static SolverGame? solverGame;
    public static SolverDeckOptimize? solverDeckOptimize;
    public static SolverPreGameDecks? solverPreGameDecks;

    public static void CreateSolvers()
    {
        solverGame = new();
        solverDeckOptimize = new();
        solverPreGameDecks = new();

        TriadGameSimulation.StaticInitialize();
    }

    public class DeckSolverContext
    {
        public SolveDeckDelegate? callback;
        public int deckId;
        public TriadGameSimulationState? gameState;
        public int passId;
        public TriadGameSolver? solver;
    }
}
