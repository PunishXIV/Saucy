using System;
using System.Numerics;
namespace Saucy.TripleTriad;

internal static class JeunoFirstWalkRoute
{
    private const uint MamookAetheryteId = 206;
    private const uint EntranceDataId = 2014450;
    private static readonly Vector3 YakTelPortalApproachPoint = new(-527.2f, -152.4f, 668.5f);
    // z6e1 / z6e1_2 only — do not include 1190–1192 (Shaaloani / Heritage Found / Windward Wilds).
    private static readonly uint[] LowerJeunoTerritoryIds = [1264, 1265];

    internal static readonly MultiAreaRoute Route = new()
    {
        Name = "Jeuno: The First Walk",
        TooltipHint = "Lower Jeuno routes via Mamook and the Yak T'el portal.",
        ArrivalTerritoryIds = LowerJeunoTerritoryIds,
        Matches = location =>
            MultiAreaRouteMatchers.MatchesDestination(location, LowerJeunoTerritoryIds),
        Timeout = TimeSpan.FromSeconds(180),
        Steps =
        [
            new()
            {
                Kind = MultiAreaRouteStepKind.Teleport, AetheryteId = MamookAetheryteId
            },
            new()
            {
                Kind = MultiAreaRouteStepKind.Mount
            },
            new()
            {
                Kind = MultiAreaRouteStepKind.MoveTo, Position = YakTelPortalApproachPoint, Fly = true, ArrivalObjectDataId = EntranceDataId
            },
            new()
            {
                Kind = MultiAreaRouteStepKind.Interact, ObjectDataId = EntranceDataId, Range = 6f, DismountFirst = true
            },
            new()
            {
                Kind = MultiAreaRouteStepKind.WaitForZone
            }
        ]
    };
}
