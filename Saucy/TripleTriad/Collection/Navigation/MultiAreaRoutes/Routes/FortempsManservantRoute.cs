using System;
namespace Saucy.TripleTriad;

internal static class FortempsManservantRoute
{
    private const uint FoundationAetheryteId = 70;
    private const uint LastVigilAethernetShardId = 87;
    private const uint GatekeeperNpcDataId = 1011217;
    private const uint FortempsManorTerritoryId = 433;
    private const uint PillarsMapId = 219;
    private const uint ManorInteriorMapId = 222;
    private const float GuardMapX = 11.5f;
    private const float GuardMapY = 11.0f;
    private const float ManservantMapX = 6f;
    private const float ManservantMapY = 6f;
    private static readonly uint[] FortempsManorTerritoryIds = [FortempsManorTerritoryId];

    internal static readonly MultiAreaRoute Route = new()
    {
        Name = "Fortemps Manor",
        TooltipHint = "Foundation aetheryte, aethernet to The Last Vigil, then enter the manor.",
        ArrivalTerritoryIds = FortempsManorTerritoryIds,
        InteriorMapId = ManorInteriorMapId,
        InteriorMapX = ManservantMapX,
        InteriorMapY = ManservantMapY,
        Matches = location =>
            MultiAreaRouteMatchers.MatchesDestination(
                location,
                FortempsManorTerritoryIds,
                ManorInteriorMapId),
        Timeout = TimeSpan.FromSeconds(240),
        Steps =
        [
            new()
            {
                Kind = MultiAreaRouteStepKind.Teleport, AetheryteId = FoundationAetheryteId
            },
            new()
            {
                Kind = MultiAreaRouteStepKind.Aethernet, AetheryteId = LastVigilAethernetShardId
            },
            new()
            {
                Kind = MultiAreaRouteStepKind.MoveTo,
                ApproachMapId = PillarsMapId,
                ApproachMapX = GuardMapX,
                ApproachMapY = GuardMapY,
                Fly = false,
                ArrivalObjectDataId = GatekeeperNpcDataId,
                Range = 6f
            },
            new()
            {
                Kind = MultiAreaRouteStepKind.Interact, ObjectDataId = GatekeeperNpcDataId, Range = 6f, DismountFirst = true
            },
            new()
            {
                Kind = MultiAreaRouteStepKind.WaitForZone
            }
        ]
    };
}
