namespace Saucy.TripleTriad;

internal static class TriadAutomator
{
    public static void RunModule()
    {
        TriadCardFarmSession.TickDisplaySync();

        TriadNpcSanityCheck.Tick();
        if (!TriadRunSession.ModuleEnabled)
        {
            return;
        }

        TriadCardFarmSession.TickVerificationCycle();

        if (TriadRun.preGameDecks.Count > 0 && C.UseSimmedDeck)
        {
            var selectedDeck = C.SelectedDeckIndex;
            if (selectedDeck >= 0 && !TriadRun.preGameDecks.ContainsKey(selectedDeck))
            {
                C.SelectedDeckIndex = -1;
            }
        }

        if (TriadDeckSelectAutomation.TickIfOpen())
        {
            return;
        }

        if (TriadUiState.IsMatchRegistrationVisible())
        {
            TriadRematchAutomation.ClearRematchPending();
            TriadRunSession.EnsureAutomationSessionForMatchPrep();

            if (!TriadRunSession.ShouldContinue())
            {
                if (TriadRematchAutomation.PendingRegistrationDismiss)
                {
                    if (TriadMatchRegistrationAutomation.TryDismiss())
                    {
                        TriadRematchAutomation.ClearPendingRegistrationDismiss();
                    }
                }

                return;
            }

            TriadMatchRegistrationAutomation.Tick();
            return;
        }

        if (TriadBoardAutomation.Tick())
        {
            return;
        }

        if (TriadUiState.IsResultVisible())
        {
            TriadRematchAutomation.Tick();
            return;
        }

        if (TriadUiState.IsPrepDeckSelectVisible())
        {
            TriadRematchAutomation.ClearRematchPending();
            TriadRunSession.EnsureAutomationSessionForMatchPrep();
        }

        if (!TriadRunSession.ShouldContinue())
        {
            if (!TriadRunSession.ModuleEnabled)
            {
                return;
            }

            if (TriadRematchAutomation.PendingRegistrationDismiss && TriadUiState.IsMatchRegistrationVisible())
            {
                if (TriadMatchRegistrationAutomation.TryDismiss())
                {
                    TriadRematchAutomation.ClearPendingRegistrationDismiss();
                }
            }
        }
    }
}
