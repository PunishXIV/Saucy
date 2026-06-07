using System;
namespace Saucy.TripleTriad;

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
        {
            var territoryId = location.TerritoryType.RowId;
            foreach (var arrivalId in DomanEnclaveTerritoryIds)
            {
                if (territoryId == arrivalId)
                {
                    return true;
                }
            }

            var placeName = location.PlaceName.ToString();
            return placeName.Contains("Doman Enclave", StringComparison.OrdinalIgnoreCase);
        },
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
