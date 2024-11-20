using FFTriadBuddy;
using MgAl2O4.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TriadBuddyPlugin
{
    public class SolverPreGameDecks
    {
        public UnsafeReaderProfileGS? profileGS;

        public class DeckData
        {
            public string name = string.Empty;
            public int id;
            public TriadDeck? solverDeck;
            public SolverResult chance;
        }

        public TriadNpc? preGameNpc;
        public List<TriadGameModifier> preGameMods = new();
        public Dictionary<int, DeckData> preGameDecks = new();
        public float preGameProgress => (preGameDecks.Count > 0) ? (1.0f * preGameSolved / preGameDecks.Count) : 0.0f;
        public int preGameBestId = -1;
        private int preGameId = 0;
        private int preGameSolved = 0;
        private bool preGameAllProfileDecksEmpty;
        private object preGameLock = new();

        public bool HasAllProfileDecksEmpty => preGameAllProfileDecksEmpty;

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
                if (ruleOb != null && !(ruleOb is TriadGameModifierNone))
                {
                    newPreGameMods.Add(ruleOb);
                }
            }

            // keep game solver up to date
            if (SolverUtils.solverGame != null)
            {
                SolverUtils.solverGame.lastGameNpc = newPreGameNpc;
            }

            bool canReadFromProfile = profileGS != null && !profileGS.HasErrors;
            bool canProcessDecks = !parseCtx.HasErrors &&
                // case 1: it's play request screen, no deck info in ui, proceed only if profile reader is available
                ((state.decks.Count == 0 && canReadFromProfile) ||
                // case 2: it's deck selection screen, ui has deck info, proceed only if solved already (profile reader not available)
                (state.decks.Count > 0 && !canReadFromProfile));

            if (canProcessDecks)
            {
                var profileDecks = (canReadFromProfile && profileGS != null) ? profileGS.GetPlayerDecks() : null;
                int numDecks = (profileDecks != null) ? profileDecks.Length : state.decks.Count;
                var newPreGameDecks = new Dictionary<int, DeckData>();

                TriadDeck? anyDeckOb = null;
                for (int deckIdx = 0; deckIdx < numDecks; deckIdx++)
                {
                    parseCtx.Reset();

                    var deckData = (profileDecks != null) ?
                        ParseDeckDataFromProfile(profileDecks[deckIdx], parseCtx) :
                        ParseDeckDataFromUI(state.decks[deckIdx], parseCtx);

                    if (!parseCtx.HasErrors && deckData != null)
                    {
                        newPreGameDecks.Add(deckData.id, deckData);
                        anyDeckOb = deckData.solverDeck;
                    }
                }

                // check if actually have something to do
                bool needsDeckEval = IsDeckEvalDataChanged(newPreGameNpc, newPreGameMods, newPreGameDecks);
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
                preGameAllProfileDecksEmpty = (profileDecks != null) && (anyDeckOb == null);
                if (anyDeckOb == null)
                {
                    anyDeckOb = new TriadDeck(PlayerSettingsDB.Get().starterCards);
                }

                SolverUtils.solverGame?.UpdateKnownPlayerDeck(anyDeckOb);

                foreach (var kvp in preGameDecks)
                {
                    var deckSolver = new TriadGameSolver();
                    deckSolver.InitializeSimulation(preGameMods);

                    if (kvp.Value.solverDeck == null || preGameNpc == null || preGameNpc.Deck == null)
                    {
                        continue;
                    }

                    var gameState = deckSolver.StartSimulation(kvp.Value.solverDeck, preGameNpc.Deck, ETriadGameState.InProgressBlue);
                    var calcContext = new SolverUtils.DeckSolverContext() { solver = deckSolver, gameState = gameState, deckId = kvp.Value.id, passId = preGameId };

                    Action<object?> solverAction = (ctxOb) =>
                    {
                        var ctx = ctxOb as SolverUtils.DeckSolverContext;
                        if (ctx != null && ctx.solver != null && ctx.gameState != null)
                        {
                            ctx.solver.FindNextMove(ctx.gameState, out _, out _, out var solverResult);
                            OnSolvedDeck(ctx.passId, ctx.deckId, solverResult);
                        }
                    };

                    new TaskFactory().StartNew(solverAction, calcContext);
                }

                SolverUtils.solverDeckOptimize?.SetPauseForPreGameDecks(preGameDecks.Count > 0);
            }
        }

        private bool IsDeckEvalDataChanged(TriadNpc? testNpc, List<TriadGameModifier> testMods, Dictionary<int, DeckData> testDecks)
        {
            if (testNpc != preGameNpc)
            {
                return true;
            }

            if (testMods.Count != preGameMods.Count)
            {
                return true;
            }

            for (int idx = 0; idx < testMods.Count; idx++)
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
                if (!preGameDecks.TryGetValue(kvp.Key, out DeckData? deckData))
                {
                    return true;
                }

                if (deckData.solverDeck != null &&
                    kvp.Value.solverDeck != null &&
                    !deckData.solverDeck.Equals(kvp.Value.solverDeck))
                {
                    return true;
                }
            }

            return false;
        }

        private DeckData? ParseDeckDataFromProfile(UnsafeReaderProfileGS.PlayerDeck? deckOb, GameUIParser ctx)
        {
            // empty profile decks will result in nulls here
            if (deckOb == null)
            {
                return null;
            }

            var deckData = new DeckData() { id = deckOb.id, name = deckOb.name };

            var cards = new TriadCard?[5];
            for (int cardIdx = 0; cardIdx < 5; cardIdx++)
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

        private DeckData? ParseDeckDataFromUI(UIStateTriadPrepDeck deckOb, GameUIParser ctx)
        {
            // empty UI decks are valid objects, but their card data is empty (handled by ctx)
            // do quick filter pass looking for nulls in card slots too

            int numValidCards = 0;
            for (int cardIdx = 0; cardIdx < 5; cardIdx++)
            {
                numValidCards += string.IsNullOrEmpty(deckOb.cardTexPaths[cardIdx]) ? 0 : 1;
            }

            DeckData? deckData = null;
            if (numValidCards == 5)
            {
                deckData = new DeckData() { id = deckOb.id, name = deckOb.name };

                var cards = new TriadCard?[5];
                for (int cardIdx = 0; cardIdx < 5; cardIdx++)
                {
                    cards[cardIdx] = ctx.ParseCardByTexture(deckOb.cardTexPaths[cardIdx]);
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
                    int bestId = -1;
                    foreach (var kvp in preGameDecks)
                    {
                        float testScore = kvp.Value.chance.score;
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
                            if (bestDeckData.solverDeck != null)
                            {
                                SolverUtils.solverGame?.UpdateKnownPlayerDeck(bestDeckData.solverDeck);
                            }
                        }
                    }

                    preGameBestId = bestId;

                    SolverUtils.solverDeckOptimize?.SetPauseForPreGameDecks(preGameSolved < preGameDecks.Count);
                }
            }
        }
    }
}