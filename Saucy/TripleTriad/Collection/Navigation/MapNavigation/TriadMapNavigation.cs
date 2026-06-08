using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.GameHelpers;
using Saucy.Framework;
using Saucy.IPC;
using System;
using System.Numerics;
namespace Saucy.TripleTriad;

internal static unsafe partial class TriadMapNavigation
{
    private const float NpcInteractionRange = 6f;

    private const float NpcPathArrivalRange = 3f;

    private const float NpcMinStandoffDistance = 2.75f;
    private const float NpcBackoffDistance = 4.5f;
    private static readonly TimeSpan DefaultNavigationTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan NavMeshBuildWaitTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PathfindStartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MountBeforeNavTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AethernetStartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AethernetSettleDelay = TimeSpan.FromSeconds(1.5);
    private static readonly float AethernetMinMoveDistance = 3f;
    private static readonly float AethernetNearShardDistance = 8f;

    private static PendingNavigation? _pending;
    private static TriadNavigationGoal _activeNavigationGoal = TriadNavigationGoal.FarmCards;
    private static bool _frameworkSubscribed;

    public static bool IsNavigationActive => _pending != null;

    public static bool IsExecutingMultiAreaRoute
        => _pending?.Phase == NavigationPhase.ExecutingRoute;

    public static bool IsInNavigationTargetTerritory() =>
        _pending == null ||
        _pending.TargetTerritoryId == 0 ||
        IsInTargetTerritory(_pending.TargetTerritoryId);

    public static void HandleMapClick(
        MapLinkPayload location,
        TriadNpc? npc = null,
        bool fly = true,
        TriadNavigationGoal goal = TriadNavigationGoal.FarmCards)
    {
        // Pre-flight unlock check — runs BEFORE anything else (Battle Hall block, duplicate-navigation
        // check, deck-optimizer prep, teleport). If the NPC's prerequisite quest isn't complete, refuse
        // to do anything: no teleport, no walk, no interaction spam.
        if (TryRejectLockedNpcOnClick(npc))
        {
            return;
        }

        if (TriadBattleHall.ShouldBlockMapNavigation(npc, location))
        {
            TriadBattleHall.PrintNavigationBlocked();
            return;
        }

        if (npc != null &&
            IsNavigationActive &&
            _pending?.Npc?.Id == npc.Id &&
            _pending?.NavigationGoal == goal)
        {
            return;
        }

        _activeNavigationGoal = goal;

        if (!TryBeginNavigation(location, fly, npc))
        {
            Svc.GameGui.OpenMapWithMapLink(location);
            return;
        }

        if (npc != null)
        {
            TriadRunSession.PrepareNavigationRunMode(npc, goal);
            TriadRun.OnNpcSelected(npc, [], startOptimizer: true, forNavigation: true);
        }
    }

    private static bool TryRejectLockedNpcOnClick(TriadNpc? npc) =>
        TriadNpcUnlockHelper.TryRejectForClick(npc, out var _);

    public static bool TryRejectLockedNpcDuringNavigation(TriadNpc? npc)
    {
        if (!TriadNpcUnlockHelper.TryReject(npc, out var reason))
        {
            return false;
        }

        TriadNpcUnlockHelper.Announce(reason);
        CancelActiveNavigation();
        return true;
    }

    public static void CancelActiveNavigation()
    {
        TriadRunSession.ClearNavigationDeckBuildOverride();

        if (_pending?.RouteExecution != null)
        {
            _pending.RouteExecution.Failed = true;
        }

        StopVnavIfRunning();
        Lifestream.TryAbort();
        ClearPending();
    }

    public static void Tick()
    {
        var pending = _pending;
        if (pending == null)
        {
            return;
        }

        if (TriadUiState.IsAutomationFlowActive())
        {
            CancelActiveNavigation();
            return;
        }

        if (TryRejectLockedNpcDuringNavigation(pending.Npc))
        {
            return;
        }

        if (pending.Npc != null)
        {
            TriadRun.EnsureNavigationDeckOptimizerStarted(pending.Npc);
        }

        TriadRun.SetNavigationOptimizerPause(
            pending.Phase == NavigationPhase.WaitingForNavReady &&
            !Vnavmesh.IsNavReady());

        var timeout = pending.RouteExecution?.Route.Timeout ?? DefaultNavigationTimeout;
        var shouldWaitForDeckOptimizer =
            pending.Phase is NavigationPhase.MovingToNpc or NavigationPhase.StartingTriadMatch;
        var waitingForDeckOptimizer =
            shouldWaitForDeckOptimizer &&
            pending.Npc != null &&
            TriadRun.IsNavigationBlockedWaitingForOptimizer(pending.Npc);
        var waitingForNavMesh =
            pending.Phase == NavigationPhase.WaitingForNavReady && !Vnavmesh.IsNavReady();
        var waitingNearNpc =
            pending.Npc != null &&
            pending.Phase is NavigationPhase.MovingToNpc or NavigationPhase.StartingTriadMatch &&
            IsPlayerWithinNpcInteractionRange(pending);
        if (waitingForDeckOptimizer || waitingForNavMesh || waitingNearNpc)
        {
            pending.StartedUtc = DateTime.UtcNow;
        }
        else if (DateTime.UtcNow - pending.StartedUtc > timeout)
        {
            Svc.Chat.PrintError("[Saucy] Navigation timed out.");
            ClearPending();
            return;
        }

        switch (pending.Phase)
        {
            case NavigationPhase.WaitingForLifestream:
                SuppressVnavDuringLifestream();
                if (!IsLifestreamTravelComplete(pending))
                {
                    return;
                }

                if (pending.RouteExecution != null)
                {
                    pending.Phase = NavigationPhase.ExecutingRoute;
                    pending.PhaseStartedUtc = DateTime.UtcNow;
                    return;
                }

                if (TryBeginPendingAethernet(pending))
                {
                    return;
                }

                if (!IsInTargetTerritory(pending.TargetTerritoryId))
                {
                    return;
                }

                RefreshPendingDestination(pending);
                if (TryBeginMovingToNpcIfAlreadyNearby(pending))
                {
                    return;
                }

                pending.Phase = NavigationPhase.WaitingForNavReady;
                pending.PhaseStartedUtc = DateTime.UtcNow;
                pending.AttemptMountBeforeNav = true;
                return;

            case NavigationPhase.ExecutingRoute:
                if (pending.RouteExecution == null)
                {
                    ClearPending();
                    return;
                }

                if (pending.RouteExecution.Failed)
                {
                    ClearPending();
                    return;
                }

                if (MultiAreaRouteExecutor.Tick(pending.RouteExecution))
                {
                    ContinueAfterZoneArrival(pending);
                }

                return;

            case NavigationPhase.WaitingForAethernet:
                SuppressVnavDuringLifestream();
                if (!IsLifestreamTravelComplete(pending))
                {
                    return;
                }

                pending.PendingAethernetShardId = 0;
                pending.PendingAethernetShardName = null;
                RefreshPendingDestination(pending);
                if (TryBeginMovingToNpcIfAlreadyNearby(pending))
                {
                    return;
                }

                pending.Phase = NavigationPhase.WaitingForNavReady;
                pending.PhaseStartedUtc = DateTime.UtcNow;
                pending.AttemptMountBeforeNav = true;
                return;

            case NavigationPhase.ApproachingAethernetHub:
                TickApproachingAethernetHub(pending);
                return;

            case NavigationPhase.WaitingForNavReady:
                TickWaitingForNavReady(pending);
                return;

            case NavigationPhase.MovingToNpc:
                TickMovingToNpc(pending);
                return;

            case NavigationPhase.StartingTriadMatch:
                TickStartingTriadMatch(pending);
                return;
        }
    }
    private static void BeginPending(
        MapLinkPayload location,
        Vector3 destination,
        bool fly,
        uint targetTerritoryId,
        TriadNpc? npc = null,
        MultiAreaRouteExecutor.RouteExecution? routeExecution = null,
        uint aethernetShardId = 0,
        string? aethernetShardName = null,
        uint activeAethernetShardId = 0,
        uint hubAetheryteId = 0,
        uint expectedPostTeleportTerritoryId = 0,
        NavigationPhase startingPhase = NavigationPhase.WaitingForLifestream)
    {
        if (startingPhase is NavigationPhase.WaitingForLifestream or NavigationPhase.WaitingForAethernet)
        {
            StopVnavIfRunning();
        }

        _pending = new()
        {
            Location = location,
            Destination = destination,
            Fly = fly,
            Npc = npc,
            NavigationGoal = _activeNavigationGoal,
            TargetTerritoryId = targetTerritoryId,
            RouteExecution = routeExecution,
            PendingAethernetShardId = aethernetShardId,
            PendingAethernetShardName = aethernetShardName,
            ActiveAethernetShardId = activeAethernetShardId,
            HubAetheryteId = hubAetheryteId,
            ExpectedPostTeleportTerritoryId = expectedPostTeleportTerritoryId,
            AethernetStartPosition = activeAethernetShardId != 0 ? Player.Position : null,
            AethernetShardPosition = activeAethernetShardId != 0
                ? AetheryteHelper.GetAethernetShardWorldPosition(activeAethernetShardId)
                : null,
            Phase = startingPhase,
            StartedUtc = DateTime.UtcNow,
            PhaseStartedUtc = DateTime.UtcNow,
            AethernetSeenBusy = false,
            AethernetBusyClearedUtc = null
        };

        EnsureFrameworkSubscription();
    }

    public static bool TryGetPendingNpc(out TriadNpc? npc)
    {
        npc = _pending?.Npc;
        return npc != null;
    }

    public static bool IsAwaitingTriadStartDialog() =>
        _pending?.Phase == NavigationPhase.StartingTriadMatch;

    public static bool TryAdvanceTriadStartDialog()
    {
        if (TalkHelper.IsVisible())
        {
            if (!IsStableForNpcCommands())
            {
                return false;
            }

            if (SelectYesnoHelper.TryGetTriadYesno(out var talkYesno) &&
                SelectYesnoHelper.PressYes(talkYesno))
            {
                TriadNpcGate.MarkDialogueFlow();
                return true;
            }

            if (TalkHelper.TryAdvance("SaucyNavTalk"))
            {
                TriadNpcGate.MarkDialogueFlow();
                return true;
            }

            return false;
        }

        if (SelectStringHelper.IsNpcListMenuVisible() &&
            SelectStringHelper.TrySelectTriadEntry())
        {
            TriadNpcGate.MarkDialogueFlow();
            return true;
        }

        if (!IsStableForNpcCommands())
        {
            return false;
        }

        if (SelectYesnoHelper.TryGetTriadYesno(out var yesno) &&
            SelectYesnoHelper.PressYes(yesno))
        {
            TriadNpcGate.MarkDialogueFlow();
            return true;
        }

        return false;
    }

    private static void ClearPending()
    {
        TriadRun.SetNavigationOptimizerPause(false);
        StopVnavIfRunning();
        _pending = null;
    }
    private static bool IsBetweenAreas() => Svc.Condition[ConditionFlag.BetweenAreas];

    private static bool CanBeginLocalNavigation() =>
        Player.Interactable &&
        !IsBetweenAreas() &&
        !Player.IsAnimationLocked &&
        !Svc.Condition[ConditionFlag.Jumping] &&
        !Svc.Condition[ConditionFlag.MountOrOrnamentTransition] &&
        !Svc.Condition[ConditionFlag.Casting];

    private static bool IsStableForNpcCommands() =>
        !Svc.Condition[ConditionFlag.Mounted] &&
        CanBeginLocalNavigation();

    private static bool IsReadyForNpcInteraction() =>
        EnsureDismountedForNpcInteraction() && IsStableForNpcCommands();

    private static void EnsureFrameworkSubscription()
    {
        if (_frameworkSubscribed)
        {
            return;
        }

        Svc.Framework.Update += OnFrameworkUpdate;
        _frameworkSubscribed = true;
    }

    private static void OnFrameworkUpdate(IFramework _)
    {
        Tick();

        if (_pending == null && _frameworkSubscribed)
        {
            Svc.Framework.Update -= OnFrameworkUpdate;
            _frameworkSubscribed = false;
        }
    }

    private enum NavigationPhase
    {
        WaitingForLifestream,
        ExecutingRoute,
        WaitingForAethernet,
        ApproachingAethernetHub,
        WaitingForNavReady,
        MovingToNpc,
        StartingTriadMatch
    }

    private sealed class PendingNavigation
    {
        public uint ActiveAethernetShardId;
        public DateTime? AethernetBusyClearedUtc;
        public bool AethernetSeenBusy;
        public Vector3? AethernetShardPosition;
        public Vector3? AethernetStartPosition;
        public bool AnnouncedTriadStart;
        public bool ArrivedViaMultiAreaRoute;
        public bool AttemptMountBeforeNav;
        public required Vector3 Destination;
        public uint ExpectedPostTeleportTerritoryId;
        public required bool Fly;
        public uint HubAetheryteId;
        public int LastAnnouncedBuildProgress = -1;
        public required MapLinkPayload Location;
        public TriadNavigationGoal NavigationGoal;
        public bool NavMeshWaitAnnounced;
        public bool NavMeshWasReady;
        public TriadNpc? Npc;
        public int NpcInteractionAttempts;
        public uint PendingAethernetShardId;
        public string? PendingAethernetShardName;
        public NavigationPhase Phase;
        public DateTime PhaseStartedUtc;
        public MultiAreaRouteExecutor.RouteExecution? RouteExecution;
        public DateTime StartedUtc;
        public required uint TargetTerritoryId;
        public int VnavRetryCount;
    }
}
