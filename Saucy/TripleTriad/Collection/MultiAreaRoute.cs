using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.Automation;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;
using Saucy.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetheryteSheet = Lumina.Excel.Sheets.Aetheryte;

namespace Saucy.TripleTriad;

internal enum MultiAreaRouteStepKind
{
    Teleport,
    Mount,
    MoveTo,
    Interact,
    WaitForZone
}

internal sealed class MultiAreaRouteStep
{
    public required MultiAreaRouteStepKind Kind { get; init; }
    public uint AetheryteId { get; init; }
    public Vector3 Position { get; init; }
    public bool Fly { get; init; }
    public uint ObjectDataId { get; init; }
    public uint ArrivalObjectDataId { get; init; }
    public float Range { get; init; } = 6f;
    public bool DismountFirst { get; init; }
}

internal sealed class MultiAreaRoute
{
    public required string Name { get; init; }
    public string? TooltipHint { get; init; }
    public required Func<MapLinkPayload, bool> Matches { get; init; }
    public required IReadOnlyList<MultiAreaRouteStep> Steps { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(180);
}

internal static class MultiAreaRouteRegistry
{
    private static readonly IReadOnlyList<MultiAreaRoute> Routes =
    [
        JeunoFirstWalkRoute.Route
    ];

    public static MultiAreaRoute? FindRoute(MapLinkPayload location) =>
        Routes.FirstOrDefault(route => route.Matches(location));

    public static bool MatchesDestination(MapLinkPayload location) => FindRoute(location) != null;
}

internal static unsafe class MultiAreaRouteExecutor
{
    internal sealed class RouteExecution
    {
        public required MultiAreaRoute Route;
        public required MultiAreaRouteContext Context;
        public int StepIndex;
        public bool StepActionStarted;
        public bool Failed;
        public DateTime StepStartedUtc;
    }

    internal sealed class MultiAreaRouteContext
    {
        public required MapLinkPayload Location;
        public required Vector3 Destination;
        public required uint TargetTerritoryId;
    }

    public static bool TryBeginRoute(
        MultiAreaRoute route,
        MultiAreaRouteContext context,
        out RouteExecution execution,
        out string? beginMessage)
    {
        execution = null!;
        beginMessage = null;

        var startIndex = GetFirstIncompleteStepIndex(route, context);
        if (startIndex < 0)
        {
            return false;
        }

        execution = new()
        {
            Route = route,
            Context = context,
            StepIndex = startIndex,
            StepStartedUtc = DateTime.UtcNow
        };

        var step = route.Steps[startIndex];
        if (step.Kind == MultiAreaRouteStepKind.Teleport)
        {
            if (!Lifestream.TryTeleport(step.AetheryteId))
            {
                Svc.Chat.PrintError($"[Saucy] Lifestream could not teleport for {route.Name}.");
                return false;
            }

            beginMessage =
                $"[Saucy] Teleporting for {route.Name}, then moving to {context.Location.CoordinateString}.";
            return true;
        }

        beginMessage =
            $"[Saucy] Entering {route.Name}, then moving to {context.Location.CoordinateString}.";
        return true;
    }

    public static bool Tick(RouteExecution execution)
    {
        if (execution.Failed || execution.StepIndex >= execution.Route.Steps.Count)
        {
            return !execution.Failed && execution.StepIndex >= execution.Route.Steps.Count;
        }

        var step = execution.Route.Steps[execution.StepIndex];
        var complete = step.Kind switch
        {
            MultiAreaRouteStepKind.Teleport => TickTeleport(step),
            MultiAreaRouteStepKind.Mount => TickMount(),
            MultiAreaRouteStepKind.MoveTo => TickMoveTo(step, execution),
            MultiAreaRouteStepKind.Interact => TickInteract(step),
            MultiAreaRouteStepKind.WaitForZone => TickWaitForZone(execution),
            _ => false
        };

        if (execution.Failed)
        {
            return false;
        }

        if (!complete)
        {
            return false;
        }

        execution.StepIndex++;
        execution.StepActionStarted = false;
        execution.StepStartedUtc = DateTime.UtcNow;
        return execution.StepIndex >= execution.Route.Steps.Count;
    }

    public static bool IsTeleportStepComplete(RouteExecution execution, MultiAreaRouteStep teleportStep)
    {
        if (execution.StepIndex != 0 || teleportStep.Kind != MultiAreaRouteStepKind.Teleport)
        {
            return false;
        }

        return TickTeleport(teleportStep);
    }

    private static int GetFirstIncompleteStepIndex(MultiAreaRoute route, MultiAreaRouteContext context)
    {
        for (var i = 0; i < route.Steps.Count; i++)
        {
            var step = route.Steps[i];
            if (step.Kind == MultiAreaRouteStepKind.Teleport &&
                Svc.ClientState.TerritoryType == GetAetheryteTerritoryId(step.AetheryteId))
            {
                continue;
            }

            if (step.Kind == MultiAreaRouteStepKind.WaitForZone &&
                Svc.ClientState.TerritoryType == context.TargetTerritoryId)
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private static bool TickTeleport(MultiAreaRouteStep step)
    {
        if (Lifestream.IsBusyNow())
        {
            return false;
        }

        if (!Player.Interactable || IsBetweenAreas() || Player.IsAnimationLocked)
        {
            return false;
        }

        var territoryId = GetAetheryteTerritoryId(step.AetheryteId);
        return territoryId != 0 && Svc.ClientState.TerritoryType == territoryId;
    }

    private static bool TickMount()
    {
        if (!Player.Interactable || IsBetweenAreas())
        {
            return false;
        }

        return TryMountUp();
    }

    private static bool TickMoveTo(MultiAreaRouteStep step, RouteExecution execution)
    {
        if (!execution.StepActionStarted)
        {
            if (!Player.Interactable || IsBetweenAreas())
            {
                return false;
            }

            if (!Vnavmesh.IsNavReady())
            {
                Svc.Chat.PrintError("[Saucy] vnavmesh is not ready for this route step.");
                execution.Failed = true;
                return false;
            }

            var approachPoint = Vnavmesh.TryGetPointOnFloor(step.Position) ?? step.Position;
            if (!Vnavmesh.TryPathfindAndMoveTo(approachPoint, step.Fly))
            {
                Svc.Chat.PrintError("[Saucy] vnavmesh could not start movement for this route step.");
                execution.Failed = true;
                return false;
            }

            execution.StepActionStarted = true;
            return false;
        }

        if (Vnavmesh.IsPathfindInProgress() || Vnavmesh.IsPathRunning())
        {
            return false;
        }

        if (!Player.Interactable || Player.IsAnimationLocked)
        {
            return false;
        }

        var pointOnFloor = Vnavmesh.TryGetPointOnFloor(step.Position) ?? step.Position;
        return Vector3.Distance(Player.Position, pointOnFloor) <= 8f ||
               (step.ArrivalObjectDataId != 0 && FindObject(step.ArrivalObjectDataId) != null);
    }

    private static bool TickInteract(MultiAreaRouteStep step)
    {
        if (step.DismountFirst && !TryDismount())
        {
            return false;
        }

        var target = FindObject(step.ObjectDataId);
        if (target == null)
        {
            return false;
        }

        if (Vector3.Distance(Player.Position, target.Position) > step.Range)
        {
            return false;
        }

        if (Svc.Targets.Target?.BaseId != step.ObjectDataId)
        {
            Svc.Targets.Target = target;
            return false;
        }

        if (!EzThrottler.Throttle("SaucyRouteInteract"))
        {
            return false;
        }

        TargetSystem.Instance()->InteractWithObject((GameObject*)target.Address, false);
        return true;
    }

    private static bool TickWaitForZone(RouteExecution execution)
    {
        if (IsBetweenAreas() || !Player.Interactable || Lifestream.IsBusyNow())
        {
            return false;
        }

        if (Svc.ClientState.TerritoryType != execution.Context.TargetTerritoryId)
        {
            if (DateTime.UtcNow - execution.StepStartedUtc > TimeSpan.FromSeconds(30))
            {
                Svc.Chat.PrintError($"[Saucy] Did not arrive in {execution.Route.Name} after zone transition.");
                execution.Failed = true;
            }

            return false;
        }

        return true;
    }

    private static bool TryMountUp()
    {
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (Svc.Condition[ConditionFlag.MountOrOrnamentTransition] || Svc.Condition[ConditionFlag.Casting])
        {
            EzThrottler.Throttle("SaucyRouteMountWait", 2000, true);
        }

        if (!EzThrottler.Check("SaucyRouteMountWait"))
        {
            return false;
        }

        if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 9) != 0)
        {
            return true;
        }

        if (Player.IsAnimationLocked || !EzThrottler.Throttle("SaucyRouteMount"))
        {
            return false;
        }

        Chat.ExecuteGeneralAction(9);
        return false;
    }

    private static bool TryDismount()
    {
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (Player.IsAnimationLocked || !EzThrottler.Throttle("SaucyRouteDismount"))
        {
            return false;
        }

        Chat.ExecuteGeneralAction(8);
        return false;
    }

    private static IGameObject? FindObject(uint dataId) =>
        Svc.Objects
            .Where(o => o.IsTargetable && o.BaseId == dataId)
            .OrderBy(o => Vector3.Distance(Player.Position, o.Position))
            .FirstOrDefault();

    private static uint GetAetheryteTerritoryId(uint aetheryteId)
    {
        var row = Svc.Data.GetExcelSheet<AetheryteSheet>()?.GetRowOrDefault(aetheryteId);
        return row?.Territory.RowId ?? 0;
    }

    private static bool IsBetweenAreas() => Svc.Condition[ConditionFlag.BetweenAreas];
}

internal static class JeunoFirstWalkRoute
{
    private const uint MamookAetheryteId = 206;
    private const uint EntranceDataId = 2014450;
    private static readonly Vector3 YakTelPortalApproachPoint = new(-527.2f, -152.4f, 668.5f);

    internal static readonly MultiAreaRoute Route = new()
    {
        Name = "Jeuno: The First Walk",
        TooltipHint = "Lower Jeuno routes via Mamook and the Yak T'el portal.",
        Matches = location =>
        {
            var placeName = location.PlaceName.ToString();
            return placeName.Contains("Lower Jeuno", StringComparison.OrdinalIgnoreCase) ||
                   placeName.Contains("Jeuno: The First Walk", StringComparison.OrdinalIgnoreCase);
        },
        Timeout = TimeSpan.FromSeconds(180),
        Steps =
        [
            new() { Kind = MultiAreaRouteStepKind.Teleport, AetheryteId = MamookAetheryteId },
            new() { Kind = MultiAreaRouteStepKind.Mount },
            new()
            {
                Kind = MultiAreaRouteStepKind.MoveTo,
                Position = YakTelPortalApproachPoint,
                Fly = true,
                ArrivalObjectDataId = EntranceDataId
            },
            new() { Kind = MultiAreaRouteStepKind.Interact, ObjectDataId = EntranceDataId, Range = 6f, DismountFirst = true },
            new() { Kind = MultiAreaRouteStepKind.WaitForZone }
        ]
    };
}
