namespace Saucy.TripleTriad.UI;

internal static class TriadDeckEvalDisplay
{
    public static string? FormatWinChanceLabel(SolverResult chance)
    {
        if (chance.numGames <= 0)
        {
            return null;
        }

        if (chance.expectedResult == ETriadGameState.BlueWins)
        {
            return $"{chance.winChance:P0}";
        }

        if (chance.expectedResult == ETriadGameState.BlueDraw)
        {
            return $"{chance.drawChance:P0}";
        }

        return $"{chance.winChance:P0}";
    }

    public static string? FormatWinChanceLabel(TriadSession.DeckData? deckData)
    {
        if (deckData is not { chance.numGames: > 0 })
        {
            return null;
        }

        var chance = deckData.chance;
        return FormatWinChanceLabel(chance);
    }

    public static string FormatDeckNameWithWinChance(string deckName, TriadSession.DeckData? deckData)
    {
        if (string.IsNullOrWhiteSpace(deckName))
        {
            return deckName;
        }

        var winLabel = FormatWinChanceLabel(deckData);
        return winLabel != null ? $"{deckName} ({winLabel})" : deckName;
    }
}
