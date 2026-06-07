using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using static ECommons.GenericHelpers;

namespace Saucy.TripleTriad;

internal static unsafe partial class TriadDeckSelectAutomation
{
    private static bool IsDeckSelectAddonPresent() =>
        TryGetAddonByName<AddonTripleTriadSelDeck>("TripleTriadSelDeck", out var _);

    private static bool IsDeckSelectVisible() =>
        TriadLocalClientStructs.TryGetSelDeck(out var _);

    private static bool IsSelectionComplete() =>
        !IsDeckSelectAddonPresent() ||
        (IsBoardHandsPopulated() && !IsDeckSelectVisible());

    private static bool IsSelectionSettled(AtkUnitBase* addon) =>
        IsSelectionComplete() || addon == null || !addon->IsVisible;

    private static void TickBoardVisibleDismissal(AtkUnitBase* addon)
    {
        boardDismissFrames++;

        if (!IsDeckSelectAddonPresent())
        {
            ResetSession();
            return;
        }

        if (!IsBoardHandsPopulated())
        {
            return;
        }

        if (!IsDeckSelectVisible())
        {
            ReleaseDeckSelectForMatch();
            return;
        }

        if (!confirmedThisScreen)
        {
            if (pendingProfileDeckId >= 0 && pendingDeckIndex >= 0)
            {
                TryApplyDeckSelection(addon, pendingProfileDeckId, pendingDeckIndex, pendingSelectMethod);
            }

            TryCloseDeckSelectGracefully(addon);
            confirmedThisScreen = true;
            framesSinceAttempt = DeckSelectRetryCooldownFrames;
            return;
        }

        TryCloseDeckSelectGracefully(addon);

        if (!IsDeckSelectVisible())
        {
            ReleaseDeckSelectForMatch();
            return;
        }

        if (boardDismissFrames < DeckSelectBoardVisibleMaxFrames)
        {
            return;
        }

        Svc.Log.Verbose("[TriadAutomator] Deck select overlay still visible after confirm; hiding overlay only");
        TryForceHideLastResort(addon);
        ReleaseDeckSelectForMatch();
        framesSinceAttempt = DeckSelectRetryCooldownFrames;
    }

    private static void ReleaseDeckSelectForMatch()
    {
        if (forceDismissedForMatch)
        {
            return;
        }

        forceDismissedForMatch = true;
        ClearPending();
        uiReaderPrep.OnDeckSelectLost();
    }

    private static void TryForceHideLastResort(AtkUnitBase* addon)
    {
        var agentHandle = Svc.GameGui.FindAgentInterface((nint)addon);
        if (agentHandle.Address != nint.Zero)
        {
            var agent = (AgentInterface*)agentHandle.Address;
            agent->HideAddon();
            agent->Hide();
            addon->Update(0);
        }

        try
        {
            addon->IsVisible = false;
            addon->Update(0);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadAutomator] Deck select last-resort hide failed");
        }
    }

    private static bool TrySelectGameRecommendedDeck(AtkUnitBase* addon)
    {
        TriadDeckLog.Print("[Saucy] Using game recommended deck...");
        foreach (var buttonId in DeckSelectRecommendedButtonIds)
        {
            if (!TryClickSelectButton(addon, buttonId))
            {
                continue;
            }

            addon->Update(0);
            TryClickConfirmButton(addon);
            addon->Update(0);
            confirmedThisScreen = true;
            if (IsSelectionComplete() || IsSelectionSettled(addon))
            {
                ClearPending();
            }

            return true;
        }

        return false;
    }

    private static bool TryBlindDeckSelect(AtkUnitBase* addon)
    {
        TriadDeckLog.Print("[Saucy] Selecting first deck...");
        foreach (var listIndex in new[]
        {
            0, 1, 2, 3, 4
        })
        {
            TryFireDeckCallback(addon, 1, listIndex);
            TryFireDeckCallback(addon, 0, listIndex);
            addon->Update(0);
            TryClickConfirmButton(addon);
            addon->Update(0);
            if (IsSelectionComplete())
            {
                confirmedThisScreen = true;
                return true;
            }
        }

        confirmedThisScreen = true;
        return false;
    }

    private static void TryFireDeckCallback(AtkUnitBase* addon, int eventId, int deckValue, bool close = false)
    {
        try
        {
            var values = stackalloc AtkValue[1];
            values[0] = new()
            {
                Type = AtkValueType.Int, Int = deckValue
            };
            addon->FireCallback((uint)eventId, values, close);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadAutomator] Deck callback {0} failed for deck {1}", eventId, deckValue);
        }
    }

    private static bool TryFireDeckSelectConfirmCallback(AtkUnitBase* addon, int deckValue)
    {
        if (deckValue < 0)
        {
            return false;
        }

        TryFireDeckCallback(addon, 1, deckValue, true);
        addon->Update(0);
        if (IsSelectionComplete() || !IsDeckSelectVisible())
        {
            return true;
        }

        TryFireDeckCallback(addon, 0, deckValue, true);
        addon->Update(0);
        return IsSelectionComplete() || !IsDeckSelectVisible();
    }

    private static void TryCloseDeckSelectGracefully(AtkUnitBase* addon)
    {
        var deckValue = pendingProfileDeckId >= 0 ? pendingProfileDeckId : pendingDeckIndex;
        if (!TryClickConfirmButton(addon) && deckValue >= 0)
        {
            TryFireDeckSelectConfirmCallback(addon, deckValue);
        }

        addon->Update(0);
    }
}
