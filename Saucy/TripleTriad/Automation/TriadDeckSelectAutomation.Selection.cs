using ECommons.Automation.UIInput;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
namespace Saucy.TripleTriad;

internal static unsafe partial class TriadDeckSelectAutomation
{
    private static bool TryClickSelectButton(AtkUnitBase* addon, uint buttonId)
    {
        var button = addon->GetComponentButtonById(buttonId);
        if (button == null || !button->IsEnabled || button->AtkResNode == null || !button->AtkResNode->IsVisible())
        {
            return false;
        }

        try
        {
            button->ClickAddonButton(addon);
            addon->Update(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadAutomator] Deck select button {0} click failed", buttonId);
            return false;
        }
    }

    private static bool TrySelectPreferredProfileDeck(AtkUnitBase* addon)
    {
        if (!C.UseSimmedDeck || !TriadRun.TryResolveAutoPickProfileDeckId(out var profileDeckId))
        {
            return false;
        }

        if (!TriadRun.TryResolveDeckListIndex(profileDeckId, out var listIndex))
        {
            return false;
        }

        PrintAttemptMessage(profileDeckId, listIndex);
        pendingProfileDeckId = profileDeckId;
        pendingDeckIndex = listIndex;
        pendingSelectMethod = 0;
        awaitingConfirm = true;
        TryApplyDeckSelection(addon, profileDeckId, listIndex, 0);
        return true;
    }

    private static bool TrySelectVisibleSaucyDeck(AtkUnitBase* addon)
    {
        var expectedName = TriadRun.GetExpectedSaucyDeckName();
        var npcName = TriadRun.preGameNpc?.Name ?? string.Empty;
        for (var idx = 0; idx < uiReaderPrep.cachedState.decks.Count; idx++)
        {
            var deck = uiReaderPrep.cachedState.decks[idx];
            if (string.IsNullOrWhiteSpace(deck.name))
            {
                continue;
            }

            var isSaucyDeck = deck.name.Contains("(Sa", StringComparison.OrdinalIgnoreCase);
            if (!isSaucyDeck)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(expectedName) &&
                !TriadDeckNameHelper.RowMatchesNpc(deck.name, expectedName, npcName))
            {
                continue;
            }

            TriadDeckLog.Print($"[Saucy] Selecting \"{deck.name}\"...");
            var profileDeckId = TriadRun.HasOptimizedDeckApplied
                ? TriadRun.OptimizedDeckSlotId
                : deck.id;
            pendingProfileDeckId = profileDeckId;
            pendingDeckIndex = idx;
            pendingSelectMethod = 0;
            awaitingConfirm = true;
            TryApplyDeckSelection(addon, profileDeckId, idx, 0);
            return true;
        }

        return false;
    }

    private static void TryApplyDeckSelection(AtkUnitBase* addon, int profileDeckId, int listIndex, int method)
    {
        var selectionClosed = false;
        switch (method)
        {
            case 0:
                if (C.UseSimmedDeck &&
                    TriadRun.HasOptimizedDeckApplied &&
                    profileDeckId == TriadRun.OptimizedDeckSlotId)
                {
                    selectionClosed = TryFireDeckSelectConfirmCallback(addon, profileDeckId);
                    if (!selectionClosed)
                    {
                        TryFireDeckCallback(addon, 0, profileDeckId);
                    }
                }
                else
                {
                    TryClickDeckListRow(addon, listIndex);
                }

                break;
            case 1:
                TryFireDeckCallback(addon, 1, profileDeckId);
                break;
            case 2:
                TryFireDeckCallback(addon, 0, profileDeckId);
                break;
            case 3:
                TryFireDeckCallback(addon, 2, profileDeckId);
                break;
            case 4:
                selectionClosed = TryClickConfirmButton(addon);
                break;
        }

        addon->Update(0);

        if (!selectionClosed && method < 4)
        {
            TryClickConfirmButton(addon);
            addon->Update(0);
        }

        if (IsSelectionComplete())
        {
            confirmedThisScreen = true;
            if (!TriadUiState.IsBoardVisible() || IsBoardHandsPopulated())
            {
                ClearPending();
            }
        }
    }

    private static void ClearPending()
    {
        pendingDeckIndex = -1;
        pendingProfileDeckId = -1;
        pendingSelectMethod = 0;
        awaitingConfirm = false;
    }

    private static bool TryClickConfirmButton(AtkUnitBase* addon)
    {
        foreach (var buttonId in DeckSelectConfirmButtonIds)
        {
            if (TryClickSelectButton(addon, buttonId))
            {
                return true;
            }
        }

        var deckValue = pendingProfileDeckId >= 0 ? pendingProfileDeckId : pendingDeckIndex;
        return deckValue >= 0 && TryFireDeckSelectConfirmCallback(addon, deckValue);
    }

    private static void PrintAttemptMessage(int deck, int listIndex)
    {
        string message;
        if (attemptCount > 0 || AttemptedDeckIndices.Count > 0)
        {
            message = $"[Saucy] Retrying with deck {deck + 1}...";
        }
        else if (C.UseSimmedDeck && TriadRun.HasOptimizedDeckApplied)
        {
            var deckName = TriadRun.GetProfileDeckName(deck);
            if (string.IsNullOrWhiteSpace(deckName))
            {
                deckName = TriadRun.GetExpectedSaucyDeckName();
            }

            message = !string.IsNullOrWhiteSpace(deckName)
                ? $"[Saucy] Selecting \"{deckName}\" (slot {deck + 1})..."
                : $"[Saucy] Selecting optimized deck {deck + 1}...";
        }
        else
        {
            var deckName = TriadRun.GetProfileDeckName(deck);
            if (string.IsNullOrWhiteSpace(deckName) &&
                listIndex >= 0 && listIndex < uiReaderPrep.cachedState.decks.Count)
            {
                deckName = uiReaderPrep.cachedState.decks[listIndex].name;
            }

            message = !string.IsNullOrWhiteSpace(deckName)
                ? $"[Saucy] Selecting \"{deckName}\"..."
                : $"[Saucy] Selecting deck {deck + 1}...";
        }

        if (C.UseSimmedDeck)
        {
            TriadDeckLog.Print(message);
        }
        else
        {
            Svc.Log.Verbose(message);
        }
    }

    private static bool TryClickDeckListRow(AtkUnitBase* addon, int listIndex)
    {
        if (listIndex < 0 || listIndex >= uiReaderPrep.cachedState.decks.Count)
        {
            return false;
        }

        var rowAddr = uiReaderPrep.cachedState.decks[listIndex].rootNodeAddr;
        if (rowAddr == 0)
        {
            return false;
        }

        var rowNode = (AtkResNode*)rowAddr;
        if (rowNode == null)
        {
            return false;
        }

        if (TryClickComponentButton(rowNode, addon))
        {
            return true;
        }

        var children = GUINodeUtils.GetImmediateChildNodes(rowNode);
        if (children == null)
        {
            return false;
        }

        foreach (var child in children)
        {
            if (TryClickComponentButton(child, addon))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryClickComponentButton(AtkResNode* node, AtkUnitBase* addon)
    {
        if (node == null)
        {
            return false;
        }

        var button = node->GetAsAtkComponentButton();
        if (button == null || !button->IsEnabled || button->AtkResNode == null || !button->AtkResNode->IsVisible())
        {
            return false;
        }

        try
        {
            button->ClickAddonButton(addon);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadAutomator] Deck row button click failed");
            return false;
        }
    }
}
