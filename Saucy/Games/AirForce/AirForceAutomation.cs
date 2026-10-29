using Dalamud.Game.ClientState.Conditions;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
namespace Saucy.AirForce;

public class AirForceOne : Framework.Module
{
    public override string InternalName => ModuleNames.AirForceOne;
    private const string FireCachedTargetSignature = "48 8B C4 53 48 81 EC ?? ?? ?? ?? 0F 29 70 ?? 0F 57 C9 0F 29 78 ?? 48 8B D9 F3 0F 10 B9 ?? ?? ?? ?? BA FF FF 00 00";
    public override string Name => "Air Force One";

    public override void Enable()
    {
        Svc.Framework.Update += OnFrameworkUpdate;
        if (Svc.SigScanner.TryScanText(FireCachedTargetSignature, out var fireCachedTargetAddress))
        {
            AirForceAutomation.FireCachedTarget = Marshal.GetDelegateForFunctionPointer<AirForceAutomation.FireCachedTargetDelegate>(fireCachedTargetAddress);
        }
    }

    public override void Disable()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        AirForceAutomation.ClearRewardTracking();
    }

    private static void OnFrameworkUpdate(IFramework _) => AirForceAutomation.OnUpdate();
}

public static unsafe class AirForceAutomation
{
    private const int MaxTargetWalk = 128;
    private const long MinShotIntervalMs = 500;

    internal static FireCachedTargetDelegate? FireCachedTarget;
    private static long LastShotAt;

    private static DateTime? rewardWindowUntilUtc;
    private static bool wasInDuty;

    public static bool ShouldTrackReward => rewardWindowUntilUtc != null && DateTime.UtcNow <= rewardWindowUntilUtc.Value;

    public static void ClearRewardTracking()
    {
        rewardWindowUntilUtc = null;
        wasInDuty = false;
    }

    public static void ConsumeRewardTracking() => rewardWindowUntilUtc = null;

    private static bool resultRecorded;
    private static int lastResultMgp;

    // Air Force One shows its own RideShootingResult window; the MGP is in the "Final Reward" text node
    private static void TrackResultScreen()
    {
        if (!GenericHelpers.TryGetAddonByName("RideShootingResult", out AtkUnitBase* result) || !result->IsReady())
        {
            resultRecorded = false;
            lastResultMgp = 0;
            return;
        }

        if (resultRecorded)
        {
            return;
        }

        AtkResNode* n1 = result->UldManager.NodeListCount > 8 ? result->UldManager.NodeList[8] : null;
        AtkComponentBase* c1 = n1 != null ? n1->GetComponent() : null;
        AtkResNode* n2 = c1 != null && c1->UldManager.NodeListCount > 3 ? c1->UldManager.NodeList[3] : null;
        AtkComponentBase* c2 = n2 != null ? n2->GetComponent() : null;
        AtkResNode* text = c2 != null && c2->UldManager.NodeListCount > 1 ? c2->UldManager.NodeList[1] : null;

        if (!GoldSaucerRewardMgpParser.TryParseMgpDigits(GUINodeUtils.GetNodeText(text), out var mgp))
        {
            return;
        }

        // only count once the number stops changing between frames (in case it counts up)
        if (mgp != lastResultMgp)
        {
            lastResultMgp = mgp;
            return;
        }

        resultRecorded = true;
        StatsSessionClock.MarkAirForceActive();
        C.UpdateStats(stats =>
        {
            stats.AirForceMGP += StatsBonusHelper.ApplyMgpBonus(mgp);
            stats.AirForceGamesPlayed++;
        });
        C.Save();
        result->FireCallbackInt(-1);
    }

    public static void OnUpdate()
    {
        if (!C.IsModuleEnabled(ModuleNames.AirForceOne))
        {
            ClearRewardTracking();
            return;
        }
        TrackResultScreen();

        var inDuty = Svc.Condition[ConditionFlag.BoundByDuty95] &&
                     GenericHelpers.TryGetAddonByName("RideShooting", out AtkUnitBase* addon) &&
                     addon->IsReady();

        if (inDuty)
        {
            wasInDuty = true;
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
            *(int*)(context + 0xCB0) = 1;
            *(ushort*)(context + 0xCB4) = *(ushort*)(target + 0x30);

            FireCachedTarget?.Invoke(context);

            LastShotAt = now;

            return;
        }

        if (wasInDuty)
        {
            wasInDuty = false;
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

            var kind = *(int*)(target + 0x4C);
            var subState = *(int*)(target + 0x50);
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
