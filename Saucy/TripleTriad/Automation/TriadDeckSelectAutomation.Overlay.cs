using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
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

    private static void TickGameRecommendedDeck(AtkUnitBase* addon)
    {
        if (recommendedAttempts >= MaxDeckSelectAttemptsPerScreen)
        {
            if (recommendedAttempts == MaxDeckSelectAttemptsPerScreen)
            {
                Svc.Chat.PrintError(
                    "[Saucy] Could not use game recommended deck. Pick a deck manually or try another option.");
                recommendedAttempts++;
            }

            return;
        }

        if (!recommendedClicked)
        {
            if (!TryClickGameRecommendedButton(addon))
            {
                recommendedAttempts++;
                framesSinceAttempt = DeckSelectRetryCooldownFrames;
                return;
            }

            recommendedClicked = true;
            addon->Update(0);
            if (IsSelectionComplete() || IsSelectionSettled(addon))
            {
                confirmedThisScreen = true;
                ClearPending();
                return;
            }

            framesSinceAttempt = DeckSelectRecommendedSettleFrames;
            return;
        }

        if (TryConfirmGameRecommendedDeck(addon))
        {
            confirmedThisScreen = true;
            ClearPending();
            return;
        }

        recommendedAttempts++;
        framesSinceAttempt = DeckSelectRetryCooldownFrames;
    }

    private static bool TryClickGameRecommendedButton(AtkUnitBase* addon)
    {
        if (TryClickBottomDeckSelectActionButton(addon, preferLeft: true))
        {
            TriadDeckLog.Print("[Saucy] Using game recommended deck...");
            return true;
        }

        var list = TryGetDeckSelectList(addon);
        foreach (var buttonId in DeckSelectRecommendedButtonIds)
        {
            var button = addon->GetComponentButtonById(buttonId);
            if (IsButtonInsideList(button, list) || !TryClickSelectButton(addon, buttonId))
            {
                continue;
            }

            TriadDeckLog.Print("[Saucy] Using game recommended deck...");
            return true;
        }

        return false;
    }

    private static bool TryConfirmGameRecommendedDeck(AtkUnitBase* addon)
    {
        if (TryDispatchSelectedDeckListItem(addon))
        {
            addon->Update(0);
            if (IsSelectionComplete() || IsSelectionSettled(addon))
            {
                return true;
            }
        }

        TryClickConfirmButton(addon);
        addon->Update(0);
        if (IsSelectionComplete() || IsSelectionSettled(addon))
        {
            return true;
        }

        var selected = TryGetSelectedDeckListIndex(addon);
        if (selected >= 0 && TryFireDeckSelectConfirmCallback(addon, selected))
        {
            return true;
        }

        return IsSelectionComplete() || IsSelectionSettled(addon);
    }

    private static bool TryDispatchSelectedDeckListItem(AtkUnitBase* addon)
    {
        var list = TryGetDeckSelectList(addon);
        if (list is null)
        {
            return false;
        }

        var selected = list->SelectedItemIndex;
        if (selected < 0)
        {
            return false;
        }

        try
        {
            list->SelectItem(selected, true);
            list->DispatchItemEvent(selected, AtkEventType.ListItemClick);
            pendingDeckIndex = selected;
            pendingProfileDeckId = selected;
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadAutomator] Recommended deck list confirm failed for index {0}", selected);
            return false;
        }
    }

    private static int TryGetSelectedDeckListIndex(AtkUnitBase* addon)
    {
        var list = TryGetDeckSelectList(addon);
        return list is null ? -1 : list->SelectedItemIndex;
    }

    private static AtkComponentList* TryGetDeckSelectList(AtkUnitBase* addon)
    {
        AtkComponentList* best = null;
        var bestLen = 0;
        for (uint id = 1; id <= MaxDeckSelectNodeScan; id++)
        {
            var list = addon->GetComponentListById(id);
            if (list is null || list->ListLength <= 0)
            {
                continue;
            }

            if (list->ListLength <= bestLen)
            {
                continue;
            }

            best = list;
            bestLen = list->ListLength;
        }

        return best;
    }

    private static bool TryClickBottomDeckSelectActionButton(AtkUnitBase* addon, bool preferLeft)
    {
        var list = TryGetDeckSelectList(addon);
        AtkComponentButton* best = null;
        var bestX = preferLeft ? float.MaxValue : float.MinValue;
        var bestY = float.MinValue;
        for (uint id = 1; id <= MaxDeckSelectNodeScan; id++)
        {
            var button = addon->GetComponentButtonById(id);
            if (button is null ||
                !button->IsEnabled ||
                button->AtkResNode is null ||
                !button->AtkResNode->IsVisible() ||
                IsButtonInsideList(button, list))
            {
                continue;
            }

            var pos = GUINodeUtils.GetNodePosition(button->AtkResNode);
            if (pos.Y < bestY - 8f)
            {
                continue;
            }

            if (pos.Y > bestY + 8f)
            {
                best = button;
                bestX = pos.X;
                bestY = pos.Y;
                continue;
            }

            if (preferLeft ? pos.X < bestX : pos.X > bestX)
            {
                best = button;
                bestX = pos.X;
            }
        }

        return AddonButton.TryClick(addon, best);
    }

    private static bool IsButtonInsideList(AtkComponentButton* button, AtkComponentList* list)
    {
        if (button is null || list is null || list->OwnerNode is null || button->AtkResNode is null)
        {
            return false;
        }

        var listNode = (AtkResNode*)list->OwnerNode;
        for (var node = button->AtkResNode; node is not null; node = node->ParentNode)
        {
            if (node == listNode)
            {
                return true;
            }
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
