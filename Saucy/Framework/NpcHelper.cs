using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Saucy.IPC;
using System.Collections.Generic;
namespace Saucy.Framework;

public static unsafe class NpcHelper
{
    private static readonly Dictionary<string, HashSet<uint>> TrackedScopes = [];

    public static void SetTrackedNpcs(string scope, IEnumerable<uint> baseIds, string? logLabel = null)
    {
        TrackedScopes[scope] = [.. baseIds];

        if (!string.IsNullOrWhiteSpace(logLabel))
        {
            Svc.Log.Information($"[{logLabel}] Tracking ENpcBase ids: {string.Join(", ", TrackedScopes[scope])}");
        }
    }

    public static void ClearTrackedNpcs(string scope) => TrackedScopes.Remove(scope);

    public static bool IsTargeting(string scope) =>
        TrackedScopes.TryGetValue(scope, out var npcIds) && IsTargeting(npcIds);

    public static bool IsTargeting(IEnumerable<uint> baseIds) =>
        IsTargeting([.. baseIds]);

    /// <summary>
    ///     Broker/target NPC is selected and Talk or SelectString is open — dialogue was initiated.
    /// </summary>
    public static bool HasInitiatedDialogue(string scope) =>
        IsTargeting(scope) &&
        (TalkHelper.IsVisible() || SelectStringHelper.IsNpcListMenuVisible());

    private static bool IsTargeting(HashSet<uint> npcIds) =>
        IsTrackedNpc(Svc.Targets.Target, npcIds) ||
        IsTrackedNpc(Svc.Targets.SoftTarget, npcIds);

    private static bool IsTrackedNpc(IGameObject? obj, HashSet<uint> npcIds) =>
        obj != null && npcIds.Contains(obj.BaseId);

    public static IGameObject? FindNearestNpc(uint baseId, float maxDistance = float.MaxValue)
    {
        IGameObject? closest = null;
        var closestDist = float.MaxValue;

        foreach (var obj in Svc.Objects)
        {
            if (obj.BaseId != baseId)
            {
                continue;
            }

            var dist = Player.DistanceTo(obj);
            if (dist <= maxDistance && dist < closestDist)
            {
                closestDist = dist;
                closest = obj;
            }
        }

        return closest;
    }

    public static bool TryMoveToNpc(uint baseId, float closeRange = Vnavmesh.NpcCloseRange)
    {
        var npc = FindNearestNpc(baseId);
        if (npc == null || !Vnavmesh.IsInstalled)
        {
            return false;
        }

        return Vnavmesh.TryMoveTo(npc.Position, false, closeRange);
    }

    public static bool HasArrivedAtNpc(IGameObject npc, float closeRange = Vnavmesh.NpcCloseRange) =>
        Vnavmesh.TickArrival(npc.Position, closeRange);

    public static bool TryInteractWithNpc(
        uint baseId,
        float interactRange = Vnavmesh.NpcCloseRange,
        string throttleKey = "Saucy.Npc.Interact")
    {
        if (!Player.Interactable)
        {
            return false;
        }

        var npc = FindNearestNpc(baseId, interactRange + 1f);
        if (npc == null || Player.DistanceTo(npc) > interactRange)
        {
            return false;
        }

        if (Svc.Targets.Target?.BaseId != baseId)
        {
            if (EzThrottler.Throttle($"{throttleKey}.Target", 400))
            {
                Svc.Targets.Target = npc;
            }

            return false;
        }

        if (!EzThrottler.Throttle(throttleKey, 600))
        {
            return false;
        }

        TargetSystem.Instance()->InteractWithObject((GameObject*)npc.Address, false);
        return true;
    }
}
