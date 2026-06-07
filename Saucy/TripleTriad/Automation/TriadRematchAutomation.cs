using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
namespace Saucy.TripleTriad;

internal static unsafe class TriadRematchAutomation
{
    private const int ResultOutcomeFallbackFrames = 45;
    private const int RematchRetryCooldownFrames = 15;

    private static int framesSinceRematchAttempt;
    private static bool sessionEndDismissRequested;
    private static int framesSinceSessionEndDismiss;
    private static nint lastRecordedResultAddonPtr;
    private static int framesWaitingForResultOutcome;

    public static bool RematchPending { get; private set; }

    public static bool PendingRegistrationDismiss { get; private set; }

    public static void ClearPendingRegistrationDismiss() => PendingRegistrationDismiss = false;

    public static void CancelSessionEndDismissRequest() => sessionEndDismissRequested = false;

    public static void ResetSessionFlags()
    {
        RematchPending = false;
        sessionEndDismissRequested = false;
        PendingRegistrationDismiss = false;
        framesSinceSessionEndDismiss = 0;
        framesSinceRematchAttempt = 0;
        framesWaitingForResultOutcome = 0;
        lastRecordedResultAddonPtr = nint.Zero;
    }

    public static void RequestRematch()
    {
        RematchPending = true;
        sessionEndDismissRequested = false;
    }

    public static void ClearRematchPending()
    {
        RematchPending = false;
        framesSinceRematchAttempt = 0;
    }

    public static void RequestSessionEndDismiss()
    {
        ClearRematchPending();
        sessionEndDismissRequested = true;
        framesSinceSessionEndDismiss = 0;
        PendingRegistrationDismiss = true;
    }

    public static void ResetResultMatchRecording() => lastRecordedResultAddonPtr = nint.Zero;

    internal static bool IsResultMatchRecorded(nint resultAddonPtr) =>
        resultAddonPtr != nint.Zero && resultAddonPtr == lastRecordedResultAddonPtr;

    internal static bool IsResultReady(AtkUnitBase* addon) => addon != null && addon->IsReady;

    public static void RecordMatchResultIfNeeded(nint resultAddonPtr = default, bool requireActionButtons = false)
    {
        if (!TriadRunSession.ModuleEnabled)
        {
            return;
        }

        if (resultAddonPtr == nint.Zero)
        {
            if (!TriadLocalClientStructs.TryGetResult(out var liveResult))
            {
                return;
            }

            resultAddonPtr = (nint)liveResult;
        }

        if (TriadCardFarmSession.IsModeActive() && TriadCardFarmSession.IsDropVerificationPending())
        {
            return;
        }

        if (resultAddonPtr == lastRecordedResultAddonPtr)
        {
            return;
        }

        var resultAddon = (AtkUnitBase*)resultAddonPtr;
        if (!resultAddon->IsVisible)
        {
            return;
        }

        if (requireActionButtons && !IsResultReady(resultAddon))
        {
            return;
        }

        lastRecordedResultAddonPtr = resultAddonPtr;
        TriadCardFarmSession.EnsureArmed();

        if (TriadCardFarmSession.IsModeActive())
        {
            sessionEndDismissRequested = false;
            if (!TriadCardFarmSession.IsComplete())
            {
                RequestRematch();
            }
            else
            {
                TriadCardFarmSession.DeactivateSession();
                RequestSessionEndDismiss();
                Svc.Framework.Run(TryDismissResultIfSessionEnded);
            }

            return;
        }

        if (TriadRunSession.PlayUntilCardDrops && TriadRunSession.PlayUntilAnyCardDropped)
        {
            TriadCardFarmSession.DeactivateSession();
            RequestSessionEndDismiss();
            Svc.Framework.Run(TryDismissResultIfSessionEnded);
            return;
        }

        if (TriadRunSession.PlayXTimes && !TriadRunSession.PlayUntilAllCardsDropOnce && !TriadRunSession.PlayUntilCardDrops)
        {
            TriadRunSession.MatchesCompletedThisSession++;
            if (TriadRunSession.NumberOfTimes > 0)
            {
                TriadRunSession.NumberOfTimes--;
            }
        }

        if (TriadRunSession.ShouldContinue())
        {
            RequestRematch();
        }
        else
        {
            RequestSessionEndDismiss();
            Svc.Framework.Run(TryDismissResultIfSessionEnded);
        }
    }

    public static void TryDismissResultIfSessionEnded()
    {
        if (TriadRunSession.ShouldContinue() || !TriadUiState.IsResultVisible())
        {
            return;
        }

        if (!TriadLocalClientStructs.TryGetResult(out var resultAddon, false))
        {
            return;
        }

        TryDismissTriadResult(&resultAddon->AtkUnitBase);
    }

    public static bool Tick()
    {
        if (!TriadUiState.IsResultVisible())
        {
            ResetResultMatchRecording();
            framesWaitingForResultOutcome = 0;
            sessionEndDismissRequested = false;
            return false;
        }

        if (!TriadLocalClientStructs.TryGetResult(out var resultAddon, false))
        {
            return false;
        }

        var addon = &resultAddon->AtkUnitBase;

        TriadCardFarmSession.EnsureArmed();

        if (IsResultMatchRecorded((nint)addon))
        {
            framesWaitingForResultOutcome = 0;
        }
        else if (++framesWaitingForResultOutcome >= ResultOutcomeFallbackFrames)
        {
            uiReaderMatchResults.ForceNotifyFromFallback((nint)addon);
            framesWaitingForResultOutcome = 0;
        }

        if (TriadCardFarmSession.IsModeActive() && TriadCardFarmSession.HasPendingDrops())
        {
            if (TriadCardFarmSession.IsDropVerificationPending())
            {
                return true;
            }

            sessionEndDismissRequested = false;
            if (!RematchPending && IsResultMatchRecorded((nint)addon))
            {
                RequestRematch();
            }
        }

        if (TriadRunSession.ModuleEnabled &&
            !IsResultMatchRecorded((nint)addon) &&
            IsResultReady(addon))
        {
            RecordMatchResultIfNeeded((nint)addon, true);
        }

        if (sessionEndDismissRequested)
        {
            if (TriadCardFarmSession.IsModeActive() && TriadCardFarmSession.HasPendingDrops())
            {
                if (TriadCardFarmSession.IsDropVerificationPending())
                {
                    return true;
                }

                sessionEndDismissRequested = false;
                if (!RematchPending)
                {
                    RequestRematch();
                }
            }
            else if (framesSinceSessionEndDismiss <= 0)
            {
                if (TryDismissTriadResult(addon))
                {
                    sessionEndDismissRequested = false;
                    return true;
                }

                framesSinceSessionEndDismiss = RematchRetryCooldownFrames;
            }
            else
            {
                framesSinceSessionEndDismiss--;
            }

            return true;
        }

        if (!TriadRunSession.ModuleEnabled || !RematchPending)
        {
            return false;
        }

        if (TriadCardFarmSession.IsDropVerificationPending())
        {
            return true;
        }

        if (framesSinceRematchAttempt > 0)
        {
            framesSinceRematchAttempt--;
            return true;
        }

        try
        {
            if (!IsResultReady(addon))
            {
                return true;
            }

            if (DidEnterRematchFlow())
            {
                ClearRematchPending();
                return true;
            }

            TryFireResultRematch(addon);

            if (DidEnterRematchFlow())
            {
                ClearRematchPending();
            }
            else if (!addon->IsVisible && TriadCardFarmSession.IsModeActive() && TriadCardFarmSession.HasPendingDrops())
            {
                RequestRematch();
            }

            framesSinceRematchAttempt = RematchRetryCooldownFrames;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadRematchAutomation] Tick failed");
        }

        return true;
    }

    private static bool TryDismissTriadResult(AtkUnitBase* addon)
    {
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
            Svc.Log.Verbose(ex, "[TriadRematchAutomation] Result FireCallbackInt(1) failed");
        }

        try
        {
            addon->Close(true);
            addon->Update(0);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadRematchAutomation] Result Close(true) failed");
        }

        return !addon->IsVisible;
    }

    private static bool TryFireResultRematch(AtkUnitBase* addon)
    {
        try
        {
            addon->FireCallbackInt(0);
            addon->Update(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadRematchAutomation] Result FireCallbackInt(0) failed");
            return false;
        }
    }

    private static bool DidEnterRematchFlow() =>
        TriadUiState.IsPrepDeckSelectVisible() || TriadUiState.IsMatchRegistrationVisible();
}
