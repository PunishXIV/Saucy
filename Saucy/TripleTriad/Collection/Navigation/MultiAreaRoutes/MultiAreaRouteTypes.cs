using Dalamud.Game.Text.SeStringHandling.Payloads;
using System;
using System.Collections.Generic;
using System.Numerics;
namespace Saucy.TripleTriad;

internal enum MultiAreaRouteStepKind
{
    Teleport,
    Aethernet,
    Mount,
    MoveTo,
    Interact,
    SelectYesno,
    WaitForZone
}

internal sealed class MultiAreaRouteStep
{
    public required MultiAreaRouteStepKind Kind { get; init; }
    public uint AetheryteId { get; init; }
    public string? AethernetShardName { get; init; }
    public Vector3 Position { get; init; }
    public bool Fly { get; init; }
    public uint ObjectDataId { get; init; }
    public uint ArrivalObjectDataId { get; init; }
    public uint ApproachMapId { get; init; }
    public float ApproachMapX { get; init; }
    public float ApproachMapY { get; init; }
    public float Range { get; init; } = 6f;
    public bool DismountFirst { get; init; }
}

internal sealed class MultiAreaRoute
{
    public required string Name { get; init; }
    public string? TooltipHint { get; init; }
    public required Func<MapLinkPayload, bool> Matches { get; init; }
    public required IReadOnlyList<MultiAreaRouteStep> Steps { get; init; }
    public IReadOnlyList<uint>? ArrivalTerritoryIds { get; init; }
    public uint InteriorMapId { get; init; }
    public float InteriorMapX { get; init; }
    public float InteriorMapY { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(180);

    public bool IsInDestinationTerritory(uint territoryId, uint fallbackTargetTerritoryId)
    {
        if (territoryId == fallbackTargetTerritoryId)
        {
            return true;
        }

        if (ArrivalTerritoryIds is not { Count: > 0 })
        {
            return false;
        }

        foreach (var candidate in ArrivalTerritoryIds)
        {
            if (candidate == territoryId)
            {
                return true;
            }
        }

        return false;
    }
}
