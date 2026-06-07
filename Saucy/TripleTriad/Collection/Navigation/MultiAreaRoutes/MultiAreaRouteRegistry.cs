using Dalamud.Game.Text.SeStringHandling.Payloads;
using System.Collections.Generic;
using System.Linq;
namespace Saucy.TripleTriad;

internal static class MultiAreaRouteRegistry
{
    private static readonly IReadOnlyList<MultiAreaRoute> Routes =
    [
        JeunoFirstWalkRoute.Route,
        FortempsManservantRoute.Route,
        DomanEnclaveRoute.Route
    ];

    public static MultiAreaRoute? FindRoute(MapLinkPayload location) =>
        Routes.FirstOrDefault(route => route.Matches(location));

    public static MultiAreaRoute? FindRouteForTerritory(uint territoryId) =>
        Routes.FirstOrDefault(route =>
            route.ArrivalTerritoryIds?.Any(id => id == territoryId) == true);

    public static bool MatchesDestination(MapLinkPayload location) => FindRoute(location) != null;
}
