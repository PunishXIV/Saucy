using Dalamud.Game.ClientState.Conditions;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Numerics;

namespace Saucy.AirForce;

public static unsafe class AirForceAutomation
{
    private const int  MaxTargetWalk     = 128;
    private const long MinShotIntervalMs = 500;

    private static readonly IFramework                Framework;
    private static          bool                      Enabled;
    internal static         FireCachedTargetDelegate? FireCachedTarget;
    private static          long                      LastShotAt;

    private static DateTime? rewardWindowUntilUtc;
    private static bool      wasInDuty;

    public static bool ShouldTrackReward => rewardWindowUntilUtc != null && DateTime.UtcNow <= rewardWindowUntilUtc.Value;

    public static void ClearRewardTracking()
    {
        rewardWindowUntilUtc = null;
        wasInDuty            = false;
    }

    public static void ConsumeRewardTracking() => rewardWindowUntilUtc = null;

    public static void OnUpdate()
    {
        if (!C.IsModuleEnabled(ModuleNames.AirForceOne))
        {
            ClearRewardTracking();
            return;
        }

        var inDuty = Svc.Condition[ConditionFlag.BoundByDuty95]                               &&
                     GenericHelpers.TryGetAddonByName("RideShooting", out AtkUnitBase* addon) &&
                     addon->IsReady();

        if (inDuty)
        {
            wasInDuty            = true;
            rewardWindowUntilUtc = null;

            var now = Environment.TickCount64;
            if (now - LastShotAt < MinShotIntervalMs)
            {
                return;
            }

            var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.RideShooting);
            if (agent == null)
            {
                return;
            }

            var addonEventInterface = *(nint*)((byte*)agent + 0x30);
            if (addonEventInterface == 0)
            {
                return;
            }

            var context = addonEventInterface - 0x20;

            var target = FindBestTarget(context);
            if (target == 0)
            {
                return;
            }

            *(Vector3*)(context + 0xCA0) = *(Vector3*)(target + 0x00);
            *(int*)(context     + 0xCB0) = 1;
            *(ushort*)(context  + 0xCB4) = *(ushort*)(target + 0x30);

            FireCachedTarget?.Invoke(context);

            LastShotAt = now;

            return;
        }

        if (wasInDuty)
        {
            wasInDuty            = false;
            rewardWindowUntilUtc = DateTime.UtcNow.AddMinutes(2);
        }
    }

    private static nint FindBestTarget(nint context)
    {
        var sentinel = *(nint*)(context + 0xC58);
        if (sentinel == 0)
        {
            return 0;
        }

        nint best = 0;

        var node = *(nint*)(sentinel + 0x00);
        for (var i = 0; i < MaxTargetWalk && node != 0 && node != sentinel; i++, node = *(nint*)(node + 0x00))
        {
            var target = *(nint*)(node + 0x10);
            if (target == 0)
            {
                continue;
            }

            var kind       = *(int*)(target  + 0x4C);
            var subState   = *(int*)(target  + 0x50);
            var targetType = *(nint*)(target + 0x40);

            if (kind != 2 || subState != 0 || targetType == 0)
            {
                continue;
            }

            var score = *(short*)(targetType + 0x04);
            if (score < 0)
            {
                continue;
            }

            best = target;
        }

        return best;
    }

    internal delegate nint FireCachedTargetDelegate(nint context);
}
