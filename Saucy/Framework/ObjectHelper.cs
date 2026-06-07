using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Collections.Generic;
using System.Numerics;
namespace Saucy.Framework;

public static unsafe class ObjectHelper
{
    private const float RememberedObjectMaxDistance = 6f;

    private static readonly Dictionary<string, HashSet<uint>> TrackedScopes = [];
    private static readonly Dictionary<string, Func<IGameObject, bool>> CustomMatchers = [];
    private static readonly Dictionary<string, ulong> LastKnownObjectIds = [];

    public static void SetTrackedObjects(
        string scope,
        IEnumerable<uint> baseIds,
        Func<IGameObject, bool>? matcher = null,
        string? logLabel = null)
    {
        TrackedScopes[scope] = [.. baseIds];
        if (matcher == null)
        {
            CustomMatchers.Remove(scope);
        }
        else
        {
            CustomMatchers[scope] = matcher;
        }

        if (!string.IsNullOrWhiteSpace(logLabel))
        {
            Svc.Log.Information($"[{logLabel}] Tracking object base ids: {string.Join(", ", TrackedScopes[scope])}");
        }
    }

    public static void ClearTrackedObjects(string scope)
    {
        TrackedScopes.Remove(scope);
        CustomMatchers.Remove(scope);
    }

    public static void RememberObject(string scope, IGameObject obj) =>
        LastKnownObjectIds[scope] = obj.GameObjectId;

    public static bool IsRememberedObject(string scope, IGameObject obj) =>
        LastKnownObjectIds.TryGetValue(scope, out var id) && obj.GameObjectId == id;

    public static bool TryGetRememberedObject(string scope, out IGameObject? obj)
    {
        obj = null;
        if (!LastKnownObjectIds.TryGetValue(scope, out var id))
        {
            return false;
        }

        foreach (var candidate in Svc.Objects)
        {
            if (candidate.GameObjectId == id)
            {
                obj = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool IsNearRememberedObject(string scope, Func<IGameObject, float> getDistance) =>
        TryGetRememberedObject(scope, out var obj) &&
        obj != null &&
        getDistance(obj) <= RememberedObjectMaxDistance;

    public static bool IsTargeting(string scope)
    {
        if (!TrackedScopes.TryGetValue(scope, out var baseIds))
        {
            return false;
        }

        var matcher = CustomMatchers.GetValueOrDefault(scope);
        return IsTargeting(scope, baseIds, matcher) ||
               (TryGetRememberedObject(scope, out var remembered) &&
                remembered != null &&
                (Svc.Targets.Target?.GameObjectId == remembered.GameObjectId ||
                 Svc.Targets.SoftTarget?.GameObjectId == remembered.GameObjectId));
    }

    private static bool MatchesObject(IGameObject? obj, string scope)
    {
        if (obj == null || !TrackedScopes.TryGetValue(scope, out var baseIds))
        {
            return false;
        }

        return MatchesObject(scope, obj, baseIds, CustomMatchers.GetValueOrDefault(scope));
    }

    /// <summary>
    ///     Machine is targeted and the Gold Saucer arcade start menu (SelectString) is open.
    /// </summary>
    public static bool HasInitiatedArcadeMenu(string scope) =>
        IsTargeting(scope) &&
        SelectStringHelper.TryGetArcadeMenu(out var menu) &&
        SelectStringHelper.IsArcadeYesnoMenu(menu);

    public static IGameObject? FindNearest(
        string scope,
        Func<IGameObject, float> getDistance,
        Func<IGameObject, float> getMaxDistance)
    {
        if (!TrackedScopes.TryGetValue(scope, out var baseIds))
        {
            return null;
        }

        var matcher = CustomMatchers.GetValueOrDefault(scope);
        IGameObject? nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var obj in Svc.Objects)
        {
            if (!MatchesObject(scope, obj, baseIds, matcher))
            {
                continue;
            }

            var maxDistance = getMaxDistance(obj);
            if (maxDistance <= 0f)
            {
                continue;
            }

            var distance = getDistance(obj);
            if (distance > maxDistance || distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearest = obj;
        }

        if (nearest != null)
        {
            RememberObject(scope, nearest);
            return nearest;
        }

        if (TryGetRememberedObject(scope, out var remembered) && remembered != null)
        {
            var rememberedDistance = getDistance(remembered);
            if (rememberedDistance <= RememberedObjectMaxDistance)
            {
                return remembered;
            }
        }

        return null;
    }

    public static bool TryInteractWithObject(
        IGameObject obj,
        string throttleKey = "Saucy.Object.Interact")
    {
        if (!Player.Interactable || obj == null)
        {
            return false;
        }

        if (Svc.Targets.Target?.Address != obj.Address)
        {
            if (EzThrottler.Throttle($"{throttleKey}.Target", 400))
            {
                Svc.Targets.Target = obj;
            }

            return false;
        }

        if (!EzThrottler.Throttle(throttleKey, 600))
        {
            return false;
        }

        TargetSystem.Instance()->InteractWithObject((GameObject*)obj.Address, false);
        return true;
    }

    public static float GetHorizontalEdgeDistance(IGameObject target)
    {
        var localPlayer = Svc.Objects.LocalPlayer;
        if (localPlayer is null || target.GameObjectId == localPlayer.GameObjectId)
        {
            return 0f;
        }

        var position = new Vector2(target.Position.X, target.Position.Z);
        var selfPosition = new Vector2(localPlayer.Position.X, localPlayer.Position.Z);
        return Math.Max(0f, Vector2.Distance(position, selfPosition) - target.HitboxRadius - localPlayer.HitboxRadius);
    }

    private static bool IsTargeting(
        string scope,
        HashSet<uint> baseIds,
        Func<IGameObject, bool>? matcher) =>
        IsTrackedObject(scope, Svc.Targets.Target, baseIds, matcher) ||
        IsTrackedObject(scope, Svc.Targets.SoftTarget, baseIds, matcher);

    private static bool IsTrackedObject(
        string scope,
        IGameObject? obj,
        HashSet<uint> baseIds,
        Func<IGameObject, bool>? matcher) =>
        obj != null && MatchesObject(scope, obj, baseIds, matcher);

    private static bool MatchesObject(
        string scope,
        IGameObject obj,
        HashSet<uint> baseIds,
        Func<IGameObject, bool>? matcher) =>
        IsRememberedObject(scope, obj) ||
        baseIds.Contains(obj.BaseId) ||
        (matcher?.Invoke(obj) ?? false);
}
