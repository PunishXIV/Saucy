using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons;
using ECommons.Automation;
using ECommons.CSExtensions;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using ECommons.Throttlers;
using ECommons.WindowsFormsReflector;
using System;
using System.Linq;
namespace Saucy.AirForce;

public static unsafe class AirForceAutomation
{
    private static DateTime? rewardWindowUntilUtc;
    private static bool wasInDuty;

    public static bool ShouldTrackReward
        => rewardWindowUntilUtc != null && DateTime.UtcNow <= rewardWindowUntilUtc.Value;

    public static void ClearRewardTracking()
    {
        rewardWindowUntilUtc = null;
        wasInDuty = false;
    }

    public static void ConsumeRewardTracking() => rewardWindowUntilUtc = null;

    public static void OnUpdate()
    {
        if (!C.IsModuleEnabled(ModuleNames.AirForceOne))
        {
            ClearRewardTracking();
            return;
        }

        var inDuty = Svc.Condition[ConditionFlag.BoundByDuty95] &&
                     GenericHelpers.TryGetAddonByName("RideShooting", out AddonRideShooting* rideAddon) &&
                     rideAddon->AtkUnitBase.IsReady();

        if (inDuty)
        {
            wasInDuty = true;
            rewardWindowUntilUtc = null;

            foreach (var x in Svc.Objects.OfType<IEventObj>().Where(x => x.BaseId.EqualsAny<uint>(
                2009678,
                2009676,
                2009677,
                2009679,
                2015180,
                2015179,
                2015178,
                2015183
            )).Where(x => x.AnimationId == 1).OrderBy(Player.DistanceTo))
            {
                if (x.BaseId.EqualsAny<uint>(
                    2015183,
                    2009679
                ))
                {
                    continue;
                }

                if (Svc.GameGui.WorldToScreen(x.Position, out var screen))
                {
                    if (EzThrottler.Throttle("Shoot", 250) && RideShootingAim.TrySetScreenAim(screen))
                    {
                        Svc.Framework.RunOnTick(() =>
                            {
                                _ = WindowsKeypress.SendKeypress(Keys.Space);
                            },
                            delayTicks: 1);
                        break;
                    }
                }
            }

            return;
        }

        if (wasInDuty)
        {
            wasInDuty = false;
            rewardWindowUntilUtc = DateTime.UtcNow.AddMinutes(2);
        }
    }

    public static void DrawDebug()
    {
        ImGuiEx.Text($"Enabled: {C.IsModuleEnabled(ModuleNames.AirForceOne)}");
        ImGuiEx.Text($"In duty: {Svc.Condition[ConditionFlag.BoundByDuty95]}");
        ImGuiEx.Text($"Tracking reward: {ShouldTrackReward}");

        var addonReady = GenericHelpers.TryGetAddonByName("RideShooting", out AddonRideShooting* rideAddon) &&
                         rideAddon->AtkUnitBase.IsReady();
        ImGuiEx.Text($"RideShooting addon ready: {addonReady}");

        var parityOk = RideShootingAim.VerifyLayoutParity(out var parityDetail);
        ImGuiEx.Text($"Legacy vs typed layout: {(parityOk ? "OK" : "MISMATCH")} — {parityDetail}");

        if (RideShootingAim.TryReadAim(out var aim))
        {
            ImGuiEx.Text($"Current aim: ({aim.X:F1}, {aim.Y:F1})");
        }

        var targets = Svc.Objects.OfType<IEventObj>().Where(x => x.BaseId.EqualsAny<uint>(
            2009678, 2009676, 2009677, 2009679, 2015180, 2015179, 2015178, 2015183
        )).Where(x => x.AnimationId == 1).OrderBy(Player.DistanceTo).Take(3).ToArray();
        ImGuiEx.Text($"Shootable targets (anim=1): {targets.Length}");
        foreach (var t in targets)
        {
            ImGuiEx.Text($"  {t.Name} ({t.BaseId}) dist={Player.DistanceTo(t):F1}");
        }
    }
}
