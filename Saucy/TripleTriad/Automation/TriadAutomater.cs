using Dalamud.Utility;
using ECommons.Automation;
using ECommons.Automation.UIInput;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Saucy.TripleTriad.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using static ECommons.GenericHelpers;
using static Saucy.Saucy;

namespace Saucy.TripleTriad;

internal static unsafe class TriadAutomater
{
    public delegate int PlaceCardDelegate(nint addon);

    private const int MaxDeckSelectAttemptsPerScreen = 12;
    private const int DeckSelectRetryCooldownFrames = 15;
    private const int DeckSelectPostOptimizerCooldownFrames = Solver.DeckSelectPostProfileWriteFrames;
    private const int MaxDeckSelectMethods = 5;

    public static int DeckSelectFramesOpen { get; private set; }

    public static bool ModuleEnabled = false;
    public static Dictionary<uint, int> TempCardsWonList = [];

    public static bool PlayXTimes = false;
    public static bool PlayUntilCardDrops = false;
    public static int NumberOfTimes = 1;
    public static bool LogOutAfterCompletion = false;
    public static bool PlayUntilAllCardsDropOnce = false;

    public static int MatchesCompletedThisSession = 0;
    public static int SessionInitialPlayCount = 1;

    private static readonly HashSet<int> attemptedDeckIndices = [];
    private static bool deckSelectScreenActive;
    private static int deckSelectAttemptCount;
    private static int framesSinceRematchAttempt;
    private static int framesSinceMatchAcceptAttempt;
    private static int framesSinceDeckSelectAttempt;
    private static bool rematchPending;
    private static int pendingDeckIndex = -1;
    private static int pendingSelectMethod;
    private static bool awaitingDeckSelectConfirm;
    private static int framesSinceSessionEndDismiss;
    private static bool sessionEndQuitLogged;

    private const int RematchRetryCooldownFrames = 15;
    private const int MatchAcceptRetryCooldownFrames = 15;
    private const uint ResultQuitButtonId = 20;
    private const uint ResultRematchButtonId = 21;

    /// <summary>When true, posts brief /echo lines for triad move debugging.</summary>
    public const bool DebugTriadAutomation = false;

    public static bool PlaceCard(int which, int slot)
    {
        try
        {
            if (!TryGetAddonByName("TripleTriad", out AddonTripleTriad* addon))
            {
                return false;
            }

            Callback.Fire(&addon->AtkUnitBase, true, 14, (uint)slot + ((uint)which << 16));
            addon->AtkUnitBase.Update(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomater] PlaceCard failed");
            return false;
        }
    }

    public static void RunModule()
    {
        if (PlayUntilAllCardsDropOnce)
        {
            EnsureRunTargetCards(ResolveRunTargetNpc());
        }

        if (TTSolver.preGameDecks.Count > 0)
        {
            var selectedDeck = C.SelectedDeckIndex;
            if (selectedDeck >= 0 && !TTSolver.preGameDecks.ContainsKey(selectedDeck))
            {
                C.SelectedDeckIndex = -1;
            }
        }

        if (IsTriadBoardVisible() || IsTriadResultVisible())
        {
            ResetDeckSelectSession();
        }

        if (TryRunTriadBoard())
        {
            return;
        }

        if (IsTriadResultVisible())
        {
            TryRematch();
            return;
        }

        if (IsTriadRequestVisible() || IsPrepDeckSelectVisible())
        {
            EnsureAutomationSessionForMatchPrep();
        }

        if (!ShouldContinueTriadSession())
        {
            return;
        }

        if (IsTriadRequestVisible())
        {
            AcceptTriadMatch();
            return;
        }

        if (IsPrepDeckSelectVisible())
        {
            DeckSelect();
        }
    }

    public static bool IsTriadBoardVisible() =>
        TryGetAddonByName("TripleTriad", out AddonTripleTriad* triadAddon) &&
        triadAddon->AtkUnitBase.IsVisible;

    public static bool IsTriadResultVisible() =>
        TryGetAddonByName<AtkUnitBase>("TripleTriadResult", out var addon) && addon->IsVisible;

    public static bool CanFinalizeTriadSession() =>
        !ShouldContinueTriadSession() && !IsTriadBoardVisible() && !IsTriadResultVisible();

    private static bool TryRunTriadBoard()
    {
        if (!IsTriadBoardVisible())
        {
            return false;
        }

        if (!TryGetAddonByName("TripleTriad", out AddonTripleTriad* triadAddon))
        {
            return false;
        }

        uiReaderGame.SyncCurrentFromAddon((nint)triadAddon);

        // Any non-waiting turn state (includes forced-card / masked moves from special rules).
        var canPlace = triadAddon->TurnState != TurnState.Waiting;

        if (canPlace)
        {
            TTSolver.TickPlaceRetryCooldown();
            TTSolver.UpdateGame(uiReaderGame.currentState, automationTick: true);
        }

        if (canPlace && TTSolver.ShouldAttemptPlace())
        {
            if (PlaceCard(TTSolver.moveCardIdx, TTSolver.moveBoardIdx))
            {
                TTSolver.RecordPlaceAttempt();
            }

            return true;
        }

        return canPlace;
    }

    private static bool IsTriadRequestVisible() =>
        TryGetAddonByName<AtkUnitBase>("TripleTriadRequest", out var addon) && addon->IsVisible;

    private static bool IsPrepDeckSelectVisible() =>
        TryGetAddonByName<AtkUnitBase>("TripleTriadSelDeck", out var addon) &&
        addon->IsVisible &&
        !IsTriadBoardVisible() &&
        !IsTriadResultVisible();

    public static void RequestRematch() => rematchPending = true;

    public static void ClearRematchPending()
    {
        rematchPending = false;
        framesSinceRematchAttempt = 0;
    }

    public static void BeginAutomationSession()
    {
        MatchesCompletedThisSession = 0;
        if (PlayXTimes)
        {
            SyncPlayXTimesSession(NumberOfTimes);
        }

        ClearRematchPending();
        framesSinceSessionEndDismiss = 0;
        ResetDeckSelectSession();
        TTSolver.ResetRunTargetNpcSession();
    }

    /// <summary>Call when run mode or play count changes in the UI mid-session.</summary>
    public static void SyncPlayXTimesSession(int desiredPlayCount)
    {
        if (!PlayXTimes)
        {
            return;
        }

        SessionInitialPlayCount = Math.Max(1, desiredPlayCount);

        if (IsTriadResultVisible())
        {
            MatchesCompletedThisSession = Math.Max(MatchesCompletedThisSession, 1);
        }

        NumberOfTimes = Math.Max(0, SessionInitialPlayCount - MatchesCompletedThisSession);

        if (!ShouldContinueTriadSession())
        {
            RequestSessionEndDismiss();
        }
    }

    public static void OnRunModeSettingsChanged()
    {
        if (PlayXTimes)
        {
            SyncPlayXTimesSession(NumberOfTimes);
        }
        else if (PlayUntilCardDrops && NumberOfTimes <= 0)
        {
            NumberOfTimes = 1;
        }

        if (!ShouldContinueTriadSession())
        {
            RequestSessionEndDismiss();
        }
    }

    public static void RequestSessionEndDismiss()
    {
        ClearRematchPending();
        framesSinceSessionEndDismiss = 0;
        sessionEndQuitLogged = false;
    }

    public static void TryDismissResultIfSessionEnded()
    {
        if (ShouldContinueTriadSession() || !IsTriadResultVisible())
        {
            return;
        }

        if (!TryGetAddonByName<AtkUnitBase>("TripleTriadResult", out var addon))
        {
            return;
        }

        TryDismissTriadResult(addon);
    }

    /// <summary>Reset play counters when starting a new match after the previous run finished.</summary>
    public static void EnsureAutomationSessionForMatchPrep()
    {
        if (!ModuleEnabled || !PlayXTimes)
        {
            return;
        }

        if (NumberOfTimes > 0 && MatchesCompletedThisSession < SessionInitialPlayCount)
        {
            return;
        }

        var playCount = SessionInitialPlayCount > 0 ? SessionInitialPlayCount : 1;
        SessionInitialPlayCount = playCount;
        NumberOfTimes = playCount;
        MatchesCompletedThisSession = 0;
        ClearRematchPending();
        ResetDeckSelectSession();
    }

    public static bool IsAutomationFlowActive() =>
        IsTriadBoardVisible() ||
        IsTriadResultVisible() ||
        IsTriadRequestVisible() ||
        IsPrepDeckSelectVisible();

    private static int lastTargetNpcId = -1;

    public static GameNpcInfo? ResolveRunTargetNpc()
    {
        TTSolver.EnsureRunTargetNpcSynced();

        var npcId = TTSolver.preGameNpc?.Id ?? TTSolver.currentNpc?.Id ?? TTSolver.lastGameNpc?.Id ?? -1;
        if (npcId >= 0 && GameNpcDB.Get().mapNpcs.TryGetValue(npcId, out var npcInfo))
        {
            if (PlayUntilAllCardsDropOnce)
            {
                EnsureRunTargetCards(npcInfo);
            }

            return npcInfo;
        }

        return null;
    }

    public static bool ShouldContinueTriadSession()
    {
        if (PlayXTimes && NumberOfTimes <= 0)
        {
            return false;
        }

        if (PlayXTimes && MatchesCompletedThisSession >= SessionInitialPlayCount)
        {
            return false;
        }

        if (PlayUntilCardDrops && NumberOfTimes <= 0)
        {
            return false;
        }

        if (PlayUntilAllCardsDropOnce)
        {
            var runTargetNpc = ResolveRunTargetNpc();
            EnsureRunTargetCards(runTargetNpc);

            if (runTargetNpc != null && TempCardsWonList.Count == 0)
            {
                return false;
            }

            if (TempCardsWonList.Count > 0 && TempCardsWonList.All(x => x.Value >= NumberOfTimes))
            {
                return false;
            }
        }

        return true;
    }

    public static void EnsureRunTargetCards(GameNpcInfo? npcInfo)
    {
        if (!PlayUntilAllCardsDropOnce || npcInfo == null)
        {
            return;
        }

        if (lastTargetNpcId != npcInfo.npcId)
        {
            TempCardsWonList.Clear();
            lastTargetNpcId = npcInfo.npcId;
        }

        GameCardDB.Get().Refresh();
        foreach (var cardId in npcInfo.rewardCards)
        {
            var cardInfo = GameCardDB.Get().FindById(cardId);
            if (cardInfo == null)
            {
                continue;
            }

            if (!C.OnlyUnobtainedCards || !cardInfo.IsOwned)
            {
                TempCardsWonList.TryAdd((uint)cardId, 0);
            }
        }
    }

    private static bool TryRematch()
    {
        if (!IsTriadResultVisible())
        {
            return false;
        }

        if (!TryGetAddonByName<AtkUnitBase>("TripleTriadResult", out var addon))
        {
            return false;
        }

        if (!ShouldContinueTriadSession())
        {
            ClearRematchPending();
            if (framesSinceSessionEndDismiss <= 0)
            {
                if (TryDismissTriadResult(addon))
                {
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

        if (!ModuleEnabled)
        {
            return false;
        }

        if (!rematchPending)
        {
            return false;
        }

        if (framesSinceRematchAttempt > 0)
        {
            framesSinceRematchAttempt--;
            return true;
        }

        try
        {
            TryFireResultChoiceCallback(addon, 1);
            TryClickResultButton(addon, ResultRematchButtonId);

            framesSinceRematchAttempt = RematchRetryCooldownFrames;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomater] TryRematch failed");
        }

        return true;
    }

    private static bool TryDismissTriadResult(AtkUnitBase* addon)
    {
        if (!sessionEndQuitLogged)
        {
            sessionEndQuitLogged = true;
            Svc.Chat.Print("[Saucy] Session complete — quitting...");
        }

        if (TryClickResultButton(addon, ResultQuitButtonId))
        {
            return true;
        }

        TryFireResultChoiceCallback(addon, 0);

        if (TryClickResultButton(addon, ResultQuitButtonId))
        {
            return true;
        }

        if (TryClickLowestResultButton(addon, skipNodeId: ResultRematchButtonId))
        {
            return true;
        }

        try
        {
            addon->Close(true);
            addon->Update(0);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadAutomater] Result Close(true) failed");
        }

        return !addon->IsVisible;
    }

    private static bool TryClickResultButton(AtkUnitBase* addon, uint buttonId)
    {
        if (TryClickAddonButton(addon, FindResultButton(addon, (ushort)buttonId)))
        {
            return true;
        }

        var button = addon->GetComponentButtonById(buttonId);
        if (TryClickAddonButton(addon, button))
        {
            return true;
        }

        return false;
    }

    private static unsafe AtkComponentButton* TryGetButtonFromNode(AtkResNode* node)
    {
        if (node == null)
        {
            return null;
        }

        var button = node->GetAsAtkComponentButton();
        if (button != null)
        {
            return button;
        }

        if ((int)node->Type >= 1000)
        {
            var component = ((AtkComponentNode*)node)->Component;
            if (component != null)
            {
                return (AtkComponentButton*)component;
            }
        }

        return null;
    }

    private static unsafe AtkComponentButton* FindResultButton(AtkUnitBase* addon, ushort targetNodeId)
    {
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || node->NodeId != targetNodeId)
            {
                continue;
            }

            var button = TryGetButtonFromNode(node);
            if (button != null)
            {
                return button;
            }
        }

        foreach (var node in GUINodeUtils.GetAllChildNodes(addon->RootNode) ?? [])
        {
            if (node == null || node->NodeId != targetNodeId)
            {
                continue;
            }

            var button = TryGetButtonFromNode(node);
            if (button != null)
            {
                return button;
            }
        }

        return null;
    }

    private static unsafe bool TryClickLowestResultButton(AtkUnitBase* addon, uint skipNodeId)
    {
        AtkComponentButton* bestButton = null;
        ushort bestNodeId = ushort.MaxValue;

        foreach (var node in GUINodeUtils.GetAllChildNodes(addon->RootNode) ?? [])
        {
            if (node == null || node->NodeId == skipNodeId)
            {
                continue;
            }

            var button = TryGetButtonFromNode(node);
            if (button == null || !button->IsEnabled || button->AtkResNode == null || !button->AtkResNode->IsVisible())
            {
                continue;
            }

            if (node->NodeId < bestNodeId)
            {
                bestNodeId = (ushort)node->NodeId;
                bestButton = button;
            }
        }

        return bestButton != null && TryClickAddonButton(addon, bestButton);
    }

    private static bool TryClickAddonButton(AtkUnitBase* addon, AtkComponentButton* button)
    {
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
            Svc.Log.Verbose(ex, "[TriadAutomater] Addon button click failed");
            return false;
        }
    }

    private static void TryFireResultChoiceCallback(AtkUnitBase* addon, uint choice)
    {
        try
        {
            var values = stackalloc AtkValue[2];
            values[0] = new()
            {
                Type = AtkValueType.Int, Int = 0
            };
            values[1] = new()
            {
                Type = AtkValueType.UInt, UInt = choice
            };
            addon->FireCallback(2, values);
            addon->Update(0);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadAutomater] Result choice callback {0} failed", choice);
        }
    }

    private static void TryFireResultCallback(AtkUnitBase* addon, uint eventId, int value)
    {
        try
        {
            var values = stackalloc AtkValue[1];
            values[0] = new()
            {
                Type = AtkValueType.Int, Int = value
            };
            addon->FireCallback(eventId, values);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadAutomater] Result callback {0} failed", eventId);
        }
    }

    private static void DeckSelect()
    {
        try
        {
            if (!ShouldContinueTriadSession() || !IsPrepDeckSelectVisible())
            {
                ClearDeckSelectPending();
                ResetDeckSelectSession();
                return;
            }

            if (!TryGetAddonByName("TripleTriadSelDeck", out AtkUnitBase* addon))
            {
                ClearDeckSelectPending();
                ResetDeckSelectSession();
                return;
            }

            DeckSelectFramesOpen++;

            if (!deckSelectScreenActive)
            {
                ResetDeckSelectSession();
                deckSelectScreenActive = true;
            }

            TTSolver.TickDeckSelectPostWriteCooldown();
            TTSolver.EnsureRunTargetNpcSynced(deckSelectScreen: true);
            TTSolver.EnsureExistingSaucyDeckForPrep();

            if (framesSinceDeckSelectAttempt > 0)
            {
                framesSinceDeckSelectAttempt--;
                return;
            }

            if (awaitingDeckSelectConfirm)
            {
                if (!addon->IsVisible)
                {
                    ClearDeckSelectPending();
                    return;
                }

                if (pendingSelectMethod + 1 < MaxDeckSelectMethods)
                {
                    pendingSelectMethod++;
                    TryApplyDeckSelection(addon, pendingDeckIndex, pendingSelectMethod);
                    framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;
                    return;
                }

                attemptedDeckIndices.Add(pendingDeckIndex);
                deckSelectAttemptCount++;
                ClearDeckSelectPending();
                return;
            }

            if (deckSelectAttemptCount >= MaxDeckSelectAttemptsPerScreen)
            {
                if (deckSelectAttemptCount == MaxDeckSelectAttemptsPerScreen)
                {
                    Svc.Chat.PrintError("[Saucy] Could not select a deck automatically. Pick one manually.");
                    deckSelectAttemptCount++;
                }

                return;
            }

            if (!IsAddonReady(addon))
            {
                return;
            }

            if (!awaitingDeckSelectConfirm && pendingSelectMethod == 0 && deckSelectAttemptCount == 0 &&
                attemptedDeckIndices.Count == 0)
            {
                uiReaderPrep.RefreshDeckSelectList((nint)addon);
            }

            if (C.UseRecommendedDeck && deckSelectAttemptCount == 0 && attemptedDeckIndices.Count == 0)
            {
                if (TrySelectVisibleSaucyDeck(addon))
                {
                    deckSelectAttemptCount++;
                    framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;
                    return;
                }

                if (TryRecommendedDeckButton(addon))
                {
                    deckSelectAttemptCount++;
                    framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;
                    return;
                }

                if (uiReaderPrep.cachedState.decks.Count == 0 && TryBlindDeckSelect(addon))
                {
                    deckSelectAttemptCount++;
                    framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;
                    return;
                }
            }

            if (!TTSolver.TryGetDeckSelectCandidate(C.UseRecommendedDeck, C.SelectedDeckIndex, attemptedDeckIndices,
                    out var deck))
            {
                if (C.UseRecommendedDeck && TTSolver.HasOptimizedDeckApplied &&
                    TryRecommendedDeckButton(addon))
                {
                    deckSelectAttemptCount++;
                    framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;
                }

                return;
            }

            if (deck < 0)
            {
                if (C.UseRecommendedDeck && TTSolver.HasOptimizedDeckApplied &&
                    TryRecommendedDeckButton(addon))
                {
                    deckSelectAttemptCount++;
                    framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;
                    return;
                }

                if (TryRandomDeckButton(addon))
                {
                    deckSelectAttemptCount++;
                    framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;
                }

                return;
            }

            if (attemptedDeckIndices.Contains(deck))
            {
                return;
            }

            PrintDeckSelectAttemptMessage(deck);

            var listIndex = deck;
            if (!TTSolver.TryResolveDeckListIndex(deck, out var resolvedListIndex))
            {
                Svc.Chat.PrintError($"[Saucy] Could not find deck {deck + 1} in the selection list.");
                attemptedDeckIndices.Add(deck);
                deckSelectAttemptCount++;
                return;
            }

            listIndex = resolvedListIndex;
            pendingDeckIndex = listIndex;
            pendingSelectMethod = 0;
            awaitingDeckSelectConfirm = true;
            TryApplyDeckSelection(addon, listIndex, 0);
            framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomater] DeckSelect failed");
        }
    }

    private static bool TryBlindDeckSelect(AtkUnitBase* addon)
    {
        Svc.Chat.Print("[Saucy] Selecting first deck...");
        foreach (var listIndex in new[] { 0, 1, 2, 3, 4 })
        {
            TryFireDeckCallback(addon, 1, listIndex);
            TryFireDeckCallback(addon, 0, listIndex);
            addon->Update(0);
            if (!addon->IsVisible)
            {
                return true;
            }
        }

        return TryRecommendedDeckButton(addon) || TryClickAnyAddonButton(addon, skipRandom: true);
    }

    private static bool TryClickAnyAddonButton(AtkUnitBase* addon, bool skipRandom = false)
    {
        foreach (var buttonId in new uint[] { 0, 1, 2, 3, 4, 5, 6 })
        {
            if (skipRandom && buttonId == 3)
            {
                continue;
            }

            var button = addon->GetComponentButtonById(buttonId);
            if (button == null || !button->IsEnabled || button->AtkResNode == null || !button->AtkResNode->IsVisible())
            {
                continue;
            }

            try
            {
                button->ClickAddonButton(addon);
                addon->Update(0);
                if (!addon->IsVisible)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Verbose(ex, "[TriadAutomater] Addon button {0} click failed", buttonId);
            }
        }

        return false;
    }

    private static bool TrySelectVisibleSaucyDeck(AtkUnitBase* addon)
    {
        var expectedName = TTSolver.GetExpectedSaucyDeckName();
        for (var idx = 0; idx < uiReaderPrep.cachedState.decks.Count; idx++)
        {
            var deck = uiReaderPrep.cachedState.decks[idx];
            if (string.IsNullOrWhiteSpace(deck.name))
            {
                continue;
            }

            var isSaucyDeck = deck.name.Contains("(Saucy)", StringComparison.OrdinalIgnoreCase);
            if (!isSaucyDeck)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(expectedName) &&
                !deck.name.Equals(expectedName, StringComparison.OrdinalIgnoreCase) &&
                !deck.name.StartsWith(TTSolver.preGameNpc?.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Svc.Chat.Print($"[Saucy] Selecting \"{deck.name}\"...");
            pendingDeckIndex = idx;
            pendingSelectMethod = 0;
            awaitingDeckSelectConfirm = true;
            TryApplyDeckSelection(addon, idx, 0);
            return true;
        }

        return false;
    }

    private static void TryApplyDeckSelection(AtkUnitBase* addon, int deckIndex, int method)
    {
        switch (method)
        {
            case 0:
                TryClickDeckListRow(addon, deckIndex);
                break;
            case 1:
                TryFireDeckCallback(addon, 1, deckIndex);
                break;
            case 2:
                TryFireDeckCallback(addon, 0, deckIndex);
                break;
            case 3:
                TryFireDeckCallback(addon, 2, deckIndex);
                break;
            case 4:
                TryClickDeckConfirmButton(addon);
                break;
        }

        addon->Update(0);
    }

    private static void ClearDeckSelectPending()
    {
        pendingDeckIndex = -1;
        pendingSelectMethod = 0;
        awaitingDeckSelectConfirm = false;
    }

    private static bool TryRecommendedDeckButton(AtkUnitBase* addon)
    {
        foreach (var buttonId in new uint[] { 0, 2, 4, 1, 3, 5 })
        {
            var button = addon->GetComponentButtonById(buttonId);
            if (button == null || !button->IsEnabled || button->AtkResNode == null || !button->AtkResNode->IsVisible())
            {
                continue;
            }

            Svc.Chat.Print("[Saucy] Using recommended deck...");
            try
            {
                button->ClickAddonButton(addon);
                addon->Update(0);
                return true;
            }
            catch (Exception ex)
            {
                Svc.Log.Verbose(ex, "[TriadAutomater] Recommended deck button {0} click failed", buttonId);
            }
        }

        return false;
    }

    private static bool TryClickDeckConfirmButton(AtkUnitBase* addon)
    {
        foreach (var buttonId in new uint[] { 1, 0, 5 })
        {
            var button = addon->GetComponentButtonById(buttonId);
            if (button == null || !button->IsEnabled || button->AtkResNode == null || !button->AtkResNode->IsVisible())
            {
                continue;
            }

            try
            {
                button->ClickAddonButton(addon);
                addon->Update(0);
                return true;
            }
            catch (Exception ex)
            {
                Svc.Log.Verbose(ex, "[TriadAutomater] Deck confirm button {0} click failed", buttonId);
            }
        }

        return false;
    }

    private static bool TryRandomDeckButton(AtkUnitBase* addon)
    {
        if (deckSelectAttemptCount >= MaxDeckSelectAttemptsPerScreen)
        {
            return false;
        }

        var button = GetRandomDeckButton(addon);
        if (button == null || !button->IsEnabled || button->AtkResNode == null || !button->AtkResNode->IsVisible())
        {
            return false;
        }

        Svc.Chat.Print("[Saucy] Using random deck...");
        try
        {
            button->ClickAddonButton(addon);
            addon->Update(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[TriadAutomater] Random deck button click failed");
            return false;
        }
    }

    private static void PrintDeckSelectAttemptMessage(int deck)
    {
        if (deckSelectAttemptCount > 0 || attemptedDeckIndices.Count > 0)
        {
            Svc.Chat.Print($"[Saucy] Retrying with deck {deck + 1}...");
        }
        else if (C.UseRecommendedDeck && TTSolver.HasOptimizedDeckApplied)
        {
            Svc.Chat.Print($"[Saucy] Selecting optimized deck {deck + 1}...");
        }
        else
        {
            Svc.Chat.Print($"[Saucy] Selecting deck {deck + 1}...");
        }
    }

    private static void TryFireDeckCallback(AtkUnitBase* addon, int eventId, int value)
    {
        try
        {
            var values = stackalloc AtkValue[1];
            values[0] = new()
            {
                Type = AtkValueType.Int, Int = value
            };
            addon->FireCallback((uint)eventId, values);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadAutomater] Deck callback {0} failed for deck {1}", eventId, value);
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
            Svc.Log.Verbose(ex, "[TriadAutomater] Deck row button click failed");
            return false;
        }
    }

    private static void ResetDeckSelectSession()
    {
        ClearDeckSelectPending();
        deckSelectScreenActive = false;
        attemptedDeckIndices.Clear();
        deckSelectAttemptCount = 0;
        framesSinceDeckSelectAttempt = 0;
        DeckSelectFramesOpen = 0;
    }

    public static void PrepareRetryWithOptimizedDeck(int deckId)
    {
        if (!ShouldContinueTriadSession() || !IsPrepDeckSelectVisible())
        {
            return;
        }

        if (TryGetAddonByName("TripleTriadSelDeck", out AtkUnitBase* addon))
        {
            uiReaderPrep.RefreshDeckSelectList((nint)addon);
        }

        deckSelectScreenActive = true;
        ClearDeckSelectPending();
        attemptedDeckIndices.Clear();
        deckSelectAttemptCount = 0;
        framesSinceDeckSelectAttempt = DeckSelectPostOptimizerCooldownFrames;
        TTSolver.BeginDeckSelectPostWriteCooldown();
    }

    private static AtkComponentButton* GetRandomDeckButton(AtkUnitBase* addon)
    {
        var button = addon->GetComponentButtonById(3);
        if (button != null)
        {
            return button;
        }

        if (addon->UldManager.NodeListCount > 3)
        {
            return addon->UldManager.NodeList[3]->GetAsAtkComponentButton();
        }

        return null;
    }

    private static void AcceptTriadMatch()
    {
        try
        {
            if (!TryGetAddonByName<AtkUnitBase>("TripleTriadRequest", out var addon) || !addon->IsVisible)
            {
                framesSinceMatchAcceptAttempt = 0;
                return;
            }

            if (framesSinceMatchAcceptAttempt > 0)
            {
                framesSinceMatchAcceptAttempt--;
                return;
            }

            if (!IsAddonReady(addon))
            {
                return;
            }

            var values = stackalloc AtkValue[1];
            values[0] = new()
            {
                Type = AtkValueType.Int, Int = 0
            };
            addon->FireCallback(0, values);
            addon->Update(0);

            var challengeButton = addon->GetComponentButtonById(41);
            if (challengeButton != null && challengeButton->IsEnabled &&
                challengeButton->AtkResNode != null && challengeButton->AtkResNode->IsVisible())
            {
                try
                {
                    challengeButton->ClickAddonButton(addon);
                }
                catch (Exception clickEx)
                {
                    Svc.Log.Verbose(clickEx, "[TriadAutomater] Challenge button click fallback failed");
                }
            }

            framesSinceMatchAcceptAttempt = MatchAcceptRetryCooldownFrames;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomater] AcceptTriadMatch failed");
        }
    }

    public static bool Logout()
    {
        var isLoggedIn = Svc.Condition.Any();
        if (!isLoggedIn)
        {
            return true;
        }

        Chat.SendMessage("/logout");
        return true;
    }

    public static bool SelectYesLogout()
    {
        var addon = GetSpecificYesno(Svc.Data.GetExcelSheet<Addon>().GetRow(115).Text.ToDalamudString().GetText());
        if (addon == null)
        {
            return false;
        }
        new AddonMaster.SelectYesno(addon).Yes();
        return true;
    }

    internal static AtkUnitBase* GetSpecificYesno(params string[] s)
    {
        for (var i = 1; i < 100; i++)
        {
            try
            {
                var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("SelectYesno", i).Address;
                if (addon == null)
                {
                    return null;
                }
                if (IsAddonReady(addon))
                {
                    var textNode = addon->UldManager.NodeList[15]->GetAsAtkTextNode();
                    var text = textNode->NodeText.GetText();
                    if (text.EqualsAny(s))
                    {
                        Svc.Log.Verbose($"SelectYesno {s} addon {i}");
                        return addon;
                    }
                }
            }
            catch (Exception e)
            {
                e.Log();
                return null;
            }
        }
        return null;
    }
}
