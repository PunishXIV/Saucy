#nullable disable
using Dalamud.Utility;
using ECommons.Automation;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Saucy.TripleTriad.Data;
using Saucy.TripleTriad.GameLogic;
namespace Saucy.TripleTriad.UI;

public class Solver
{
    public delegate void SolveDeckDelegate(SolverResult winChance);

    public enum Status
    {
        NoErrors,
        FailedToParseCards,
        FailedToParseRules,
        FailedToParseNpc
    }

    private readonly object preGameLock = new();

    public ScannerTriad.GameState cachedScreenState;
    public TriadNpc currentNpc;

    // optimizer
    public TriadDeckOptimizer deckOptimizer = new();
    public bool hasMove;

    private byte lastGameMove = byte.MaxValue;
    private ScannerTriad.ETurnState lastTurnState = ScannerTriad.ETurnState.Waiting;
    private bool solveInProgress;

    private int pendingPlaceCardIdx = -1;
    private int pendingPlaceBoardIdx = -1;
    private int placeAttemptCount;
    private int placeRetryCooldown;

    private const int PlaceRetryCooldownFrames = 3;
    private const int MaxPlaceAttempts = 40;

    public TriadNpc lastGameNpc;
    public int moveBoardIdx;
    public int moveCardIdx;
    public SolverResult moveWinChance;
    private bool pauseOptimizerForDeckEval;
    private bool pauseOptimizerForOptimizedEval;
    private bool pauseOptimizerForSolver;

    private const int RecommendedDeckOptimizerTimeoutMs = 25000;
    private static readonly List<TriadCard> UnlockedDeckSlots = [null, null, null, null, null];

    private int optimizerPassId;
    private bool optimizerInProgress;
    private bool optimizerTimedOut;
    private bool optimizerApplied;
    private int optimizerTargetDeckId = -1;
    private string optimizerSessionKey = string.Empty;
    private string lastOptimizerSkipKey = string.Empty;
    private DateTime optimizerStartUtc;
    private Task optimizerTask;

    public int preGameBestId = -1;
    public Dictionary<int, DeckData> preGameDecks = [];
    private int preGameId;
    public List<TriadGameModifier> preGameMods = [];

    public TriadNpc preGameNpc;
    private int preGameSolved;

    public UnsafeReaderProfileGS profileGS;

    public Status status;

    public Solver() => TriadGameSimulation.StaticInitialize();

    public TriadGameScreenMemory DebugScreenMemory { get; } = new();
    public ScannerTriad.GameState DebugScreenState => cachedScreenState;
    public TriadCard MoveCard => DebugScreenMemory.deckBlue?.GetCard(moveCardIdx);
    public float PreGameProgress => (preGameDecks.Count > 0) ? (1.0f * preGameSolved / preGameDecks.Count) : 0.0f;
    public float DeckOptimizerProgress => deckOptimizer.GetProgress() / 100.0f;
    public bool IsRecommendedDeckOptimizerRunning => optimizerInProgress && !optimizerTimedOut && !optimizerApplied;
    public bool HasErrors => status != Status.NoErrors;
    public bool HasAllProfileDecksEmpty { get; private set; }

    public bool IsDeckEvalInProgress
    {
        get
        {
            lock (preGameLock)
            {
                return preGameDecks.Count > 0 && preGameSolved < preGameDecks.Count;
            }
        }
    }

    /// <summary>
    /// Resolves a deck index safe to pass to TripleTriadSelDeck.
    /// When <paramref name="useRecommended"/> is true, returns false while deck evaluation is still running.
    /// </summary>
    public bool TryResolveDeckIndex(bool useRecommended, int manualDeckIndex, out int deckIndex)
    {
        deckIndex = -1;
        if (IsDeckSelectPrepBlocking(useRecommended))
        {
            return false;
        }

        lock (preGameLock)
        {
            var preferred = useRecommended ? preGameBestId : manualDeckIndex;
            var fallback = useRecommended ? manualDeckIndex : preGameBestId;
            deckIndex = ResolveSelectableDeckIndexLocked(preferred, fallback);
        }

        return true;
    }

    /// <summary>
    /// Picks the next deck index to try on the selection screen, skipping <paramref name="excluded"/>.
    /// Returns false while recommended-deck evaluation is still running.
    /// </summary>
    private const int DeckSelectPrepWaitFrames = 90;

    public bool IsDeckSelectPrepBlocking(bool useRecommended)
    {
        if (!useRecommended || TriadAutomater.DeckSelectFramesOpen >= DeckSelectPrepWaitFrames)
        {
            return false;
        }

        lock (preGameLock)
        {
            if (IsRecommendedDeckOptimizerBlockingLocked())
            {
                return true;
            }

            return !optimizerApplied && preGameDecks.Count > 0 && preGameSolved < preGameDecks.Count;
        }
    }

    public bool TryGetDeckSelectCandidate(bool useRecommended, int manualDeckIndex, HashSet<int> excluded, out int deckIndex)
    {
        deckIndex = -1;
        if (IsDeckSelectPrepBlocking(useRecommended))
        {
            return false;
        }

        lock (preGameLock)
        {

            foreach (var candidate in GetOrderedDeckCandidatesLocked(useRecommended, manualDeckIndex))
            {
                if (excluded is { Count: > 0 } && excluded.Contains(candidate))
                {
                    continue;
                }

                if (IsDeckSelectableLocked(candidate))
                {
                    deckIndex = candidate;
                    return true;
                }
            }
        }

        return true;
    }

    public event Action<bool> OnMoveChanged;

    /// <summary>
    /// Optional in-game debug lines via /echo. Off by default.
    /// </summary>
    public const bool DebugTriadMoves = false;

    public void UpdateGame(UIStateTriadGame stateOb) => UpdateGame(stateOb, false);

    public void UpdateGame(UIStateTriadGame stateOb, bool automationTick)
    {
        status = Status.NoErrors;

        if (stateOb != null && HasPendingPlacement())
        {
            ProcessPendingPlacement(stateOb);
            if (HasPendingPlacement())
            {
                return;
            }
        }

        ScannerTriad.GameState screenOb = null;
        if (stateOb != null)
        {
            var parseCtx = new GameUIParser();
            screenOb = stateOb.ToTriadScreenState(parseCtx, markFailed: false);
            currentNpc = stateOb.ToTriadNpc(parseCtx);
            EnsureScreenMods(screenOb);

            if (parseCtx.HasErrors)
            {
                currentNpc = null;
                status =
                    parseCtx.hasFailedCard ? Status.FailedToParseCards :
                    parseCtx.hasFailedModifier ? Status.FailedToParseRules :
                    parseCtx.hasFailedNpc ? Status.FailedToParseNpc :
                    Status.NoErrors;
            }
        }
        else
        {
            // not really an error state, ui reader will push null state when game is finished
            currentNpc = null;
            ResetPendingPlacement();
        }

        if (currentNpc != null)
        {
            lastGameNpc = currentNpc;
        }

        cachedScreenState = screenOb;

        var npcForGame = ResolveNpcForGame(stateOb);
        if (npcForGame != null && screenOb != null && stateOb != null)
        {
            var updateFlags = DebugScreenMemory.OnNewScan(screenOb, npcForGame);
            var moveChanged = stateOb.move != lastGameMove;
            lastGameMove = stateOb.move;
            var turnBecameActive = screenOb.turnState == ScannerTriad.ETurnState.Active &&
                                   lastTurnState != ScannerTriad.ETurnState.Active;
            lastTurnState = screenOb.turnState;

            if (screenOb.turnState == ScannerTriad.ETurnState.Active)
            {
                var needsFreshMove = !HasValidMove();
                if ((automationTick && !HasValidMove()) ||
                    updateFlags != TriadGameScreenMemory.EUpdateFlags.None || moveChanged ||
                    turnBecameActive || needsFreshMove)
                {
                    if (!solveInProgress)
                    {
                        ApplySolverMove(npcForGame, updateFlags, moveChanged, turnBecameActive);
                    }
                }
            }
            else if (hasMove)
            {
                ClearMove();
            }
        }
        else
        {
            if (stateOb == null)
            {
                lastGameMove = byte.MaxValue;
                lastTurnState = ScannerTriad.ETurnState.Waiting;
            }

            if (hasMove)
            {
                ClearMove();
            }
        }
    }

    /// <summary>
    /// Called from <see cref="TriadAutomater.RunModule"/> when the UI reader has not fired
    /// (unchanged Equals state) but the player can act on the TripleTriad addon.
    /// </summary>
    public void EnsurePlayerMove(UIStateTriadGame? stateOb)
    {
        if (stateOb == null || solveInProgress)
        {
            return;
        }

        if (HasPendingPlacement())
        {
            ProcessPendingPlacement(stateOb);
            if (HasPendingPlacement())
            {
                return;
            }
        }

        if (HasValidMove())
        {
            return;
        }

        if (stateOb.move == 0)
        {
            return;
        }

        UpdateGame(stateOb);
    }

    public void TickPlaceRetryCooldown()
    {
        if (placeRetryCooldown > 0)
        {
            placeRetryCooldown--;
        }
    }

    public bool ShouldAttemptPlace()
    {
        if (!hasMove || moveCardIdx < 0 || moveBoardIdx < 0)
        {
            return false;
        }

        if (placeRetryCooldown > 0)
        {
            return false;
        }

        placeRetryCooldown = PlaceRetryCooldownFrames;
        return true;
    }

    public void RecordPlaceAttempt()
    {
        pendingPlaceCardIdx = moveCardIdx;
        pendingPlaceBoardIdx = moveBoardIdx;
        placeAttemptCount++;
    }

    public bool HasPendingPlacement() => pendingPlaceBoardIdx >= 0;

    private void ProcessPendingPlacement(UIStateTriadGame stateOb)
    {
        if (pendingPlaceBoardIdx < 0)
        {
            return;
        }

        if (IsPlacementConfirmed(stateOb))
        {
            ResetPendingPlacement();
            ClearMove();
            return;
        }

        moveCardIdx = pendingPlaceCardIdx;
        moveBoardIdx = pendingPlaceBoardIdx;
        hasMove = moveCardIdx >= 0 && moveBoardIdx >= 0;

        if (placeAttemptCount >= MaxPlaceAttempts)
        {
            ResetPendingPlacement();
            ClearMove();
        }
    }

    private bool IsPlacementConfirmed(UIStateTriadGame stateOb)
    {
        if (stateOb.move == 0)
        {
            return true;
        }

        if (pendingPlaceBoardIdx >= 0 &&
            pendingPlaceBoardIdx < stateOb.board.Length &&
            stateOb.board[pendingPlaceBoardIdx].isPresent)
        {
            return true;
        }

        if (pendingPlaceCardIdx >= 0 &&
            pendingPlaceCardIdx < stateOb.blueDeck.Length &&
            !stateOb.blueDeck[pendingPlaceCardIdx].isPresent)
        {
            return true;
        }

        return false;
    }

    private void ResetPendingPlacement()
    {
        pendingPlaceCardIdx = -1;
        pendingPlaceBoardIdx = -1;
        placeAttemptCount = 0;
        placeRetryCooldown = 0;
    }

    private TriadNpc? ResolveNpcForGame(UIStateTriadGame? stateOb)
    {
        if (currentNpc != null)
        {
            lastGameNpc = currentNpc;
            return currentNpc;
        }

        if (lastGameNpc != null)
        {
            return lastGameNpc;
        }

        if (stateOb != null)
        {
            var ctx = new GameUIParser();
            var fromGame = stateOb.ToTriadNpc(ctx);
            if (fromGame != null)
            {
                lastGameNpc = fromGame;
                currentNpc = fromGame;
                return fromGame;
            }
        }

        if (!string.IsNullOrEmpty(uiReaderPrep.cachedState.npc))
        {
            var ctx = new GameUIParser();
            var fromPrep = ctx.ParseNpc(uiReaderPrep.cachedState.npc, markFailed: false) ??
                           ctx.ParseNpcNameStart(uiReaderPrep.cachedState.npc, markFailed: false);
            if (fromPrep != null)
            {
                lastGameNpc = fromPrep;
                preGameNpc ??= fromPrep;
                return fromPrep;
            }
        }

        return null;
    }

    private void TryApplyFallbackMove()
    {
        if (DebugScreenMemory.gameState == null || DebugScreenMemory.gameSolver == null)
        {
            return;
        }

        DebugScreenMemory.gameSolver.FindAvailableActions(
            DebugScreenMemory.gameState,
            out var availBoardMask,
            out var numAvailBoard,
            out var availCardsMask,
            out var numAvailCards);

        if (numAvailCards <= 0 || numAvailBoard <= 0)
        {
            return;
        }

        moveCardIdx = TriadGameAgentRandom.PickRandomBitFromMask(availCardsMask, 0);
        moveBoardIdx = TriadGameAgentRandom.PickRandomBitFromMask(availBoardMask, 0);
        hasMove = moveCardIdx >= 0 && moveBoardIdx >= 0;
        moveWinChance = SolverResult.Zero;
    }

    private void EnsureScreenMods(ScannerTriad.GameState? screenOb)
    {
        if (screenOb == null || screenOb.mods.Count > 0)
        {
            return;
        }

        if (preGameMods.Count > 0)
        {
            screenOb.mods.AddRange(preGameMods);
            return;
        }

        var npc = currentNpc ?? lastGameNpc;
        if (npc?.Rules != null)
        {
            screenOb.mods.AddRange(npc.Rules);
        }
    }

    public void ClearMoveAfterPlace()
    {
        ResetPendingPlacement();
        ClearMove();
    }

    private bool HasValidMove() => hasMove && moveCardIdx >= 0 && moveBoardIdx >= 0;

    private void ClearMove()
    {
        hasMove = false;
        moveCardIdx = -1;
        moveBoardIdx = -1;
        ResetPendingPlacement();
        OnMoveChanged?.Invoke(false);
    }

    private void ApplySolverMove(TriadNpc _, TriadGameScreenMemory.EUpdateFlags updateFlags, bool moveChanged, bool turnBecameActive)
    {
        if (DebugScreenMemory.deckBlue == null || DebugScreenMemory.gameState == null || DebugScreenMemory.gameSolver == null)
        {
            if (DebugTriadMoves)
            {
                Svc.Chat.Print("[Saucy] Triad: screen memory not ready.");
            }

            ClearMove();
            return;
        }

#if DEBUG
        DebugScreenMemory.gameSolver.agent.debugFlags = TriadGameAgent.DebugFlags.ShowMoveStart | TriadGameAgent.DebugFlags.ShowMoveDetails;
#endif

        solveInProgress = true;
        pauseOptimizerForSolver = true;
        UpdateDeckOptimizerPause();

        try
        {
            DebugScreenMemory.gameSolver.FindNextMove(DebugScreenMemory.gameState, out var bestCardIdx, out var bestBoardPos, out var solverResult);

            moveCardIdx = bestCardIdx;
            moveBoardIdx = (moveCardIdx < 0) ? -1 : bestBoardPos;
            moveWinChance = solverResult;

            if ((DebugScreenMemory.gameState.forcedCardIdx >= 0) && (moveCardIdx != DebugScreenMemory.gameState.forcedCardIdx))
            {
                var forcedCardOb = DebugScreenMemory.deckBlue.GetCard(DebugScreenMemory.gameState.forcedCardIdx);
                var solverCardOb = DebugScreenMemory.deckBlue.GetCard(moveCardIdx);
                var solverCardDesc = solverCardOb != null ? solverCardOb.Name : "??";
                var forcedCardDesc = forcedCardOb != null ? forcedCardOb.Name : "??";
                PluginLog.Warning($"Solver selected card [{moveCardIdx}]:{solverCardDesc}, but game wants: [{DebugScreenMemory.gameState.forcedCardIdx}]:{forcedCardDesc} !");
                moveCardIdx = DebugScreenMemory.gameState.forcedCardIdx;
            }

            hasMove = moveCardIdx >= 0 && moveBoardIdx >= 0;

            if (!hasMove)
            {
                TryApplyFallbackMove();
            }

            if (DebugTriadMoves)
            {
                var cardName = DebugScreenMemory.deckBlue.GetCard(moveCardIdx)?.Name ?? "??";
                Svc.Chat.Print(hasMove
                    ? $"[Saucy] Triad move: card {moveCardIdx} ({cardName}) -> board {moveBoardIdx}"
                    : $"[Saucy] Triad: solver found no legal move (flags:{updateFlags}, moveChg:{moveChanged}, turnOn:{turnBecameActive}).");
            }

            if (hasMove)
            {
                var solverCardOb = DebugScreenMemory.deckBlue.GetCard(moveCardIdx);
                Logger.WriteLine("  suggested move: [{0}] {1} {2} (expected: {3})",
                    moveBoardIdx, ETriadCardOwner.Blue,
                    solverCardOb != null ? solverCardOb.Name : "??",
                    moveWinChance.expectedResult);
            }
        }
        finally
        {
            solveInProgress = false;
            pauseOptimizerForSolver = false;
            UpdateDeckOptimizerPause();
        }

        OnMoveChanged?.Invoke(hasMove);
    }

    public (List<TriadCard>, List<TriadCard>) GetScreenRedDeckDebug()
    {
        var knownCards = new List<TriadCard>();
        var unknownCards = new List<TriadCard>();

        if (DebugScreenMemory != null && DebugScreenMemory.deckRed != null && DebugScreenMemory.deckRed.deck != null)
        {
            var deckInst = DebugScreenMemory.deckRed;
            if (deckInst.availableCardMask > 0)
            {
                for (var Idx = 0; Idx < deckInst.cards.Length; Idx++)
                {
                    var bIsAvailable = (deckInst.availableCardMask & (1 << Idx)) != 0;
                    if (bIsAvailable)
                    {
                        var cardOb = deckInst.GetCard(Idx);
                        var bIsKnownPool = deckInst.deck.knownCards.Contains(cardOb);

                        var listToUse = bIsKnownPool ? knownCards : unknownCards;
                        listToUse.Add(cardOb);
                    }
                }
            }

            var visibleCardsMask = (deckInst.cards != null) ? ((1 << deckInst.cards.Length) - 1) : 0;
            var hasHiddenCards = (deckInst.availableCardMask & ~visibleCardsMask) != 0;
            if (hasHiddenCards)
            {
                for (var Idx = deckInst.cards.Length; Idx < 15; Idx++)
                {
                    var bIsAvailable = (deckInst.availableCardMask & (1 << Idx)) != 0;
                    if (bIsAvailable)
                    {
                        var cardOb = deckInst.GetCard(Idx);
                        var bIsKnownPool = (deckInst.unknownPoolMask & (1 << Idx)) == 0;

                        var listToUse = bIsKnownPool ? knownCards : unknownCards;
                        listToUse.Add(cardOb);
                    }
                }
            }
        }

        return (knownCards, unknownCards);
    }

    public void OnNpcSelected(TriadNpc npc) => OnNpcSelected(npc, []);

    public void OnNpcSelected(TriadNpc npc, List<TriadGameModifier> regionMods)
    {
        if (npc == null)
        {
            return;
        }

        preGameNpc = npc;
        preGameMods = regionMods ?? [];
        lastGameNpc = npc;
        ApplyRunTargetNpc(npc);
    }

    private int lastAppliedRunTargetNpcId = -1;

    public void ResetRunTargetNpcSession() => lastAppliedRunTargetNpcId = -1;

    public void EnsureRunTargetNpcSynced()
    {
        uiReaderGame.RefreshFromVisibleAddon();

        if (uiReaderGame.currentState != null)
        {
            var npc = ResolveNpcForGame(uiReaderGame.currentState);
            if (npc != null)
            {
                ApplyRunTargetNpc(npc);
                return;
            }
        }

        if (preGameNpc != null)
        {
            return;
        }

        if (TrySyncNpcFromPrepState(uiReaderPrep.cachedState))
        {
            ApplyRunTargetNpc(preGameNpc!);
        }
    }

    private void ApplyRunTargetNpc(TriadNpc npc)
    {
        if (npc == null)
        {
            return;
        }

        var changed = lastAppliedRunTargetNpcId != npc.Id;
        preGameNpc = npc;
        lastGameNpc = npc;
        lastAppliedRunTargetNpcId = npc.Id;

        if (GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out var npcInfo))
        {
            TriadAutomater.EnsureRunTargetCards(npcInfo);
        }

        if (changed && C.UseRecommendedDeck)
        {
            StartRecommendedDeckOptimizer(npc, preGameMods);
        }
    }

    public void EnsurePrepNpcSynced() => EnsureRunTargetNpcSynced();

    private bool TrySyncNpcFromPrepState(UIStateTriadPrep state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.npc))
        {
            return false;
        }

        var parseCtx = new GameUIParser();
        var npc = parseCtx.ParseNpc(state.npc, false) ?? parseCtx.ParseNpcNameStart(state.npc, false);
        if (npc == null)
        {
            return false;
        }

        preGameNpc = npc;
        preGameMods = ParsePrepRegionMods(state, parseCtx);
        lastGameNpc = npc;
        return true;
    }

    public void OnMatchPrepDetected(UIStateTriadPrep state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.npc))
        {
            return;
        }

        if (!TrySyncNpcFromPrepState(state))
        {
            var parseCtx = new GameUIParser();
            parseCtx.OnFailedNpcSilent(state.npc);
            return;
        }

        ApplyRunTargetNpc(preGameNpc!);
    }

    private static List<TriadGameModifier> ParsePrepRegionMods(UIStateTriadPrep state, GameUIParser parseCtx)
    {
        var mods = new List<TriadGameModifier>();
        foreach (var rule in state.rules)
        {
            var ruleOb = parseCtx.ParseModifier(rule, false);
            if (ruleOb is not null and not TriadGameModifierNone)
            {
                mods.Add(ruleOb);
            }
        }

        return mods;
    }

    public void UpdateDecks(UIStateTriadPrep state)
    {
        // don't report status here, just log stuff out
        var parseCtx = new GameUIParser();

        var newPreGameNpc = string.IsNullOrWhiteSpace(state.npc)
            ? preGameNpc
            : parseCtx.ParseNpc(state.npc, false) ?? parseCtx.ParseNpcNameStart(state.npc, false);
        if (newPreGameNpc == null && !string.IsNullOrWhiteSpace(state.npc))
        {
            parseCtx.OnFailedNpcSilent(state.npc);
        }

        var newPreGameMods = ParsePrepRegionMods(state, parseCtx);

        lastGameNpc = newPreGameNpc ?? preGameNpc;

        var canReadFromProfile = profileGS != null && !profileGS.HasErrors;
        var canProcessDecks = !parseCtx.HasErrors &&
                              (canReadFromProfile || (state.decks.Count > 0 && !canReadFromProfile));

        if (canProcessDecks)
        {
            var profileDecks = canReadFromProfile ? profileGS.GetPlayerDecks() : null;
            var numDecks = (profileDecks != null) ? profileDecks.Length : state.decks.Count;
            var newPreGameDecks = new Dictionary<int, DeckData>();

            TriadDeck anyDeckOb = null;
            for (var deckIdx = 0; deckIdx < numDecks; deckIdx++)
            {
                parseCtx.Reset();

                var deckData = (profileDecks != null)
                    ? ParseDeckDataFromProfile(profileDecks[deckIdx], parseCtx)
                    : ParseDeckDataFromUI(state.decks[deckIdx], parseCtx);

                if (!parseCtx.HasErrors && deckData != null)
                {
                    newPreGameDecks.Add(deckData.id, deckData);
                    anyDeckOb = deckData.solverDeck;
                }
            }

            // check if actually have something to do
            var needsDeckEval = IsDeckEvalDataChanged(newPreGameNpc, newPreGameMods, newPreGameDecks);
            if (!needsDeckEval)
            {
                Logger.WriteLine("ignore deck eval, same input data");
                if (C.UseRecommendedDeck && newPreGameNpc != null && !parseCtx.HasErrors)
                {
                    StartRecommendedDeckOptimizer(newPreGameNpc, newPreGameMods);
                }

                return;
            }

            preGameNpc = newPreGameNpc;
            preGameMods = newPreGameMods;
            preGameDecks = newPreGameDecks;

            // bump pass id, pending workers from previous update won't try to write their results
            preGameId++;
            preGameBestId = -1;
            preGameSolved = 0;

            // initialize screenMemory.playerDeck, see comment in OnSolvedDeck() for details
            HasAllProfileDecksEmpty = (profileDecks != null) && (anyDeckOb == null);
            anyDeckOb ??= new(PlayerSettingsDB.Get().starterCards);
            DebugScreenMemory.UpdatePlayerDeck(anyDeckOb);

            foreach (var kvp in preGameDecks)
            {
                var deckSolver = new TriadGameSolver();
                deckSolver.InitializeSimulation(preGameMods);

                var gameState = deckSolver.StartSimulation(kvp.Value.solverDeck, preGameNpc.Deck, ETriadGameState.InProgressBlue);
                var calcContext = new DeckSolverContext
                {
                    solver = deckSolver, gameState = gameState, deckId = kvp.Value.id, passId = preGameId
                };

                void solverAction(object ctxOb)
                {
                    var ctx = ctxOb as DeckSolverContext;
                    ctx.solver.FindNextMove(ctx.gameState, out var _, out var _, out var solverResult);
                    OnSolvedDeck(ctx.passId, ctx.deckId, solverResult);
                }

                new TaskFactory().StartNew(solverAction, calcContext);
            }

            pauseOptimizerForDeckEval = preGameDecks.Count > 0;
            UpdateDeckOptimizerPause();

            if (C.UseRecommendedDeck && newPreGameNpc != null && !parseCtx.HasErrors)
            {
                StartRecommendedDeckOptimizer(newPreGameNpc, newPreGameMods);
            }
            else
            {
                ResetRecommendedDeckOptimizer();
            }
        }
        else if (!C.UseRecommendedDeck)
        {
            ResetRecommendedDeckOptimizer();
        }
    }

    private void ResetRecommendedDeckOptimizer()
    {
        if (!optimizerInProgress && !optimizerApplied)
        {
            return;
        }

        deckOptimizer.AbortProcess();
        optimizerPassId++;
        optimizerInProgress = false;
        optimizerTimedOut = false;
        optimizerApplied = false;
        optimizerTargetDeckId = -1;
        optimizerSessionKey = string.Empty;
    }

    private bool IsDeckEvalDataChanged(TriadNpc testNpc, List<TriadGameModifier> testMods, Dictionary<int, DeckData> testDecks)
    {
        if (testNpc != preGameNpc)
        {
            return true;
        }

        if (testMods.Count != preGameMods.Count)
        {
            return true;
        }

        for (var idx = 0; idx < testMods.Count; idx++)
        {
            if (testMods[idx] != preGameMods[idx])
            {
                return true;
            }
        }

        if (testDecks.Count != preGameDecks.Count)
        {
            return true;
        }

        foreach (var kvp in testDecks)
        {
            if (!preGameDecks.TryGetValue(kvp.Key, out var deckData))
            {
                return true;
            }

            if (!deckData.solverDeck.Equals(kvp.Value.solverDeck))
            {
                return true;
            }
        }

        return false;
    }

    private DeckData ParseDeckDataFromProfile(UnsafeReaderProfileGS.PlayerDeck deckOb, GameUIParser ctx)
    {
        // empty profile decks will result in nulls here
        if (deckOb == null)
        {
            return null;
        }

        var deckData = new DeckData
        {
            id = deckOb.id, name = deckOb.name
        };

        var cards = new TriadCard[5];
        for (var cardIdx = 0; cardIdx < 5; cardIdx++)
        {
            int cardId = deckOb.cardIds[cardIdx];
            cards[cardIdx] = ctx.cards.FindById(cardId);

            if (cards[cardIdx] == null)
            {
                ctx.OnFailedCard($"id:{cardId}");
            }
        }

        deckData.solverDeck = ctx.HasErrors ? null : new TriadDeck(cards);
        return deckData;
    }

    private DeckData ParseDeckDataFromUI(UIStateTriadPrepDeck deckOb, GameUIParser ctx)
    {
        // empty UI decks are valid objects, but their card data is empty (handled by ctx)
        // do quick filter pass looking for nulls in card slots too

        var numValidCards = 0;
        for (var cardIdx = 0; cardIdx < 5; cardIdx++)
        {
            numValidCards += string.IsNullOrEmpty(deckOb.cardTexPaths[cardIdx]) ? 0 : 1;
        }

        DeckData deckData = null;
        if (numValidCards == 5)
        {
            deckData = new()
            {
                id = deckOb.id, name = deckOb.name
            };

            var cards = new TriadCard[5];
            for (var cardIdx = 0; cardIdx < 5; cardIdx++)
            {
                cards[cardIdx] = ctx.ParseCard(deckOb.cardTexPaths[cardIdx]);
            }

            deckData.solverDeck = ctx.HasErrors ? null : new TriadDeck(cards);
        }

        return deckData;
    }

    private void OnSolvedDeck(int passId, int deckId, SolverResult winChance)
    {
        if (preGameId != passId)
        {
            return;
        }

        lock (preGameLock)
        {
            if (preGameDecks.TryGetValue(deckId, out var deckData))
            {
                deckData.chance = winChance;
                preGameSolved++;

                // TODO: broadcast? (this is still worker thread!)
                Logger.WriteLine($"deck[{deckId}]:'{deckData.name}', {winChance}");

                float bestScore = 0;
                var bestId = -1;
                foreach (var kvp in preGameDecks)
                {
                    var testScore = kvp.Value.chance.score;
                    if (bestId < 0 || testScore > bestScore)
                    {
                        bestId = kvp.Key;
                        bestScore = testScore;
                    }
                }

                // screenMemory.PlayerDeck - originally used for determining swapped cards
                // there's probably much better way of doing that and it needs further work
                // for now, just pretend that best scoring deck is the one that player will be using
                // - yes, player used that one in game - yay, swap detection works correctly
                // - nope, player picked something else - whatever, build in failsafes in swap detection will handle that after 3-4 matches
                if (bestId >= 0 && bestId != preGameBestId)
                {
                    if (preGameDecks.TryGetValue(bestId, out var bestDeckData))
                    {
                        DebugScreenMemory.UpdatePlayerDeck(bestDeckData.solverDeck);
                    }
                }

                preGameBestId = bestId;

                pauseOptimizerForDeckEval = (preGameSolved < preGameDecks.Count);
                UpdateDeckOptimizerPause();
            }
        }
    }

    public void SolveOptimizedDeck(TriadDeck deck, TriadNpc npc, List<TriadGameModifier> regionMods, SolveDeckDelegate callback)
    {
        if (npc == null || deck == null)
        {
            return;
        }

        var deckSolver = new TriadGameSolver();
        deckSolver.InitializeSimulation(npc.Rules, regionMods);

        var gameState = deckSolver.StartSimulation(deck, npc.Deck, ETriadGameState.InProgressBlue);
        var calcContext = new DeckSolverContext
        {
            solver = deckSolver, gameState = gameState, callback = callback
        };

        pauseOptimizerForOptimizedEval = true;
        UpdateDeckOptimizerPause();

        void solverAction(object ctxOb)
        {
            var ctx = ctxOb as DeckSolverContext;
            ctx.solver.FindNextMove(ctx.gameState, out var _, out var _, out var solverResult);
            callback?.Invoke(solverResult);

            pauseOptimizerForOptimizedEval = false;
            UpdateDeckOptimizerPause();
        }

        new TaskFactory().StartNew(solverAction, calcContext);
    }

    private void UpdateDeckOptimizerPause() => deckOptimizer.SetPaused(pauseOptimizerForSolver || pauseOptimizerForDeckEval || pauseOptimizerForOptimizedEval);

    private bool IsRecommendedDeckOptimizerBlockingLocked()
    {
        if (!optimizerInProgress || optimizerApplied)
        {
            return false;
        }

        if (optimizerTimedOut)
        {
            return false;
        }

        var elapsedMs = (DateTime.UtcNow - optimizerStartUtc).TotalMilliseconds;
        if (elapsedMs < RecommendedDeckOptimizerTimeoutMs)
        {
            return true;
        }

        optimizerTimedOut = true;
        optimizerInProgress = false;
        optimizerPassId++;
        deckOptimizer.AbortProcess();
        PrintOptimizerChat(
            $"[Saucy] Deck optimizer timed out after {RecommendedDeckOptimizerTimeoutMs / 1000}s; using profile deck ranking.");
        return false;
    }

    private static void PrintOptimizerChat(string message)
    {
        Svc.Log.Info(message);
        Svc.Framework.Run(() => Svc.Chat.Print(message));
    }

    private void AnnounceOptimizerSkipOnce(string skipKey, string message)
    {
        Svc.Log.Info(message);
        if (lastOptimizerSkipKey == skipKey)
        {
            return;
        }

        lastOptimizerSkipKey = skipKey;
        PrintOptimizerChat(message);
    }

    private void StartRecommendedDeckOptimizer(TriadNpc npc, List<TriadGameModifier> regionMods)
    {
        if (npc == null)
        {
            return;
        }

        var optimizerKey = BuildOptimizerSessionKey(npc, regionMods);

        if (optimizerInProgress && optimizerKey == optimizerSessionKey)
        {
            AnnounceOptimizerSkipOnce($"{optimizerKey}:running", $"[Saucy] Still optimizing deck for {npc.Name}...");
            return;
        }

        if (profileGS == null || profileGS.HasErrors)
        {
            AnnounceOptimizerSkipOnce($"{optimizerKey}:profile",
                $"[Saucy] Profile reader unavailable; optimizing deck for {npc.Name} (cannot save to profile).");
        }

        GameCardDB.Get().Refresh();

        if (PlayerSettingsDB.Get().ownedCards.Count == 0)
        {
            AnnounceOptimizerSkipOnce($"{optimizerKey}:no_cards",
                "[Saucy] Deck optimizer skipped: no owned cards in collection cache.");
            return;
        }

        lastOptimizerSkipKey = string.Empty;

        optimizerPassId++;
        var passId = optimizerPassId;
        optimizerInProgress = true;
        optimizerTimedOut = false;
        optimizerApplied = false;
        optimizerTargetDeckId = -1;
        optimizerSessionKey = optimizerKey;
        optimizerStartUtc = DateTime.UtcNow;

        PrintOptimizerChat($"[Saucy] Optimizing deck for {npc.Name}...");
        deckOptimizer.AbortProcess();
        var regionModsForOptimizer = BuildRegionModsForOptimizer(npc, regionMods);
        deckOptimizer.Initialize(npc, regionModsForOptimizer, UnlockedDeckSlots);

        optimizerTask = deckOptimizer.Process(npc, regionModsForOptimizer, UnlockedDeckSlots)
            .ContinueWith(_ => OnRecommendedDeckOptimizerFinished(passId), TaskScheduler.Default);
    }

    private static string BuildOptimizerSessionKey(TriadNpc npc, List<TriadGameModifier> regionMods)
    {
        if (npc == null)
        {
            return string.Empty;
        }

        var key = npc.Id.ToString();
        foreach (var mod in regionMods)
        {
            key += ":" + mod.GetLocalizationId();
        }

        return key;
    }

    private static TriadGameModifier[] BuildRegionModsForOptimizer(TriadNpc npc, List<TriadGameModifier> regionMods)
    {
        var result = new List<TriadGameModifier>();
        var removedNpcMod = new bool[2];

        foreach (var mod in regionMods)
        {
            if (mod == null)
            {
                continue;
            }

            var npcModIdx = npc.Rules.FindIndex(x => x.GetLocalizationId() == mod.GetLocalizationId());
            if (npcModIdx >= 0 && npcModIdx < removedNpcMod.Length && !removedNpcMod[npcModIdx])
            {
                removedNpcMod[npcModIdx] = true;
                continue;
            }

            var modOb = mod.Clone();
            if (modOb != null)
            {
                result.Add(modOb);
            }
        }

        return result.ToArray();
    }

    private void OnRecommendedDeckOptimizerFinished(int passId)
    {
        if (passId != optimizerPassId)
        {
            return;
        }

        lock (preGameLock)
        {
            if (passId != optimizerPassId)
            {
                return;
            }

            optimizerInProgress = false;

            if (optimizerTimedOut)
            {
                return;
            }

            if (deckOptimizer.IsAborted())
            {
                PrintOptimizerChat("[Saucy] Deck optimizer aborted; using profile deck ranking.");
                return;
            }

            var optimizedDeck = deckOptimizer.optimizedDeck;
            if (optimizedDeck == null || optimizedDeck.GetDeckState() != ETriadDeckState.Valid)
            {
                deckOptimizer.GuessDeck(UnlockedDeckSlots);
                optimizedDeck = deckOptimizer.optimizedDeck;
            }

            if (optimizedDeck == null || optimizedDeck.GetDeckState() != ETriadDeckState.Valid)
            {
                PrintOptimizerChat("[Saucy] Deck optimizer produced no valid deck; using profile deck ranking.");
                return;
            }

            ApplyOptimizedDeckToProfileLocked(optimizedDeck);
        }
    }

    private void ApplyOptimizedDeckToProfileLocked(TriadDeck optimizedDeck)
    {
        if (profileGS == null || profileGS.HasErrors)
        {
            PrintOptimizerChat("[Saucy] Optimized deck ready but profile is unavailable; cannot save to profile.");
            return;
        }

        var targetDeckId = ResolveOptimizedDeckTargetSlotLocked();
        if (targetDeckId < 0)
        {
            PrintOptimizerChat("[Saucy] Deck optimizer could not find a profile deck slot to write.");
            return;
        }

        var cardIds = new ushort[5];
        for (var idx = 0; idx < 5; idx++)
        {
            var card = optimizedDeck.knownCards[idx];
            if (card == null || card.Id <= 0)
            {
                PrintOptimizerChat($"[Saucy] Deck optimizer deck has invalid card at slot {idx + 1}.");
                return;
            }

            cardIds[idx] = (ushort)card.Id;
        }

        if (!profileGS.TryWritePlayerDeck(targetDeckId, cardIds))
        {
            PrintOptimizerChat($"[Saucy] Failed to write optimized deck to profile slot {targetDeckId + 1}.");
            return;
        }

        optimizerTargetDeckId = targetDeckId;
        optimizerApplied = true;
        preGameBestId = targetDeckId;

        var deckData = new DeckData
        {
            id = targetDeckId,
            name = $"Optimized ({preGameNpc?.Name ?? "NPC"})",
            solverDeck = optimizedDeck
        };

        preGameDecks[targetDeckId] = deckData;
        if (preGameDecks.Count > 0)
        {
            preGameSolved = preGameDecks.Count;
        }

        DebugScreenMemory.UpdatePlayerDeck(optimizedDeck);

        PrintOptimizerChat(
            $"[Saucy] Optimized deck written to slot {targetDeckId + 1} for {preGameNpc?.Name ?? "match"}.");
    }

    private int ResolveOptimizedDeckTargetSlotLocked()
    {
        var manualDeck = C.SelectedDeckIndex;
        if (manualDeck >= 0 && manualDeck <= 4)
        {
            return manualDeck;
        }

        return GetFirstProfileDeckIndex() >= 0 ? GetFirstProfileDeckIndex() : 0;
    }

    private int ResolveSelectableDeckIndexLocked(int preferredDeckId, int fallbackDeckId)
    {
        foreach (var candidate in GetOrderedDeckCandidatesLocked(preferredDeckId, fallbackDeckId))
        {
            if (IsDeckSelectableLocked(candidate))
            {
                return candidate;
            }
        }

        return -1;
    }

    private IEnumerable<int> GetOrderedDeckCandidatesLocked(bool useRecommended, int manualDeckIndex)
    {
        var preferred = useRecommended ? preGameBestId : manualDeckIndex;
        var fallback = useRecommended ? manualDeckIndex : preGameBestId;
        return GetOrderedDeckCandidatesLocked(preferred, fallback);
    }

    private IEnumerable<int> GetOrderedDeckCandidatesLocked(int preferredDeckId, int fallbackDeckId)
    {
        if (preferredDeckId >= 0)
        {
            yield return preferredDeckId;
        }

        if (fallbackDeckId >= 0 && fallbackDeckId != preferredDeckId)
        {
            yield return fallbackDeckId;
        }

        var rankedDecks = new List<KeyValuePair<int, DeckData>>();
        foreach (var kvp in preGameDecks)
        {
            if (IsSolverDeckValid(kvp.Value))
            {
                rankedDecks.Add(kvp);
            }
        }

        rankedDecks.Sort((a, b) => b.Value.chance.score.CompareTo(a.Value.chance.score));
        foreach (var kvp in rankedDecks)
        {
            if (kvp.Key != preferredDeckId && kvp.Key != fallbackDeckId)
            {
                yield return kvp.Key;
            }
        }

        for (var deckIdx = 0; deckIdx <= 4; deckIdx++)
        {
            if (deckIdx == preferredDeckId || deckIdx == fallbackDeckId)
            {
                continue;
            }

            if (preGameDecks.ContainsKey(deckIdx))
            {
                continue;
            }

            if (IsProfileDeckSelectable(deckIdx))
            {
                yield return deckIdx;
            }
        }
    }

    private bool IsDeckSelectableLocked(int deckId)
    {
        if (deckId < 0 || deckId > 4)
        {
            return false;
        }

        if (preGameDecks.TryGetValue(deckId, out var deckData))
        {
            return IsSolverDeckValid(deckData);
        }

        return IsProfileDeckSelectable(deckId);
    }

    private static bool IsSolverDeckValid(DeckData deckData) =>
        deckData?.solverDeck != null && deckData.solverDeck.GetDeckState() == ETriadDeckState.Valid;

    private bool IsProfileDeckSelectable(int deckId)
    {
        if (profileGS == null || profileGS.HasErrors)
        {
            return false;
        }

        var profileDecks = profileGS.GetPlayerDecks();
        return profileDecks != null && deckId >= 0 && deckId < profileDecks.Length && profileDecks[deckId] != null;
    }

    private int GetFirstProfileDeckIndex()
    {
        if (profileGS == null || profileGS.HasErrors)
        {
            return -1;
        }

        var profileDecks = profileGS.GetPlayerDecks();
        if (profileDecks == null)
        {
            return -1;
        }

        for (var deckIdx = 0; deckIdx < profileDecks.Length; deckIdx++)
        {
            if (profileDecks[deckIdx] != null)
            {
                return deckIdx;
            }
        }

        return -1;
    }

    // deck selection
    public class DeckData
    {
        public SolverResult chance;
        public int id;
        public string name;
        public TriadDeck solverDeck;
    }

    private class DeckSolverContext
    {
        public SolveDeckDelegate callback;
        public int deckId;
        public TriadGameSimulationState gameState;
        public int passId;
        public TriadGameSolver solver;
    }
}
