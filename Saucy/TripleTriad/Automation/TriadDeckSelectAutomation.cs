using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using static ECommons.GenericHelpers;

namespace Saucy.TripleTriad;

internal static unsafe partial class TriadDeckSelectAutomation
{
    private const int MaxDeckSelectAttemptsPerScreen = 12;
    private const int DeckSelectRetryCooldownFrames = 30;
    private const int DeckSelectStuckResetFrames = 300;
    private const int DeckSelectBoardVisibleMaxFrames = 60;
    private const int DeckSelectBoardDismissDelayFrames = 15;
    private const int DeckSelectPostOptimizerCooldownFrames = TriadSession.DeckSelectPostProfileWriteFrames;
    private const int MaxDeckSelectMethods = 5;

    private static readonly uint[] DeckSelectConfirmButtonIds =
    [
        5, 1
    ];

    private static readonly uint[] DeckSelectRecommendedButtonIds =
    [
        0, 2, 4
    ];

    private static readonly HashSet<int> AttemptedDeckIndices = [];

    private static bool confirmedThisScreen;
    private static int attemptCount;
    private static int framesSinceAttempt;
    private static int pendingDeckIndex = -1;
    private static int pendingProfileDeckId = -1;
    private static int pendingSelectMethod;
    private static bool awaitingConfirm;
    private static bool forceDismissedForMatch;
    private static int boardDismissFrames;
    private static int boardVisibleFrames;

    public static int FramesOpen { get; private set; }

    public static bool ScreenActive { get; private set; }

    public static bool TickIfOpen()
    {
        if (!TriadLocalClientStructs.TryGetSelDeck(out var _, false))
        {
            if (ScreenActive)
            {
                ResetSession();
            }

            return false;
        }

        Tick();
        return BlocksBoardAutomation();
    }

    public static bool BlocksBoardAutomation()
    {
        if (!TriadLocalClientStructs.TryGetSelDeck(out var _, false))
        {
            return false;
        }
        if (TriadUiState.IsResultVisible() || TriadUiState.IsMatchRegistrationVisible())
        {
            return false;
        }

        var cardFarmActive = TriadCardFarmSession.IsModeActive() && TriadCardFarmSession.HasPendingDrops();
        if (!TriadRunSession.ShouldContinue() && !cardFarmActive)
        {
            return false;
        }

        if (TriadUiState.IsBoardVisible())
        {
            return false;
        }

        return true;
    }

    public static void Tick()
    {
        try
        {
            if (!TriadLocalClientStructs.TryGetSelDeck(out var selDeck, false))
            {
                ClearPending();
                ResetSession();
                return;
            }

            var addon = &selDeck->AtkUnitBase;
            if (TriadUiState.IsResultVisible())
            {
                TryCloseDeckSelectGracefully(addon);
                if (IsDeckSelectAddonPresent())
                {
                    TryForceHideLastResort(addon);
                }

                ReleaseDeckSelectForMatch();
                ResetSession();
                return;
            }

            if (TriadUiState.IsMatchRegistrationVisible() && !addon->IsVisible)
            {
                ReleaseDeckSelectForMatch();
                ResetSession();
                return;
            }

            if (TriadUiState.IsBoardVisible())
            {
                if (!forceDismissedForMatch)
                {
                    ReleaseDeckSelectForMatch();
                }

                TryCloseDeckSelectGracefully(addon);
                if (IsDeckSelectAddonPresent() && IsDeckSelectVisible())
                {
                    boardVisibleFrames++;
                    if (boardVisibleFrames < DeckSelectBoardDismissDelayFrames)
                    {
                        return;
                    }

                    TickBoardVisibleDismissal(addon);
                    return;
                }

                ResetSession();
                return;
            }

            boardVisibleFrames = 0;

            var cardFarmActive = TriadCardFarmSession.IsModeActive() && TriadCardFarmSession.HasPendingDrops();
            if (!TriadRunSession.ShouldContinue() && !cardFarmActive)
            {
                TryCloseDeckSelectGracefully(addon);
                if (IsDeckSelectAddonPresent())
                {
                    TryForceHideLastResort(addon);
                }
                ReleaseDeckSelectForMatch();
                ResetSession();
                return;
            }

            if (confirmedThisScreen)
            {
                if (IsSelectionSettled(addon))
                {
                    return;
                }

                if (FramesOpen < DeckSelectStuckResetFrames)
                {
                    return;
                }

                confirmedThisScreen = false;
            }

            FramesOpen++;

            if (!ScreenActive)
            {
                ResetSession();
                ScreenActive = true;
            }

            TriadRun.TickDeckSelectPostWriteCooldown();
            TriadRun.EnsureRunTargetNpcSynced(deckSelectScreen: true);
            if (!TriadUiState.IsBoardVisible())
            {
                TriadRun.EnsureExistingSaucyDeckForPrep();
            }

            if (framesSinceAttempt > 0)
            {
                framesSinceAttempt--;
                return;
            }

            if (awaitingConfirm)
            {
                if (!IsSelectionComplete())
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

                    return;
                }

                if (pendingSelectMethod + 1 < MaxDeckSelectMethods)
                {
                    pendingSelectMethod++;
                    TryApplyDeckSelection(addon, pendingProfileDeckId, pendingDeckIndex, pendingSelectMethod);
                    framesSinceAttempt = DeckSelectRetryCooldownFrames;
                    if (IsSelectionComplete())
                    {
                        confirmedThisScreen = true;
                        if (!TriadUiState.IsBoardVisible() || IsBoardHandsPopulated())
                        {
                            ClearPending();
                        }
                    }

                    return;
                }

                if (IsSelectionSettled(addon))
                {
                    confirmedThisScreen = true;
                    ClearPending();
                    return;
                }

                AttemptedDeckIndices.Add(pendingProfileDeckId);
                attemptCount++;
                ClearPending();
                return;
            }

            if (attemptCount >= MaxDeckSelectAttemptsPerScreen)
            {
                if (attemptCount == MaxDeckSelectAttemptsPerScreen)
                {
                    Svc.Chat.PrintError("[Saucy] Could not select a deck automatically. Pick one manually.");
                    attemptCount++;
                }

                return;
            }

            if (!IsAddonReady(addon))
            {
                return;
            }

            uiReaderPrep.RefreshDeckSelectList((nint)addon);

            if (!C.UseSimmedDeck && C.SelectedDeckIndex == Configuration.GameRecommendedDeckIndex)
            {
                if (attemptCount < MaxDeckSelectAttemptsPerScreen)
                {
                    if (TrySelectGameRecommendedDeck(addon))
                    {
                        attemptCount++;
                        framesSinceAttempt = DeckSelectRetryCooldownFrames;
                    }
                    else
                    {
                        attemptCount++;
                        framesSinceAttempt = DeckSelectRetryCooldownFrames;
                        if (attemptCount == MaxDeckSelectAttemptsPerScreen)
                        {
                            Svc.Chat.PrintError(
                                "[Saucy] Could not use game recommended deck. Pick a deck manually or try another option.");
                        }
                    }
                }

                return;
            }

            if (C.UseSimmedDeck && TriadRun.IsDeckSelectPrepBlocking(C.UseSimmedDeck))
            {
                return;
            }

            if (C.UseSimmedDeck && attemptCount == 0 && AttemptedDeckIndices.Count == 0)
            {
                if (TrySelectPreferredProfileDeck(addon))
                {
                    attemptCount++;
                    framesSinceAttempt = DeckSelectRetryCooldownFrames;
                    return;
                }

                if (TriadRun.ShouldTryVisibleSaucyDeckRowSelect() && TrySelectVisibleSaucyDeck(addon))
                {
                    attemptCount++;
                    framesSinceAttempt = DeckSelectRetryCooldownFrames;
                    return;
                }

                if (uiReaderPrep.cachedState.decks.Count == 0 && TryBlindDeckSelect(addon))
                {
                    attemptCount++;
                    framesSinceAttempt = DeckSelectRetryCooldownFrames;
                    return;
                }
            }

            if (!TriadRun.TryGetDeckSelectCandidate(
                C.UseSimmedDeck,
                C.SelectedDeckIndex,
                AttemptedDeckIndices,
                out var deck))
            {
                return;
            }

            if (deck < 0)
            {
                return;
            }

            if (AttemptedDeckIndices.Contains(deck))
            {
                return;
            }

            if (!TriadRun.TryResolveDeckListIndex(deck, out var resolvedListIndex))
            {
                Svc.Chat.PrintError($"[Saucy] Could not find deck {deck + 1} in the selection list.");
                AttemptedDeckIndices.Add(deck);
                attemptCount++;
                return;
            }

            PrintAttemptMessage(deck, resolvedListIndex);

            pendingProfileDeckId = deck;
            pendingDeckIndex = resolvedListIndex;
            pendingSelectMethod = 0;
            awaitingConfirm = true;
            TryApplyDeckSelection(addon, deck, resolvedListIndex, 0);
            framesSinceAttempt = DeckSelectRetryCooldownFrames;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomator] DeckSelect failed");
        }
    }

    public static void ResetSession()
    {
        ClearPending();
        ScreenActive = false;
        confirmedThisScreen = false;
        forceDismissedForMatch = false;
        boardDismissFrames = 0;
        boardVisibleFrames = 0;
        AttemptedDeckIndices.Clear();
        attemptCount = 0;
        framesSinceAttempt = 0;
        FramesOpen = 0;
    }

    public static void PrepareRetryWithOptimizedDeck(int deckId)
    {
        if (!TriadRunSession.ShouldContinue() || !TriadUiState.IsPrepDeckSelectVisible())
        {
            return;
        }

        if (TriadUiState.IsBoardVisible() || confirmedThisScreen)
        {
            return;
        }

        if (TriadLocalClientStructs.TryGetSelDeck(out var selDeck, false))
        {
            uiReaderPrep.RefreshDeckSelectList((nint)selDeck);
        }

        ScreenActive = true;
        ClearPending();
        AttemptedDeckIndices.Clear();
        attemptCount = 0;
        framesSinceAttempt = DeckSelectPostOptimizerCooldownFrames;
        TriadRun.BeginDeckSelectPostWriteCooldown();
    }

    private static void TickBoardVisibleRecoverDeck(AtkUnitBase* addon)
    {
        boardDismissFrames++;

        if (!addon->IsVisible)
        {
            try
            {
                addon->IsVisible = true;
                addon->Update(0);
            }
            catch (Exception ex)
            {
                Svc.Log.Verbose(ex, "[TriadAutomator] Could not re-show deck select for recovery");
            }
        }

        if (pendingProfileDeckId >= 0 && pendingDeckIndex >= 0)
        {
            TryApplyDeckSelection(addon, pendingProfileDeckId, pendingDeckIndex, pendingSelectMethod);
        }
        else if (IsAddonReady(addon))
        {
            uiReaderPrep.RefreshDeckSelectList((nint)addon);
            if (C.UseSimmedDeck && TrySelectPreferredProfileDeck(addon))
            {
                framesSinceAttempt = DeckSelectRetryCooldownFrames;
            }
            else if (C.UseSimmedDeck && TriadRun.ShouldTryVisibleSaucyDeckRowSelect() && TrySelectVisibleSaucyDeck(addon))
            {
                framesSinceAttempt = DeckSelectRetryCooldownFrames;
            }
        }

        TryCloseDeckSelectGracefully(addon);

        if (boardDismissFrames == DeckSelectBoardVisibleMaxFrames)
        {
            Svc.Chat.PrintError("[Saucy] Match started without a deck. Confirm deck selection manually.");
        }
    }

    internal static bool IsBoardHandsPopulated()
    {
        if (!TriadLocalClientStructs.TryGetBoard(out var board, false))
        {
            return false;
        }

        var blueCount = 0;
        var redCount = 0;
        for (var i = 0; i < 5; i++)
        {
            if (board->BlueDeck[i].HasCard)
            {
                blueCount++;
            }

            if (board->RedDeck[i].HasCard)
            {
                redCount++;
            }
        }

        return blueCount > 0 && redCount > 0;
    }
}
