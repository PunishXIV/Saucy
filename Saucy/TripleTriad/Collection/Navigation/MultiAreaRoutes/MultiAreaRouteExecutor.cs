using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.Automation;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Saucy.Framework;
using Saucy.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AetheryteSheet = Lumina.Excel.Sheets.Aetheryte;

namespace Saucy.TripleTriad;

internal static class AethernetShardLookup
{
    private static readonly Dictionary<string, uint> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static uint Resolve(string shardName)
    {
        if (string.IsNullOrEmpty(shardName))
        {
            return 0;
        }

        if (Cache.TryGetValue(shardName, out var cached))
        {
            return cached;
        }

        var sheet = Svc.Data.GetExcelSheet<AetheryteSheet>();
        if (sheet == null)
        {
            return 0;
        }

        foreach (var row in sheet)
        {
            if (row.IsAetheryte)
            {
                continue;
            }

            var name = row.AethernetName.ValueNullable?.Name.ToString();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (name.Equals(shardName, StringComparison.OrdinalIgnoreCase))
            {
                Cache[shardName] = row.RowId;
                return row.RowId;
            }
        }

        return 0;
    }
}

internal static unsafe class MultiAreaRouteExecutor
{
    private const uint GeneralActionDismount = 23;

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
            Route = route, Context = context, StepIndex = startIndex, StepStartedUtc = DateTime.UtcNow
        };

        var step = route.Steps[startIndex];
        if (step.Kind == MultiAreaRouteStepKind.Teleport)
        {
            var aetheryteId = step.AetheryteId;
            if (route.Name == DomanEnclaveRoute.Route.Name)
            {
                aetheryteId = DomanEnclaveRoute.ResolveYanxiaEntryTeleportAetheryteId();
            }

            if (aetheryteId == 0)
            {
                return false;
            }

            if (AetheryteHelper.IsPlayerInAetheryteTerritory(aetheryteId))
            {
                beginMessage = $"[Saucy] Entering {route.Name}, then moving to the NPC.";
                return true;
            }

            if (!Lifestream.TryTeleport(aetheryteId))
            {
                Svc.Chat.PrintError($"[Saucy] Lifestream could not teleport for {route.Name}.");
                return false;
            }

            beginMessage =
                $"[Saucy] Teleporting for {route.Name}, then moving to the NPC.";
            return true;
        }

        beginMessage =
            $"[Saucy] Entering {route.Name}, then moving to the NPC.";
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
            MultiAreaRouteStepKind.Teleport => TickTeleport(execution.Route, step),
            MultiAreaRouteStepKind.Aethernet => TickAethernet(step, execution),
            MultiAreaRouteStepKind.Mount => TickMount(),
            MultiAreaRouteStepKind.MoveTo => TickMoveTo(step, execution),
            MultiAreaRouteStepKind.Interact => TickInteract(step),
            MultiAreaRouteStepKind.SelectYesno => TickSelectYesno(step),
            MultiAreaRouteStepKind.WaitForZone => TickWaitForZone(execution),
            var _ => false
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

        return TickTeleport(execution.Route, teleportStep);
    }

    private static uint ResolveTeleportAetheryteId(MultiAreaRoute route, MultiAreaRouteStep step) =>
        route.Name == DomanEnclaveRoute.Route.Name
            ? DomanEnclaveRoute.ResolveYanxiaEntryTeleportAetheryteId()
            : step.AetheryteId;

    private static int GetFirstIncompleteStepIndex(MultiAreaRoute route, MultiAreaRouteContext context)
    {
        for (var i = 0; i < route.Steps.Count; i++)
        {
            var step = route.Steps[i];
            if (step.Kind == MultiAreaRouteStepKind.Teleport &&
                Svc.ClientState.TerritoryType == GetAetheryteTerritoryId(ResolveTeleportAetheryteId(route, step)))
            {
                continue;
            }

            if (step.Kind == MultiAreaRouteStepKind.WaitForZone &&
                route.IsInDestinationTerritory(Svc.ClientState.TerritoryType, context.TargetTerritoryId))
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private static bool TickTeleport(MultiAreaRoute route, MultiAreaRouteStep step)
    {
        if (Lifestream.IsBusyNow())
        {
            return false;
        }

        if (!Player.Interactable || IsBetweenAreas() || Player.IsAnimationLocked)
        {
            return false;
        }

        var aetheryteId = ResolveTeleportAetheryteId(route, step);
        var territoryId = GetAetheryteTerritoryId(aetheryteId);
        return territoryId != 0 && Svc.ClientState.TerritoryType == territoryId;
    }

    private static bool TickAethernet(MultiAreaRouteStep step, RouteExecution execution)
    {
        var shardId = ResolveShardId(step);
        if (shardId == 0)
        {
            Svc.Chat.PrintError($"[Saucy] Could not resolve aethernet shard \"{step.AethernetShardName}\".");
            execution.Failed = true;
            return false;
        }

        if (!execution.StepActionStarted)
        {
            var shardPos = AetheryteHelper.GetAethernetShardWorldPosition(shardId);
            if (shardPos != null && Vector3.Distance(Player.Position, shardPos.Value) <= 10f)
            {
                return true;
            }

            if (Lifestream.IsBusyNow() || !Player.Interactable || IsBetweenAreas() || Player.IsAnimationLocked)
            {
                return false;
            }

            if (!Lifestream.TryAethernetViaLiCommand(
                step.AethernetShardName ?? AetheryteHelper.GetAethernetShardName(shardId)))
            {
                Svc.Chat.PrintError($"[Saucy] Lifestream could not aethernet for {execution.Route.Name}.");
                execution.Failed = true;
                return false;
            }

            execution.StepActionStarted = true;
            execution.StepStartedUtc = DateTime.UtcNow;
            return false;
        }

        if (Lifestream.IsBusyNow() || IsBetweenAreas() || !Player.Interactable || Player.IsAnimationLocked)
        {
            return false;
        }

        return DateTime.UtcNow - execution.StepStartedUtc > TimeSpan.FromSeconds(2);
    }

    private static uint ResolveShardId(MultiAreaRouteStep step)
    {
        if (!string.IsNullOrEmpty(step.AethernetShardName))
        {
            return AethernetShardLookup.Resolve(step.AethernetShardName);
        }

        return step.AetheryteId;
    }

    private static bool TickSelectYesno(MultiAreaRouteStep step)
    {
        if (!SelectYesnoHelper.TryGetVisible(out var yesno))
        {
            return false;
        }

        if (!SelectYesnoHelper.IsRouteSafeYesno(yesno))
        {
            return false;
        }

        if (!EzThrottler.Throttle("SaucyRouteYesno"))
        {
            return false;
        }

        if (!SelectYesnoHelper.PressYes(yesno))
        {
            return false;
        }

        return true;
    }

    private static bool TickMount()
    {
        if (!TravelMountHelper.CanMountInCurrentTerritory())
        {
            return true;
        }

        if (!Player.Interactable || IsBetweenAreas())
        {
            return false;
        }

        return TravelMountHelper.TryMountUp();
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
                Vnavmesh.TryEnsureNavMeshLoading();
                return false;
            }

            var approachPoint = TriadMapNavigation.ResolveNpcPathPoint(ResolveApproachPoint(step));
            var fly = TravelMountHelper.ResolveUseFlying(step.Fly);
            if (!Vnavmesh.TryMoveTo(approachPoint, fly, step.Range) &&
                fly &&
                !Vnavmesh.TryMoveTo(approachPoint, false, step.Range))
            {
                Svc.Chat.PrintError("[Saucy] vnavmesh could not start movement for this route step.");
                execution.Failed = true;
                return false;
            }

            if (!Vnavmesh.IsMoving() && !Vnavmesh.IsPathfindInProgress())
            {
                Svc.Chat.PrintError("[Saucy] vnavmesh could not start movement for this route step.");
                execution.Failed = true;
                return false;
            }

            execution.StepActionStarted = true;
            return false;
        }

        if (!Player.Interactable || Player.IsAnimationLocked)
        {
            return false;
        }

        if (step.ArrivalObjectDataId != 0)
        {
            var target = FindObject(step.ArrivalObjectDataId);
            if (target != null && Vnavmesh.TickArrival(target.Position, step.Range))
            {
                return TryCompleteMoveToStep(step);
            }
        }
        else
        {
            var approachPoint = TriadMapNavigation.ResolveNpcPathPoint(ResolveApproachPoint(step));
            if (Vnavmesh.TickArrival(approachPoint, step.Range))
            {
                return TryCompleteMoveToStep(step);
            }
        }

        return false;
    }

    private static bool TryCompleteMoveToStep(MultiAreaRouteStep step)
    {
        if (step.Fly && !TravelMountHelper.TryDismount())
        {
            return false;
        }

        return true;
    }

    private static Vector3 ResolveApproachPoint(MultiAreaRouteStep step)
    {
        if (step.ArrivalObjectDataId != 0)
        {
            var target = FindObject(step.ArrivalObjectDataId);
            if (target != null)
            {
                return target.Position;
            }
        }

        if (step.ApproachMapId != 0)
        {
            var fromMap = TriadMapNavigation.GetWorldPositionFromMap(
                step.ApproachMapId, step.ApproachMapX, step.ApproachMapY);
            if (fromMap != null)
            {
                return fromMap.Value;
            }
        }

        return step.Position;
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
        if (!Player.Interactable || Lifestream.IsBusyNow())
        {
            return false;
        }

        if (execution.Route.IsInDestinationTerritory(
            Svc.ClientState.TerritoryType, execution.Context.TargetTerritoryId))
        {
            return true;
        }

        if (IsBetweenAreas())
        {
            return false;
        }

        if (SelectYesnoHelper.TryGetVisible(out var yesno) &&
            SelectYesnoHelper.IsRouteSafeYesno(yesno) &&
            EzThrottler.Throttle("SaucyRouteYesno"))
        {
            SelectYesnoHelper.PressYes(yesno);
            return false;
        }

        if (TalkHelper.TryAdvance("SaucyRouteTalk"))
        {
            return false;
        }

        if (DateTime.UtcNow - execution.StepStartedUtc > TimeSpan.FromSeconds(30))
        {
            Svc.Chat.PrintError($"[Saucy] Did not arrive in {execution.Route.Name} after zone transition.");
            execution.Failed = true;
        }

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

        Chat.ExecuteGeneralAction(GeneralActionDismount);
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

    internal sealed class RouteExecution
    {
        public required MultiAreaRouteContext Context;
        public bool Failed;
        public required MultiAreaRoute Route;
        public bool StepActionStarted;
        public int StepIndex;
        public DateTime StepStartedUtc;
    }

    internal sealed class MultiAreaRouteContext
    {
        public required Vector3 Destination;
        public required MapLinkPayload Location;
        public required uint TargetTerritoryId;
    }
}
