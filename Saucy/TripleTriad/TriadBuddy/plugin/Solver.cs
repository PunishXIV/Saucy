using FFTriadBuddy;
using MgAl2O4.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TriadBuddyPlugin;

public class Solver
{
    public enum Status
    {
        NoErrors,
        FailedToParseCards,
        FailedToParseRules,
        FailedToParseNpc,
    }

    public UnsafeReaderProfileGS profileGS;

    // optimizer
    public TriadDeckOptimizer deckOptimizer = new();
    private bool pauseOptimizerForSolver = false;
    private bool pauseOptimizerForOptimizedEval = false;
    private bool pauseOptimizerForDeckEval = false;

    public TriadGameScreenMemory DebugScreenMemory { get; } = new();

    public ScannerTriad.GameState cachedScreenState;
    public ScannerTriad.GameState DebugScreenState => cachedScreenState;

    public TriadNpc lastGameNpc;
    public TriadNpc currentNpc;
    public TriadCard MoveCard => DebugScreenMemory.deckBlue?.GetCard(moveCardIdx);
    public int moveCardIdx;
    public int moveBoardIdx;
    public SolverResult moveWinChance;
    public bool hasMove;

    // deck selection
    public class DeckData
    {
        public string name;
        public int id;
        public TriadDeck solverDeck;
        public SolverResult chance;
    }

    public TriadNpc preGameNpc;
    public List<TriadGameModifier> preGameMods = [];
    public Dictionary<int, DeckData> preGameDecks = [];
    public float preGameProgress => (preGameDecks.Count > 0) ? (1.0f * preGameSolved / preGameDecks.Count) : 0.0f;
    public int preGameBestId = -1;
    private int preGameId = 0;
    private int preGameSolved = 0;
    private readonly object preGameLock = new();

    public Status status;
    public bool HasErrors => status != Status.NoErrors;
    public bool HasAllProfileDecksEmpty { get; private set; }

    public event Action<bool> OnMoveChanged;

    public Solver()
    {
        TriadGameSimulation.StaticInitialize();
    }

    public async void UpdateGame(UIStateTriadGame stateOb)
    {
        status = Status.NoErrors;

        ScannerTriad.GameState screenOb = null;
        if (stateOb != null)
        {
            var parseCtx = new GameUIParser();
            screenOb = stateOb.ToTriadScreenState(parseCtx);
            currentNpc = stateOb.ToTriadNpc(parseCtx);

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
        }

        if (currentNpc != null)
        {
            lastGameNpc = currentNpc;
        }

        cachedScreenState = screenOb;
        if (currentNpc != null && screenOb.turnState == ScannerTriad.ETurnState.Active && !stateOb.isPvP)
        {
            var updateFlags = DebugScreenMemory.OnNewScan(screenOb, currentNpc);
            if (updateFlags != TriadGameScreenMemory.EUpdateFlags.None)
            {
                if (DebugScreenMemory.deckBlue != null && DebugScreenMemory.gameState != null && DebugScreenMemory.gameSolver != null)
                {
#if DEBUG
                    // turn on verbose debugging when checking solver's behavior
                    DebugScreenMemory.gameSolver.agent.debugFlags = TriadGameAgent.DebugFlags.ShowMoveStart | TriadGameAgent.DebugFlags.ShowMoveDetails;
#endif // DEBUG

                    pauseOptimizerForSolver = true;
                    UpdateDeckOptimizerPause();

                    var nextMoveInfo = await UpdateGameRunSolver();

                    hasMove = true;
                    moveCardIdx = nextMoveInfo.Item1;
                    moveBoardIdx = (moveCardIdx < 0) ? -1 : nextMoveInfo.Item2;
                    moveWinChance = nextMoveInfo.Item3;

                    var solverCardOb = DebugScreenMemory.deckBlue.GetCard(moveCardIdx);
                    if ((DebugScreenMemory.gameState.forcedCardIdx >= 0) && (moveCardIdx != DebugScreenMemory.gameState.forcedCardIdx))
                    {
                        // swap + chaos may cause selecting wrong instance of duplicated card?
                        // it really, really shouldn't unless solver's agent is broken

                        var forcedCardOb = DebugScreenMemory.deckBlue.GetCard(DebugScreenMemory.gameState.forcedCardIdx);

                        var solverCardDesc = solverCardOb != null ? solverCardOb.Name.GetCodeName() : "??";
                        var forcedCardDesc = forcedCardOb != null ? forcedCardOb.Name.GetCodeName() : "??";
                        PluginLog.Warning($"Solver selected card [{moveCardIdx}]:{solverCardDesc}, but game wants: [{DebugScreenMemory.gameState.forcedCardIdx}]:{forcedCardDesc} !");

                        moveCardIdx = DebugScreenMemory.gameState.forcedCardIdx;
                        solverCardOb = forcedCardOb;
                    }

                    Logger.WriteLine("  suggested move: [{0}] {1} {2} (expected: {3})",
                        moveBoardIdx, ETriadCardOwner.Blue,
                        solverCardOb != null ? solverCardOb.Name.GetCodeName() : "??",
                        moveWinChance.expectedResult);

                    pauseOptimizerForSolver = false;
                    UpdateDeckOptimizerPause();
                }
                else
                {
                    hasMove = false;
                }

                OnMoveChanged?.Invoke(hasMove);
            }
        }
        else if (hasMove)
        {
            hasMove = false;
            OnMoveChanged?.Invoke(hasMove);
        }
    }

    private Task<Tuple<int, int, SolverResult>> UpdateGameRunSolver()
    {
        DebugScreenMemory.gameSolver.FindNextMove(DebugScreenMemory.gameState, out var bestCardIdx, out var bestBoardPos, out var solverResult);
        return Task.FromResult(new Tuple<int, int, SolverResult>(bestCardIdx, bestBoardPos, solverResult));
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

    public delegate void SolveDeckDelegate(SolverResult winChance);

    private class DeckSolverContext
    {
        public TriadGameSolver solver;
        public TriadGameSimulationState gameState;
        public SolveDeckDelegate callback;
        public int deckId;
        public int passId;
    }

    public void UpdateDecks(UIStateTriadPrep state)
    {
        // don't report status here, just log stuff out
        var parseCtx = new GameUIParser();

        var newPreGameNpc = parseCtx.ParseNpc(state.npc, false);
        if (newPreGameNpc == null)
        {
            parseCtx.OnFailedNpcSilent(state.npc);
        }

        var newPreGameMods = new List<TriadGameModifier>();
        foreach (var rule in state.rules)
        {
            var ruleOb = parseCtx.ParseModifier(rule, false);
            if (ruleOb is not null and not TriadGameModifierNone)
            {
                newPreGameMods.Add(ruleOb);
            }
        }

        lastGameNpc = newPreGameNpc;

        var canReadFromProfile = profileGS != null && !profileGS.HasErrors;
        var canProcessDecks = !parseCtx.HasErrors &&
            // case 1: it's play request screen, no deck info in ui, proceed only if profile reader is available
            ((state.decks.Count == 0 && canReadFromProfile) ||
            // case 2: it's deck selection screen, ui has deck info, proceed only if solved already (profile reader not available)
            (state.decks.Count > 0 && !canReadFromProfile));

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
            anyDeckOb ??= new TriadDeck(PlayerSettingsDB.Get().starterCards);
            DebugScreenMemory.UpdatePlayerDeck(anyDeckOb);

            foreach (var kvp in preGameDecks)
            {
                var deckSolver = new TriadGameSolver();
                deckSolver.InitializeSimulation(preGameMods);

                var gameState = deckSolver.StartSimulation(kvp.Value.solverDeck, preGameNpc.Deck, ETriadGameState.InProgressBlue);
                var calcContext = new DeckSolverContext() { solver = deckSolver, gameState = gameState, deckId = kvp.Value.id, passId = preGameId };

                void solverAction(object ctxOb)
                {
                    var ctx = ctxOb as DeckSolverContext;
                    ctx.solver.FindNextMove(ctx.gameState, out _, out _, out var solverResult);
                    OnSolvedDeck(ctx.passId, ctx.deckId, solverResult);
                }

                new TaskFactory().StartNew(solverAction, calcContext);
            }

            pauseOptimizerForDeckEval = preGameDecks.Count > 0;
            UpdateDeckOptimizerPause();
        }
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

        var deckData = new DeckData() { id = deckOb.id, name = deckOb.name };

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
            deckData = new DeckData() { id = deckOb.id, name = deckOb.name };

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
        var calcContext = new DeckSolverContext() { solver = deckSolver, gameState = gameState, callback = callback };

        pauseOptimizerForOptimizedEval = true;
        UpdateDeckOptimizerPause();

        void solverAction(object ctxOb)
        {
            var ctx = ctxOb as DeckSolverContext;
            ctx.solver.FindNextMove(ctx.gameState, out _, out _, out var solverResult);
            callback?.Invoke(solverResult);

            pauseOptimizerForOptimizedEval = false;
            UpdateDeckOptimizerPause();
        }

        new TaskFactory().StartNew(solverAction, calcContext);
    }

    private void UpdateDeckOptimizerPause()
    {
        deckOptimizer.SetPaused(pauseOptimizerForSolver || pauseOptimizerForDeckEval || pauseOptimizerForOptimizedEval);
    }
}
