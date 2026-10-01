using Dalamud.Game.Text.SeStringHandling.Payloads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

internal static class DomanEnclaveRoute
{
    internal const uint YanxiaTerritoryId = 614;
    internal const uint DomanEnclaveTerritoryId = 759;
    private const uint NamaiAetheryteId = 111;
    private const uint EnclaveEntranceNpcDataId = 1019200;
    private const uint YanxiaMapId = 354;
    private const uint EnclaveInteriorMapId = 463;
    private const float EntranceMapX = 13.8f;
    private const float EntranceMapY = 7.2f;
    private const float InteriorMapX = 5.5f;
    private const float InteriorMapY = 4.6f;
    private static readonly uint[] DomanEnclaveTerritoryIds = [DomanEnclaveTerritoryId, 739, 682];

    internal static readonly MultiAreaRoute Route = new()
    {
        Name = "The Doman Enclave",
        TooltipHint =
            "Uses the Doman Enclave aetheryte when unlocked, otherwise Yanxia Namai and the enclave entrance.",
        ArrivalTerritoryIds = DomanEnclaveTerritoryIds,
        InteriorMapId = EnclaveInteriorMapId,
        InteriorMapX = InteriorMapX,
        InteriorMapY = InteriorMapY,
        Matches = location =>
            MultiAreaRouteMatchers.MatchesDestination(
                location,
                DomanEnclaveTerritoryIds,
                EnclaveInteriorMapId),
        Timeout = TimeSpan.FromSeconds(240),
        Steps =
        [
            new()
            {
                Kind = MultiAreaRouteStepKind.Teleport, AetheryteId = NamaiAetheryteId
            },
            new()
            {
                Kind = MultiAreaRouteStepKind.MoveTo,
                ApproachMapId = YanxiaMapId,
                ApproachMapX = EntranceMapX,
                ApproachMapY = EntranceMapY,
                Fly = false,
                ArrivalObjectDataId = EnclaveEntranceNpcDataId,
                Range = 8f
            },
            new()
            {
                Kind = MultiAreaRouteStepKind.Interact,
                ObjectDataId = EnclaveEntranceNpcDataId,
                Range = 8f,
                DismountFirst = true
            },
            new()
            {
                Kind = MultiAreaRouteStepKind.WaitForZone
            }
        ]
    };

    internal static uint ResolveDirectEnclaveAetheryteId() =>
        AetheryteHelper.FindDefaultUnlockedAetheryteForTerritory(DomanEnclaveTerritoryId);

    internal static uint ResolveYanxiaEntryTeleportAetheryteId()
    {
        if (AetheryteHelper.IsUnlockedForTravel(NamaiAetheryteId) &&
            AetheryteHelper.GetAetheryteTerritoryId(NamaiAetheryteId) == YanxiaTerritoryId)
        {
            return NamaiAetheryteId;
        }

        return AetheryteHelper.FindDefaultUnlockedAetheryteForTerritory(YanxiaTerritoryId);
    }
}

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
