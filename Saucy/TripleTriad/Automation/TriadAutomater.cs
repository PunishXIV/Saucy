using Dalamud.Utility;
using ECommons.Automation;
using ECommons.Automation.UIInput;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using static ECommons.GenericHelpers;

namespace Saucy.TripleTriad;

internal static unsafe class TriadAutomater
{
    private const int MaxDeckSelectAttemptsPerScreen = 12;
    // Long enough for the deck-select addon to close after a successful confirm; shorter values caused (2x) chat lines from method 0 + method 1 racing the settle check.
    private const int DeckSelectRetryCooldownFrames = 30;
    private const int DeckSelectStuckResetFrames = 300;
    private const int DeckSelectPostOptimizerCooldownFrames = Solver.DeckSelectPostProfileWriteFrames;
    private const int MaxDeckSelectMethods = 5;
    private const int ManualDeckSelectMethods = 5;
    private static readonly uint[] DeckSelectRecommendedButtonIds =
    [
        0, 2, 4
    ];
    private static readonly uint[] DeckSelectRandomButtonIds =
    [
        3
    ];
    private static readonly uint[] DeckSelectConfirmButtonIds =
    [
        5, 1
    ];
    private const int ResultOutcomeFallbackFrames = 45;

    private const int RematchRetryCooldownFrames = 15;
    private const int MatchAcceptRetryCooldownFrames = 15;
    private const ushort ResultQuitNodeId = 20;
    private const ushort ResultRematchNodeId = 21;

    public static bool ModuleEnabled = false;
    public static Dictionary<uint, int> TempCardsWonList = [];

    public static bool PlayXTimes;
    public static bool PlayUntilCardDrops;
    public static int NumberOfTimes = 1;
    public static bool LogOutAfterCompletion = false;
    public static bool PlayUntilAllCardsDropOnce;

    public static int MatchesCompletedThisSession;
    public static int SessionInitialPlayCount = 1;

    private static readonly HashSet<int> attemptedDeckIndices = [];
    private static bool deckSelectScreenActive;
    private static bool deckSelectConfirmedThisScreen;
    private static int deckSelectAttemptCount;
    private static int framesSinceRematchAttempt;
    private static int framesSinceMatchAcceptAttempt;
    private static int framesSinceDeckSelectAttempt;
    private static bool rematchPending;
    private static bool playUntilAnyCardDropped;
    private static bool sessionEndDismissRequested;
    private static bool pendingRegistrationDismiss;
    private static int pendingDeckIndex = -1;
    private static int pendingProfileDeckId = -1;
    private static int pendingSelectMethod;
    private static bool awaitingDeckSelectConfirm;
    private static int framesSinceSessionEndDismiss;
    private static nint lastRecordedResultAddonPtr;
    private static int framesWaitingForResultOutcome;
    private static readonly HashSet<int> ownedRewardCardsAtMatchStart = [];
    private static bool matchRewardOwnershipSnapshotted;
    private static bool triadBoardActiveForSnapshot;
    private static int pendingCardDropVerifyFrames;
    private static int pendingCardDropVerifyAttemptsLeft;
    private static uint pendingCardDropVerifyItemId;
    private static readonly HashSet<int> farmDropsCountedThisMatch = [];

    private static nint cachedResultButtonAddonPtr;
    private static ushort resolvedRematchNodeId;
    private static ushort resolvedQuitNodeId;
    private static bool resultButtonsResolved;

    private static int lastTargetNpcId = -1;

    public static int DeckSelectFramesOpen { get; private set; }

    public static bool CardFarmSessionActive { get; private set; }

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

    public static void PrintTriadDeckLog(string message)
    {
        Svc.Log.Info(message);
        if (C.LogTriadDeckOptimizerToChat)
        {
            Svc.Chat.Print(message);
        }
    }

    private static void PrintManualDeckSelectLog(string message) => Svc.Log.Verbose(message);

    public static void RunModule()
    {
        if (PlayUntilAllCardsDropOnce)
        {
            EnsureCardFarmArmed();
            EnsureRunTargetCards(ResolveRunTargetNpc());
        }

        if (TickPendingCardDropVerification())
        {
            DetectAndProcessCardFarmDrops(pendingCardDropVerifyItemId);
        }

        if (TTSolver.preGameDecks.Count > 0 && C.UseRecommendedDeck)
        {
            var selectedDeck = C.SelectedDeckIndex;
            if (selectedDeck >= 0 && !TTSolver.preGameDecks.ContainsKey(selectedDeck))
            {
                C.SelectedDeckIndex = -1;
            }
        }

        if (IsTriadBoardVisible() || IsTriadResultVisible())
        {
            if (deckSelectScreenActive)
            {
                ResetDeckSelectSession();
            }

            TryForceCloseDeckSelectOverlay();
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

        if (IsMatchRegistrationVisible() || IsPrepDeckSelectVisible())
        {
            ClearRematchPending();
            EnsureAutomationSessionForMatchPrep();
        }

        if (!ShouldContinueTriadSession())
        {
            if (!ModuleEnabled)
            {
                return;
            }

            // After a finished session, the game often re-pops Match Registration once the result dismisses. One-shot: click Quit so the player doesn't have to.
            if (pendingRegistrationDismiss && IsMatchRegistrationVisible())
            {
                if (TryDismissMatchRegistration())
                {
                    pendingRegistrationDismiss = false;
                }

                return;
            }

            if (!(IsCardFarmModeActive() && CardFarmHasPendingDrops() && IsAutomationFlowActive()))
            {
                return;
            }
        }

        if (IsMatchRegistrationVisible())
        {
            ResetMatchRewardOwnershipSnapshot();
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
        !ShouldContinueTriadSession() &&
        !IsTriadBoardVisible() &&
        !IsTriadResultVisible() &&
        !IsMatchRegistrationVisible() &&
        !IsPrepDeckSelectVisible();

    private static bool TryRunTriadBoard()
    {
        if (!IsTriadBoardVisible())
        {
            triadBoardActiveForSnapshot = false;
            return false;
        }

        if (!TryGetAddonByName("TripleTriad", out AddonTripleTriad* triadAddon))
        {
            return false;
        }

        if (IsDeckSelectOverlayVisible())
        {
            TryForceCloseDeckSelectOverlay();
        }

        if (!triadBoardActiveForSnapshot)
        {
            triadBoardActiveForSnapshot = true;
            SnapshotMatchRewardOwnership();
        }

        uiReaderGame.SyncCurrentFromAddon((nint)triadAddon);

        // Any non-waiting turn state (includes forced-card / masked moves from special rules).
        var canPlace = triadAddon->TurnState != TurnState.Waiting;

        if (canPlace)
        {
            TTSolver.TickPlaceRetryCooldown();
            TTSolver.UpdateGame(uiReaderGame.currentState, true);
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

    public static bool IsMatchRegistrationVisible() =>
        TryGetAddonByName<AtkUnitBase>("TripleTriadRequest", out var addon) && addon->IsVisible;

    public static bool IsPrepDeckSelectVisible() =>
        IsDeckSelectOverlayVisible() &&
        !IsTriadBoardVisible() &&
        !IsTriadResultVisible();

    private static bool IsDeckSelectOverlayVisible() =>
        TryGetAddonByName<AtkUnitBase>("TripleTriadSelDeck", out var addon) && addon->IsVisible;

    public static void RequestRematch()
    {
        rematchPending = true;
        sessionEndDismissRequested = false;
    }

    public static void ClearRematchPending()
    {
        rematchPending = false;
        framesSinceRematchAttempt = 0;
    }

    public static void ApplyRunMode(TriadRunMode mode)
    {
        PlayXTimes = mode == TriadRunMode.PlayXTimes;
        PlayUntilCardDrops = mode == TriadRunMode.PlayUntilAnyCard;
        PlayUntilAllCardsDropOnce = mode == TriadRunMode.PlayUntilAllCards;
    }

    /// <summary>True while any tracked NPC reward card is below the per-card target count.</summary>
    public static bool IsCardFarmModeActive() => ModuleEnabled && PlayUntilAllCardsDropOnce;

    /// <summary>Card-farm mode builds and selects Saucy decks even when recommended-deck mode is off.</summary>
    public static bool ShouldAutoManageDeck() => C.UseRecommendedDeck || IsCardFarmModeActive();

    /// <summary>True only when every tracked card has been obtained at least once this session.</summary>
    public static bool IsCardFarmComplete()
    {
        if (TempCardsWonList.Count == 0)
        {
            return false;
        }

        foreach (var wins in TempCardsWonList.Values)
        {
            if (wins < 1)
            {
                return false;
            }
        }

        return true;
    }

    public static int GetCardFarmCompletedCount()
    {
        var completed = 0;
        foreach (var wins in TempCardsWonList.Values)
        {
            if (wins >= 1)
            {
                completed++;
            }
        }

        return completed;
    }

    public static bool CardFarmHasPendingDrops()
    {
        if (!CardFarmSessionActive && !IsCardFarmModeActive())
        {
            return false;
        }

        if (TempCardsWonList.Count == 0)
        {
            return CardFarmSessionActive;
        }

        return !IsCardFarmComplete();
    }

    public static void EnsureCardFarmArmed()
    {
        if (!ModuleEnabled || !PlayUntilAllCardsDropOnce)
        {
            return;
        }

        CardFarmSessionActive = true;
        if (TempCardsWonList.Count == 0)
        {
            StartCardFarmTargets(ResolveRunTargetNpc());
        }
    }

    public static void ActivateCardFarmSession(GameNpcInfo? npcInfo = null, bool resetProgress = false)
    {
        CardFarmSessionActive = true;
        TTSolver.EnsureRunTargetNpcSynced();
        var targetNpc = npcInfo ?? ResolveRunTargetNpc();
        if (resetProgress || TempCardsWonList.Count == 0)
        {
            StartCardFarmTargets(targetNpc);
        }
        else
        {
            EnsureRunTargetCards(targetNpc);
        }
    }

    public static void DeactivateCardFarmSession(bool clearProgress = false)
    {
        CardFarmSessionActive = false;
        if (clearProgress)
        {
            ClearCardFarmProgress();
        }
    }

    public static void ClearCardFarmProgress()
    {
        TempCardsWonList.Clear();
        lastTargetNpcId = -1;
        farmDropsCountedThisMatch.Clear();
    }

    public static void StartCardFarmTargets(GameNpcInfo? npcInfo)
    {
        if (npcInfo == null)
        {
            return;
        }

        lastTargetNpcId = npcInfo.npcId;
        TempCardsWonList.Clear();

        GameCardDB.Get().Refresh();
        foreach (var cardId in npcInfo.rewardCards)
        {
            if (GameCardDB.Get().FindById(cardId) == null)
            {
                continue;
            }

            if (C.OnlyUnobtainedCards && TriadMemoryReads.TryIsCardOwned(cardId))
            {
                continue;
            }

            TempCardsWonList[(uint)cardId] = 0;
        }

        farmDropsCountedThisMatch.Clear();
    }

    public static void DetectAndProcessCardFarmDrops(uint resultRewardItemId = 0)
    {
        if (TempCardsWonList.Count == 0)
        {
            return;
        }

        GameCardDB.Get().Refresh();

        if (resultRewardItemId > 0)
        {
            var hinted = GameCardDB.Get().FindByItemId(resultRewardItemId);
            if (hinted != null && TryProcessNewFarmDrop(hinted, resultRewardItemId))
            {
                return;
            }
        }

        if (matchRewardOwnershipSnapshotted &&
            TryGetVerifiedNpcCardDrop(out var droppedCard, resultRewardItemId) &&
            droppedCard != null)
        {
            TryProcessNewFarmDrop(droppedCard, resultRewardItemId);
        }
    }

    private static bool TryProcessNewFarmDrop(GameCardInfo cardInfo, uint resultRewardItemId = 0)
    {
        if (!TempCardsWonList.ContainsKey((uint)cardInfo.CardId))
        {
            return false;
        }

        if (farmDropsCountedThisMatch.Contains(cardInfo.CardId))
        {
            return false;
        }

        var resultConfirmsDrop = resultRewardItemId > 0 && cardInfo.ItemId == resultRewardItemId;
        if (resultConfirmsDrop)
        {
            RecordFarmCardDrop(cardInfo);
            return true;
        }

        if (!matchRewardOwnershipSnapshotted || ownedRewardCardsAtMatchStart.Contains(cardInfo.CardId))
        {
            return false;
        }

        if (!TriadMemoryReads.TryIsCardOwned(cardInfo.CardId))
        {
            return false;
        }

        RecordFarmCardDrop(cardInfo);
        return true;
    }

    private static void RecordFarmCardDrop(GameCardInfo cardInfo)
    {
        farmDropsCountedThisMatch.Add(cardInfo.CardId);
        TempCardsWonList[(uint)cardInfo.CardId] = TempCardsWonList[(uint)cardInfo.CardId] + 1;

        C.UpdateStats(stats =>
        {
            stats.CardsDroppedWithSaucy++;

            if (stats.CardsWon.ContainsKey((uint)cardInfo.CardId))
            {
                stats.CardsWon[(uint)cardInfo.CardId] += 1;
            }
            else
            {
                stats.CardsWon[(uint)cardInfo.CardId] = 1;
            }
        });
        C.Save();
    }

    public static void BeginAutomationSession()
    {
        MatchesCompletedThisSession = 0;
        playUntilAnyCardDropped = false;
        if (PlayXTimes)
        {
            SyncPlayXTimesSession(NumberOfTimes);
        }
        else if (PlayUntilAllCardsDropOnce)
        {
            if (NumberOfTimes <= 0)
            {
                NumberOfTimes = 1;
            }

            TTSolver.EnsureRunTargetNpcSynced();
            RefreshRunTargetFromPrep();
            ActivateCardFarmSession(ResolveRunTargetNpc(), resetProgress: true);
        }
        else
        {
            DeactivateCardFarmSession();
        }

        ClearRematchPending();
        sessionEndDismissRequested = false;
        framesSinceSessionEndDismiss = 0;
        pendingRegistrationDismiss = false;
        ResetResultMatchRecording();
        ResetMatchRewardOwnershipSnapshot();
        ResetDeckSelectSession();
        TTSolver.ResetRunTargetNpcSession();
    }

    /// <summary>Call when run mode or play count changes in the UI mid-session.</summary>
    public static void SyncPlayXTimesSession(int desiredPlayCount)
    {
        if (!PlayXTimes || PlayUntilAllCardsDropOnce || CardFarmSessionActive)
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
        else if (PlayUntilCardDrops)
        {
            playUntilAnyCardDropped = false;
            if (NumberOfTimes <= 0)
            {
                NumberOfTimes = 1;
            }
        }
        else
        {
            playUntilAnyCardDropped = false;
        }

        if (PlayUntilAllCardsDropOnce)
        {
            if (CardFarmHasPendingDrops())
            {
                sessionEndDismissRequested = false;
            }
            else if (IsTriadResultVisible() && IsCardFarmComplete())
            {
                DeactivateCardFarmSession();
                RequestSessionEndDismiss();
            }

            return;
        }

        if (CardFarmSessionActive)
        {
            DeactivateCardFarmSession();
        }

        if (!ShouldContinueTriadSession())
        {
            RequestSessionEndDismiss();
        }
    }

    public static void RequestSessionEndDismiss()
    {
        ClearRematchPending();
        sessionEndDismissRequested = true;
        framesSinceSessionEndDismiss = 0;
        // After the result is dismissed, the registration screen often re-pops; mark it for one-shot auto-quit.
        pendingRegistrationDismiss = true;
    }

    public static void ResetResultMatchRecording()
    {
        lastRecordedResultAddonPtr = nint.Zero;
        ResetResultButtonBindings();
    }

    private static void ResetResultButtonBindings()
    {
        cachedResultButtonAddonPtr = nint.Zero;
        resultButtonsResolved = false;
        resolvedRematchNodeId = 0;
        resolvedQuitNodeId = 0;
    }

    private static bool IsResultMatchRecorded(nint resultAddonPtr) =>
        resultAddonPtr != nint.Zero && resultAddonPtr == lastRecordedResultAddonPtr;

    public static void ResetMatchRewardOwnershipSnapshot()
    {
        matchRewardOwnershipSnapshotted = false;
        ownedRewardCardsAtMatchStart.Clear();
        farmDropsCountedThisMatch.Clear();
    }

    public static void SnapshotMatchRewardOwnership()
    {
        farmDropsCountedThisMatch.Clear();
        ownedRewardCardsAtMatchStart.Clear();
        var npc = ResolveRunTargetNpc();
        if (npc == null)
        {
            matchRewardOwnershipSnapshotted = false;
            return;
        }

        GameCardDB.Get().Refresh();
        foreach (var cardId in npc.rewardCards)
        {
            if (TriadMemoryReads.TryIsCardOwned(cardId))
            {
                ownedRewardCardsAtMatchStart.Add(cardId);
            }
        }

        matchRewardOwnershipSnapshotted = true;
    }

    /// <summary>
    ///     True when an NPC reward card was not owned at match start and is owned now.
    ///     Uses ownership diff; optional result item id helps disambiguate multi-reward NPCs.
    /// </summary>
    public static bool TryGetVerifiedNpcCardDrop(out GameCardInfo? droppedCard, uint resultRewardItemId = 0)
    {
        droppedCard = null;
        if (!matchRewardOwnershipSnapshotted)
        {
            return false;
        }

        GameCardDB.Get().Refresh();
        var npc = ResolveRunTargetNpc();
        if (npc == null)
        {
            return false;
        }

        if (resultRewardItemId > 0)
        {
            var hinted = GameCardDB.Get().FindByItemId(resultRewardItemId);
            if (hinted != null && npc.rewardCards.Contains(hinted.CardId) &&
                CanCountNpcRewardDrop(hinted.CardId))
            {
                droppedCard = hinted;
                return true;
            }
        }

        foreach (var cardId in npc.rewardCards)
        {
            if (!CanCountNpcRewardDrop(cardId))
            {
                continue;
            }

            droppedCard = GameCardDB.Get().FindById(cardId);
            if (droppedCard != null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanCountNpcRewardDrop(int cardId)
    {
        if (ownedRewardCardsAtMatchStart.Contains(cardId))
        {
            return false;
        }

        if (!TriadMemoryReads.TryIsCardOwned(cardId))
        {
            return false;
        }

        if (PlayUntilAllCardsDropOnce && TempCardsWonList.Count > 0 &&
            !TempCardsWonList.ContainsKey((uint)cardId))
        {
            return false;
        }

        return true;
    }

    public static void ScheduleCardDropVerification(uint resultRewardItemId)
    {
        if (resultRewardItemId > 0)
        {
            var hinted = GameCardDB.Get().FindByItemId(resultRewardItemId);
            if (hinted != null && farmDropsCountedThisMatch.Contains(hinted.CardId))
            {
                return;
            }
        }

        pendingCardDropVerifyFrames = 5;
        pendingCardDropVerifyAttemptsLeft = 12;
        pendingCardDropVerifyItemId = resultRewardItemId;
    }

    public static bool TickPendingCardDropVerification()
    {
        if (pendingCardDropVerifyAttemptsLeft <= 0)
        {
            return false;
        }

        if (--pendingCardDropVerifyFrames > 0)
        {
            return false;
        }

        pendingCardDropVerifyFrames = 5;
        pendingCardDropVerifyAttemptsLeft--;

        if (pendingCardDropVerifyItemId > 0)
        {
            var hinted = GameCardDB.Get().FindByItemId(pendingCardDropVerifyItemId);
            if (hinted != null && farmDropsCountedThisMatch.Contains(hinted.CardId))
            {
                pendingCardDropVerifyAttemptsLeft = 0;
                return false;
            }
        }

        return TempCardsWonList.Count > 0;
    }

    public static void ProcessVerifiedCardDrop(GameCardInfo droppedCard)
    {
        if (droppedCard == null)
        {
            return;
        }

        if (IsCardFarmModeActive() && TempCardsWonList.ContainsKey((uint)droppedCard.CardId))
        {
            TryProcessNewFarmDrop(droppedCard, 0);
            return;
        }

        if (PlayUntilCardDrops)
        {
            NotifyPlayUntilAnyCardDropped();
        }

        C.UpdateStats(stats =>
        {
            stats.CardsDroppedWithSaucy++;

            if (stats.CardsWon.ContainsKey((uint)droppedCard.CardId))
            {
                stats.CardsWon[(uint)droppedCard.CardId] += 1;
            }
            else
            {
                stats.CardsWon[(uint)droppedCard.CardId] = 1;
            }
        });
        C.Save();
    }

    /// <summary>
    ///     Records one completed match per result addon instance and requests rematch or quit.
    ///     Called from CheckResults; TryRematch uses the overload with requireActionButtons when UI parsing fails.
    /// </summary>
    public static void RecordMatchResultIfNeeded(nint resultAddonPtr = default, bool requireActionButtons = false)
    {
        if (!ModuleEnabled)
        {
            return;
        }

        if (resultAddonPtr == nint.Zero)
        {
            if (!TryGetAddonByName<AtkUnitBase>("TripleTriadResult", out var addon) || !addon->IsVisible)
            {
                return;
            }

            resultAddonPtr = (nint)addon;
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

        if (requireActionButtons && !IsTriadResultScreenReady(resultAddon))
        {
            return;
        }

        lastRecordedResultAddonPtr = resultAddonPtr;
        EnsureCardFarmArmed();

        if (IsCardFarmModeActive())
        {
            sessionEndDismissRequested = false;
            if (!IsCardFarmComplete())
            {
                RequestRematch();
            }
            else
            {
                DeactivateCardFarmSession();
                RequestSessionEndDismiss();
                Svc.Framework.Run(TryDismissResultIfSessionEnded);
            }

            return;
        }

        if (PlayUntilCardDrops && playUntilAnyCardDropped)
        {
            DeactivateCardFarmSession();
            RequestSessionEndDismiss();
            Svc.Framework.Run(TryDismissResultIfSessionEnded);
            return;
        }

        if (PlayXTimes && !PlayUntilAllCardsDropOnce && !PlayUntilCardDrops)
        {
            MatchesCompletedThisSession++;
            if (NumberOfTimes > 0)
            {
                NumberOfTimes--;
            }
        }

        if (ShouldContinueTriadSession())
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
        IsMatchRegistrationVisible() ||
        IsPrepDeckSelectVisible();

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

    public static void NotifyPlayUntilAnyCardDropped()
    {
        if (!PlayUntilCardDrops)
        {
            return;
        }

        playUntilAnyCardDropped = true;
    }

    public static bool ShouldContinueTriadSession()
    {
        if (IsCardFarmModeActive())
        {
            return !IsCardFarmComplete();
        }

        if (PlayUntilCardDrops)
        {
            return ModuleEnabled && !playUntilAnyCardDropped;
        }

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

        if (PlayUntilCardDrops && playUntilAnyCardDropped)
        {
            return false;
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
            if (CardFarmSessionActive && TempCardsWonList.Count > 0)
            {
                return;
            }

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

            var isOwned = TriadMemoryReads.TryIsCardOwned(cardId);
            if (!C.OnlyUnobtainedCards || !isOwned)
            {
                TempCardsWonList.TryAdd((uint)cardId, 0);
            }
        }
    }

    /// <summary>Sync NPC and missing-card targets from match registration or deck select.</summary>
    public static void RefreshRunTargetFromPrep()
    {
        if (!PlayUntilAllCardsDropOnce)
        {
            return;
        }

        try
        {
            if (IsMatchRegistrationVisible())
            {
                uiReaderPrep.SyncMatchRegistrationFromLiveAddon();
            }
            else if (IsPrepDeckSelectVisible())
            {
                uiReaderPrep.SyncDeckSelectFromLiveAddon();
            }

            TTSolver.EnsureRunTargetNpcSynced(
                deckSelectScreen: uiReaderPrep.HasDeckSelectionUI && !IsMatchRegistrationVisible());
            EnsureRunTargetCards(ResolveRunTargetNpc());
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomater] RefreshRunTargetFromPrep failed");
        }
    }

    private static bool TryRematch()
    {
        if (!IsTriadResultVisible())
        {
            ResetResultMatchRecording();
            framesWaitingForResultOutcome = 0;
            sessionEndDismissRequested = false;
            return false;
        }

        if (!TryGetAddonByName<AtkUnitBase>("TripleTriadResult", out var addon))
        {
            return false;
        }

        EnsureCardFarmArmed();

        if (IsResultMatchRecorded((nint)addon))
        {
            framesWaitingForResultOutcome = 0;
        }
        else if (++framesWaitingForResultOutcome >= ResultOutcomeFallbackFrames)
        {
            uiReaderMatchResults.ForceNotifyFromFallback((nint)addon);
            framesWaitingForResultOutcome = 0;
        }

        if (IsCardFarmModeActive() && CardFarmHasPendingDrops())
        {
            sessionEndDismissRequested = false;
            if (!rematchPending && IsResultMatchRecorded((nint)addon))
            {
                RequestRematch();
            }
        }

        if (ModuleEnabled &&
            !IsResultMatchRecorded((nint)addon) &&
            IsTriadResultScreenReady(addon))
        {
            RecordMatchResultIfNeeded((nint)addon, true);
        }

        if (sessionEndDismissRequested)
        {
            if (IsCardFarmModeActive() && CardFarmHasPendingDrops())
            {
                sessionEndDismissRequested = false;
                if (!rematchPending)
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

        if (!ModuleEnabled || !rematchPending)
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
            if (!IsResultScreenReadyForRematch(addon))
            {
                return true;
            }

            if (DidEnterRematchFlow())
            {
                ClearRematchPending();
                return true;
            }

            if (!EnsureResultButtonsResolved(addon))
            {
                return true;
            }

            if (resolvedRematchNodeId == 0)
            {
                framesSinceRematchAttempt = RematchRetryCooldownFrames;
                return true;
            }

            TryClickResultButton(addon, resolvedRematchNodeId, false);

            if (DidEnterRematchFlow())
            {
                ClearRematchPending();
            }
            else if (!addon->IsVisible && IsCardFarmModeActive() && CardFarmHasPendingDrops())
            {
                RequestRematch();
            }

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
        EnsureResultButtonsResolved(addon);

        if (resolvedQuitNodeId != 0 &&
            TryClickResultButton(addon, resolvedQuitNodeId, false))
        {
            return true;
        }

        if (resolvedQuitNodeId != 0 &&
            TryClickResultButton(addon, resolvedQuitNodeId))
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

    private static bool DidEnterRematchFlow() =>
        IsPrepDeckSelectVisible() || IsMatchRegistrationVisible();

    private static bool TryClickResultButton(AtkUnitBase* addon, ushort nodeId, bool requireEnabled = true) =>
        TryClickAddonButton(addon, FindResultButton(addon, nodeId), requireEnabled);

    private static bool IsResultScreenReadyForRematch(AtkUnitBase* addon)
    {
        if (!addon->IsReady)
        {
            return false;
        }

        var quitButton = FindResultButton(addon, ResultQuitNodeId);
        var rematchButton = FindResultButton(addon, ResultRematchNodeId);
        return quitButton != null &&
               rematchButton != null &&
               quitButton->AtkResNode != null &&
               rematchButton->AtkResNode != null &&
               quitButton->AtkResNode->IsVisible() &&
               rematchButton->AtkResNode->IsVisible();
    }

    private static bool EnsureResultButtonsResolved(AtkUnitBase* addon)
    {
        var addonPtr = (nint)addon;
        if (resultButtonsResolved && cachedResultButtonAddonPtr == addonPtr)
        {
            return resolvedRematchNodeId != 0;
        }

        var button20 = FindResultButton(addon, ResultQuitNodeId);
        var button21 = FindResultButton(addon, ResultRematchNodeId);
        if (button20 == null || button21 == null ||
            button20->AtkResNode == null || button21->AtkResNode == null ||
            !button20->AtkResNode->IsVisible() || !button21->AtkResNode->IsVisible())
        {
            return false;
        }

        var label20 = GetResultButtonLabel(button20);
        var label21 = GetResultButtonLabel(button21);

        if (!string.IsNullOrWhiteSpace(label20) && LabelMatchesRematch(label20) &&
            !string.IsNullOrWhiteSpace(label21) && LabelMatchesQuit(label21))
        {
            ApplyResultButtonBindings(addonPtr, ResultQuitNodeId, ResultRematchNodeId);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(label21) && LabelMatchesRematch(label21) &&
            !string.IsNullOrWhiteSpace(label20) && LabelMatchesQuit(label20))
        {
            ApplyResultButtonBindings(addonPtr, ResultRematchNodeId, ResultQuitNodeId);
            return true;
        }

        return false;
    }

    private static void ApplyResultButtonBindings(nint addonPtr, ushort rematchNodeId, ushort quitNodeId)
    {
        resolvedRematchNodeId = rematchNodeId;
        resolvedQuitNodeId = quitNodeId;
        cachedResultButtonAddonPtr = addonPtr;
        resultButtonsResolved = true;
    }

    private static bool LabelMatchesRematch(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        foreach (var token in new[]
        {
            "Rematch", "Revanche", "Erneutes Spiel", "Revenge", "再戦", "再戦する", "リマッチ"
        })
        {
            if (label.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LabelMatchesQuit(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        foreach (var token in new[]
        {
            "Quit", "Beenden", "Quitter", "Abandon", "Exit", "やめる", "終了"
        })
        {
            if (label.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetResultButtonLabel(AtkComponentButton* button)
    {
        if (button == null)
        {
            return null;
        }

        var comp = (AtkComponentBase*)button;
        for (var i = 0; i < comp->UldManager.NodeListCount; i++)
        {
            var node = comp->UldManager.NodeList[i];
            if (node == null)
            {
                continue;
            }

            var text = GUINodeUtils.GetNodeText(node);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }

            if ((int)node->Type < 1000)
            {
                continue;
            }

            var inner = ((AtkComponentNode*)node)->Component;
            if (inner == null)
            {
                continue;
            }

            for (var j = 0; j < inner->UldManager.NodeListCount; j++)
            {
                text = GUINodeUtils.GetNodeText(inner->UldManager.NodeList[j]);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }

        foreach (var node in GUINodeUtils.GetAllChildNodes(button->AtkResNode) ?? [])
        {
            var text = GUINodeUtils.GetNodeText(node);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }

    internal static bool HasVisibleResultActionButtons(AtkUnitBase* addon)
    {
        foreach (var node in GUINodeUtils.GetAllChildNodes(addon->RootNode) ?? [])
        {
            if (node != null &&
                (node->NodeId == ResultQuitNodeId || node->NodeId == ResultRematchNodeId) &&
                node->IsVisible())
            {
                return true;
            }
        }

        return false;
    }

    private static AtkComponentButton* TryGetButtonFromNode(AtkResNode* node)
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

    private static AtkComponentButton* FindResultButton(AtkUnitBase* addon, ushort targetNodeId)
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

    internal static bool IsTriadResultScreenReady(AtkUnitBase* addon) =>
        FindResultButton(addon, ResultQuitNodeId) != null ||
        FindResultButton(addon, ResultRematchNodeId) != null ||
        HasVisibleResultActionButtons(addon);

    internal static unsafe bool TryClickGoldSaucerCardGridButton(nint addonPtr, int pageIndex, int cellIndex)
    {
        if (addonPtr == nint.Zero || cellIndex < 0 || cellIndex >= 30)
        {
            return false;
        }

        var addon = (AddonGSInfoCardList*)addonPtr;
        var atkUnit = (AtkUnitBase*)addon;

        if (pageIndex >= 0 && pageIndex != addon->SelectedPage)
        {
            addon->RequestedPage = pageIndex;
            addon->TabController.SetTabIndexAndCallBack(pageIndex);
            atkUnit->Update(0);
        }

        return TryClickGoldSaucerCardCell(addonPtr, cellIndex);
    }

    internal static unsafe bool TryClickGoldSaucerCardCell(nint addonPtr, int cellIndex)
    {
        if (addonPtr == nint.Zero || cellIndex < 0 || cellIndex >= 30)
        {
            return false;
        }

        var addon = (AddonGSInfoCardList*)addonPtr;
        var cardButton = (AtkComponentButton*)(*(nint*)((byte*)addon + 0x3D0 + (cellIndex * sizeof(nint))));
        return TryClickAddonButton((AtkUnitBase*)addon, cardButton, requireEnabled: false);
    }

    private static bool TryClickAddonButton(AtkUnitBase* addon, AtkComponentButton* button, bool requireEnabled = true)
    {
        if (button == null || button->AtkResNode == null || !button->AtkResNode->IsVisible())
        {
            return false;
        }

        if (requireEnabled && !button->IsEnabled)
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

    private static bool IsDeckSelectionSettled(AtkUnitBase* addon) =>
        addon == null || !addon->IsVisible || IsTriadBoardVisible();

    private static void DeckSelect()
    {
        try
        {
            var cardFarmActive = IsCardFarmModeActive() && CardFarmHasPendingDrops();
            if (!IsPrepDeckSelectVisible() ||
                (!ShouldContinueTriadSession() && !cardFarmActive))
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

            if (IsTriadBoardVisible())
            {
                deckSelectConfirmedThisScreen = true;
                ClearDeckSelectPending();
                ResetDeckSelectSession();
                return;
            }

            if (deckSelectConfirmedThisScreen)
            {
                if (IsDeckSelectionSettled(addon))
                {
                    return;
                }

                // If the addon never closed after we fired a confirm, fall back to retrying — but only after a generous wait so we don't double-fire on slow UI transitions.
                if (DeckSelectFramesOpen < DeckSelectStuckResetFrames)
                {
                    return;
                }

                deckSelectConfirmedThisScreen = false;
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
                if (IsDeckSelectionSettled(addon))
                {
                    deckSelectConfirmedThisScreen = true;
                    ClearDeckSelectPending();
                    return;
                }

                if (pendingSelectMethod + 1 < GetMaxDeckSelectMethods())
                {
                    pendingSelectMethod++;
                    TryApplyDeckSelection(addon, pendingDeckIndex, pendingSelectMethod);
                    framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;
                    if (IsDeckSelectionSettled(addon))
                    {
                        deckSelectConfirmedThisScreen = true;
                        ClearDeckSelectPending();
                    }

                    return;
                }

                if (IsDeckSelectionSettled(addon))
                {
                    deckSelectConfirmedThisScreen = true;
                    ClearDeckSelectPending();
                    return;
                }

                attemptedDeckIndices.Add(pendingProfileDeckId);
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

            uiReaderPrep.RefreshDeckSelectList((nint)addon);

            // Wait while the deck optimizer is still building the Saucy deck — otherwise we fire the in-game Recommended button before the optimized deck is written.
            if (ShouldAutoManageDeck() && TTSolver.IsDeckSelectPrepBlocking(C.UseRecommendedDeck))
            {
                return;
            }

            if (ShouldAutoManageDeck() && deckSelectAttemptCount == 0 && attemptedDeckIndices.Count == 0)
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

            if (!TTSolver.TryGetDeckSelectCandidate(ShouldAutoManageDeck(), C.SelectedDeckIndex, attemptedDeckIndices,
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

                if (C.UseRecommendedDeck && TryRandomDeckButton(addon))
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

            if (!TTSolver.TryResolveDeckListIndex(deck, out var resolvedListIndex))
            {
                Svc.Chat.PrintError($"[Saucy] Could not find deck {deck + 1} in the selection list.");
                attemptedDeckIndices.Add(deck);
                deckSelectAttemptCount++;
                return;
            }

            PrintDeckSelectAttemptMessage(deck, resolvedListIndex);

            pendingProfileDeckId = deck;
            pendingDeckIndex = resolvedListIndex;
            pendingSelectMethod = 0;
            awaitingDeckSelectConfirm = true;
            TryApplyDeckSelection(addon, resolvedListIndex, 0);
            framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomater] DeckSelect failed");
        }
    }

    private static void TryHideDeckSelectOverlayOnly(AtkUnitBase* addon)
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
            Svc.Log.Verbose(ex, "[TriadAutomater] Direct deck select hide failed");
        }
    }

    private static int forceCloseDeckSelectCooldown;

    private static void TryForceCloseDeckSelectOverlay()
    {
        if (!IsDeckSelectOverlayVisible())
        {
            forceCloseDeckSelectCooldown = 0;
            return;
        }

        if (forceCloseDeckSelectCooldown > 0)
        {
            forceCloseDeckSelectCooldown--;
            return;
        }

        if (!TryGetAddonByName("TripleTriadSelDeck", out AtkUnitBase* addon))
        {
            return;
        }

        forceCloseDeckSelectCooldown = 2;

        if (IsTriadBoardVisible())
        {
            TryHideDeckSelectOverlayOnly(addon);
            ClearDeckSelectPending();
            return;
        }

        // Skip any confirm path once we've already confirmed this screen — otherwise the lingering overlay double-fires deck select (shows up as "(2x)" in chat).
        var deckValue = pendingDeckIndex >= 0 ? pendingDeckIndex : pendingProfileDeckId;
        if (!deckSelectConfirmedThisScreen && deckValue >= 0)
        {
            TryFireDeckCallback(addon, 0, deckValue);
            TryFireDeckCallback(addon, 1, deckValue);
            addon->Update(0);
            if (IsDeckSelectionSettled(addon))
            {
                ClearDeckSelectPending();
                return;
            }

            TryClickDeckConfirmButton(addon);
            addon->Update(0);
            if (IsDeckSelectionSettled(addon))
            {
                ClearDeckSelectPending();
                return;
            }
        }

        TryHideDeckSelectOverlayOnly(addon);
        if (IsDeckSelectionSettled(addon))
        {
            ClearDeckSelectPending();
        }
    }

    private static bool TryBlindDeckSelect(AtkUnitBase* addon)
    {
        PrintTriadDeckLog("[Saucy] Selecting first deck...");
        foreach (var listIndex in new[]
        {
            0, 1, 2, 3, 4
        })
        {
            TryFireDeckCallback(addon, 1, listIndex);
            TryFireDeckCallback(addon, 0, listIndex);
            addon->Update(0);
            if (IsDeckSelectionSettled(addon))
            {
                deckSelectConfirmedThisScreen = true;
                return true;
            }
        }

        // Even if the addon didn't visibly settle in this loop, we've fired multiple confirms — block subsequent fallbacks to avoid (2x).
        deckSelectConfirmedThisScreen = true;
        return false;
    }

    private static void TryFireDeckCallback(AtkUnitBase* addon, int eventId, int deckValue)
    {
        try
        {
            var values = stackalloc AtkValue[1];
            values[0] = new()
            {
                Type = AtkValueType.Int, Int = deckValue
            };
            addon->FireCallback((uint)eventId, values);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadAutomater] Deck callback {0} failed for deck {1}", eventId, deckValue);
        }
    }

    private static int GetMaxDeckSelectMethods() =>
        ShouldAutoManageDeck() ? MaxDeckSelectMethods : ManualDeckSelectMethods;

    private static bool IsDeckSelectSpecialButton(uint buttonId) =>
        Array.IndexOf(DeckSelectRecommendedButtonIds, buttonId) >= 0 ||
        Array.IndexOf(DeckSelectRandomButtonIds, buttonId) >= 0;

    private static bool TryClickDeckSelectButton(AtkUnitBase* addon, uint buttonId)
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
            Svc.Log.Verbose(ex, "[TriadAutomater] Deck select button {0} click failed", buttonId);
            return false;
        }
    }

    private static bool TryClickAnyAddonButton(AtkUnitBase* addon, bool skipRandom = false)
    {
        foreach (var buttonId in new uint[]
        {
            5, 1, 6
        })
        {
            if (skipRandom && Array.IndexOf(DeckSelectRandomButtonIds, buttonId) >= 0)
            {
                continue;
            }

            if (IsDeckSelectSpecialButton(buttonId))
            {
                continue;
            }

            if (TryClickDeckSelectButton(addon, buttonId) && !addon->IsVisible)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySelectVisibleSaucyDeck(AtkUnitBase* addon)
    {
        var expectedName = TTSolver.GetExpectedSaucyDeckName();
        var npcName = TTSolver.preGameNpc?.Name ?? string.Empty;
        for (var idx = 0; idx < uiReaderPrep.cachedState.decks.Count; idx++)
        {
            var deck = uiReaderPrep.cachedState.decks[idx];
            if (string.IsNullOrWhiteSpace(deck.name))
            {
                continue;
            }

            // "(Sa" rather than "(Saucy)" — long NPC names can truncate the tag in the deck row.
            var isSaucyDeck = deck.name.Contains("(Sa", StringComparison.OrdinalIgnoreCase);
            if (!isSaucyDeck)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(expectedName) && !DeckRowMatchesNpc(deck.name, expectedName, npcName))
            {
                continue;
            }

            PrintTriadDeckLog($"[Saucy] Selecting \"{deck.name}\"...");
            pendingProfileDeckId = deck.id;
            pendingDeckIndex = idx;
            pendingSelectMethod = 0;
            awaitingDeckSelectConfirm = true;
            TryApplyDeckSelection(addon, idx, 0);
            return true;
        }

        return false;
    }

    private static bool DeckRowMatchesNpc(string deckRowName, string expectedName, string npcName)
    {
        if (deckRowName.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(npcName) &&
            deckRowName.StartsWith(npcName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var deckBase = StripSaucyTagAndEllipsis(deckRowName);
        if (deckBase.Length < 4)
        {
            return false;
        }

        // Long names get truncated by the game; accept when the deck-row base is a prefix of the NPC name.
        return !string.IsNullOrEmpty(npcName) &&
               npcName.StartsWith(deckBase, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripSaucyTagAndEllipsis(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var tagIdx = name.IndexOf("(Sa", StringComparison.OrdinalIgnoreCase);
        var stripped = tagIdx > 0 ? name.Substring(0, tagIdx) : name;
        return stripped.TrimEnd(' ', '.', '…');
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

        if (IsDeckSelectionSettled(addon))
        {
            deckSelectConfirmedThisScreen = true;
            ClearDeckSelectPending();
        }
    }

    private static void ClearDeckSelectPending()
    {
        pendingDeckIndex = -1;
        pendingProfileDeckId = -1;
        pendingSelectMethod = 0;
        awaitingDeckSelectConfirm = false;
    }

    private static bool TryClickDeckConfirmButton(AtkUnitBase* addon)
    {
        if (TryClickDeckSelectButtonByLabel(addon, "Confirm", "OK", "Bestätigen", "決定"))
        {
            return true;
        }

        foreach (var buttonId in DeckSelectConfirmButtonIds)
        {
            if (TryClickDeckSelectButton(addon, buttonId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryClickDeckSelectButtonByLabel(AtkUnitBase* addon, params string[] labels)
    {
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null)
            {
                continue;
            }

            var button = TryGetButtonFromNode(node);
            if (button == null)
            {
                continue;
            }

            var label = GetResultButtonLabel(button);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            foreach (var token in labels)
            {
                if (label.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return TryClickAddonButton(addon, button);
                }
            }
        }

        return false;
    }

    private static bool TryRecommendedDeckButton(AtkUnitBase* addon)
    {
        PrintTriadDeckLog("[Saucy] Using recommended deck...");
        foreach (var buttonId in DeckSelectRecommendedButtonIds)
        {
            if (TryClickDeckSelectButton(addon, buttonId))
            {
                addon->Update(0);
                // Latch the "we've fired a confirm" flag eagerly — addon close may lag by several frames and the early-exit needs to block the specific-deck fallback in the meantime.
                deckSelectConfirmedThisScreen = true;
                if (IsDeckSelectionSettled(addon))
                {
                    ClearDeckSelectPending();
                }

                return true;
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

        PrintTriadDeckLog("[Saucy] Using random deck...");
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

    private static void PrintDeckSelectAttemptMessage(int deck, int listIndex)
    {
        string message;
        if (deckSelectAttemptCount > 0 || attemptedDeckIndices.Count > 0)
        {
            message = $"[Saucy] Retrying with deck {deck + 1}...";
        }
        else if (C.UseRecommendedDeck && TTSolver.HasOptimizedDeckApplied)
        {
            message = $"[Saucy] Selecting optimized deck {deck + 1}...";
        }
        else
        {
            var deckName = listIndex >= 0 && listIndex < uiReaderPrep.cachedState.decks.Count
                ? uiReaderPrep.cachedState.decks[listIndex].name
                : null;
            message = !string.IsNullOrWhiteSpace(deckName)
                ? $"[Saucy] Selecting \"{deckName}\"..."
                : $"[Saucy] Selecting deck {deck + 1}...";
        }

        if (C.UseRecommendedDeck)
        {
            PrintTriadDeckLog(message);
        }
        else
        {
            PrintManualDeckSelectLog(message);
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
        deckSelectConfirmedThisScreen = false;
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

            // Hold the Challenge click until the deck optimizer is done — otherwise we open deck select before the Saucy deck exists and fall back to the in-game Recommended button.
            if (ShouldAutoManageDeck() && TTSolver.IsDeckSelectPrepBlocking(C.UseRecommendedDeck))
            {
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
                    Svc.Log.Verbose(clickEx, "[TriadAutomater] Challenge button click failed");
                }
            }

            framesSinceMatchAcceptAttempt = MatchAcceptRetryCooldownFrames;

            if (PlayUntilAllCardsDropOnce)
            {
                EnsureCardFarmArmed();
                EnsureRunTargetCards(ResolveRunTargetNpc());
            }

            SnapshotMatchRewardOwnership();
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomater] AcceptTriadMatch failed");
        }
    }

    private static bool TryDismissMatchRegistration()
    {
        if (!TryGetAddonByName<AtkUnitBase>("TripleTriadRequest", out var addon) || !addon->IsVisible)
        {
            return true;
        }

        // Try the Quit button by label first (most reliable across game updates).
        if (TryClickRegistrationButtonByLabel(addon, "Quit", "Beenden", "Quitter", "Abandon", "Decline", "やめる"))
        {
            return !addon->IsVisible;
        }

        // Fallback: common adjacent component IDs (Challenge is 41).
        foreach (var buttonId in new uint[] { 42, 43, 50 })
        {
            var button = addon->GetComponentButtonById(buttonId);
            if (button == null || button->AtkResNode == null || !button->AtkResNode->IsVisible() || !button->IsEnabled)
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
                Svc.Log.Verbose(ex, "[TriadAutomater] Registration button {0} click failed", buttonId);
            }
        }

        // Last resort: addon-level close.
        try
        {
            addon->Close(true);
            addon->Update(0);
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TriadAutomater] Registration Close(true) failed");
        }

        return !addon->IsVisible;
    }

    private static bool TryClickRegistrationButtonByLabel(AtkUnitBase* addon, params string[] labels)
    {
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null)
            {
                continue;
            }

            var button = TryGetButtonFromNode(node);
            if (button == null)
            {
                continue;
            }

            var label = GetResultButtonLabel(button);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            foreach (var token in labels)
            {
                if (label.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return TryClickAddonButton(addon, button);
                }
            }
        }

        return false;
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
