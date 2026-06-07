#nullable disable
namespace Saucy.TripleTriad.GameLogic;

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
