#nullable disable
namespace Saucy.TripleTriad.GameLogic;

public abstract class TriadGameAgent
{
    public virtual void Initialize(TriadGameSolver solver, int sessionSeed) { }
    public virtual bool IsInitialized() => true;
    public virtual void OnSimulationStart() { }

    public abstract bool FindNextMove(TriadGameSolver solver, TriadGameSimulationState gameState, out int cardIdx, out int boardPos, out SolverResult solverResult);
}
