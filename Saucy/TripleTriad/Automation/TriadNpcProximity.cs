using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using System;
using System.Numerics;
namespace Saucy.TripleTriad;

internal static class TriadNpcProximity
{
    public const float DefaultRange = 6f;

    public static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    public static bool IsPlayerNearCurrentTarget(float maxDistance = DefaultRange)
    {
        var target = Svc.Targets.Target;
        if (target == null)
        {
            return false;
        }

        var triadNpc = TriadNpcDB.Get().FindMatchingName(target.Name.TextValue);
        return triadNpc != null &&
               HorizontalDistance(Player.Position, target.Position) <= maxDistance;
    }

    public static bool IsPlayerNear(TriadNpc npc, float maxDistance = DefaultRange) =>
        FindNearbyObject(npc, maxDistance) != null;

    public static IGameObject? FindNearbyObject(TriadNpc npc, float maxDistance = DefaultRange)
    {
        if (npc == null)
        {
            return null;
        }

        var target = Svc.Targets.Target;
        if (target != null &&
            npc.IsMatchingName(target.Name.TextValue) &&
            HorizontalDistance(Player.Position, target.Position) <= maxDistance)
        {
            return target;
        }

        IGameObject? closest = null;
        var closestDist = float.MaxValue;

        foreach (var obj in Svc.Objects)
        {
            if (obj.ObjectKind != ObjectKind.EventNpc)
            {
                continue;
            }

            if (!npc.IsMatchingName(obj.Name.ToString()))
            {
                continue;
            }

            var dist = HorizontalDistance(Player.Position, obj.Position);
            if (dist <= maxDistance && dist < closestDist)
            {
                closestDist = dist;
                closest = obj;
            }
        }

        return closest;
    }

    public static TriadNpc? ResolveTriadNpcForProximityCheck()
    {
        var fromTarget = TriadTargetNpc.FromWorldTarget();
        if (fromTarget != null)
        {
            return fromTarget;
        }

        return TriadTargetNpc.FromRunContext(TriadRunTarget.Resolve());
    }

    public static bool IsRelevantTriadNpcNearby(float maxDistance = DefaultRange) =>
        IsPlayerNearCurrentTarget(maxDistance) ||
        (ResolveTriadNpcForProximityCheck() is { } npc && IsPlayerNear(npc, maxDistance));
}
