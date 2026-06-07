using ECommons.Automation.UIInput;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using System;
using static ECommons.GenericHelpers;

namespace Saucy.TripleTriad;

internal static unsafe class TriadMatchRegistrationAutomation
{
    private const int MatchAcceptRetryCooldownFrames = 15;
    private const string DismissThrottleKey = "TriadRequestQuit";

    private static int framesSinceMatchAcceptAttempt;

    public static void ResetSession() => framesSinceMatchAcceptAttempt = 0;

    public static void Tick()
    {
        TriadRewardDropTracker.ResetSnapshot();
        TryAccept();
    }

    public static bool TryDismiss()
    {
        if (!TriadLocalClientStructs.TryGetRequest(out var request))
        {
            return true;
        }

        var addon = &request->AtkUnitBase;

        if (!EzThrottler.Throttle(DismissThrottleKey))
        {
            return false;
        }

        try
        {
            addon->FireCallbackInt(1);
            addon->Update(0);
            if (!addon->IsVisible)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadMatchRegistrationAutomation] Request FireCallbackInt(1) failed");
        }

        try
        {
            new AddonMaster.TripleTriadRequest(addon).Quit();
            addon->Update(0);
            if (!addon->IsVisible)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadMatchRegistrationAutomation] AddonMaster Quit failed");
        }

        try
        {
            addon->Close(true);
            addon->Update(0);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadMatchRegistrationAutomation] Registration Close(true) failed");
        }

        return !addon->IsVisible;
    }

    private static void TryAccept()
    {
        try
        {
            if (!TriadLocalClientStructs.TryGetRequest(out var request))
            {
                framesSinceMatchAcceptAttempt = 0;
                return;
            }

            var addon = &request->AtkUnitBase;

            if (framesSinceMatchAcceptAttempt > 0)
            {
                framesSinceMatchAcceptAttempt--;
                return;
            }

            if (!IsAddonReady(addon))
            {
                return;
            }

            var challengeButton = addon->GetComponentButtonById(41);
            if (challengeButton != null && challengeButton->AtkResNode != null &&
                challengeButton->AtkResNode->IsVisible())
            {
                try
                {
                    challengeButton->ClickAddonButton(addon);
                    addon->Update(0);
                }
                catch (Exception clickEx)
                {
                    Svc.Log.Verbose(clickEx, "[TriadMatchRegistrationAutomation] Challenge button click failed");
                }
            }

            framesSinceMatchAcceptAttempt = MatchAcceptRetryCooldownFrames;

            if (TriadRunSession.PlayUntilAllCardsDropOnce)
            {
                TriadCardFarmSession.EnsureArmed();
                TriadCardFarmSession.SyncDisplay(TriadRunTarget.Resolve());
            }

            TriadRewardDropTracker.SnapshotAtMatchStart();
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadMatchRegistrationAutomation] TryAccept failed");
        }
    }
}
