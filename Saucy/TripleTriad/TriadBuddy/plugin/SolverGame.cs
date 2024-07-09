using FFTriadBuddy;
using MgAl2O4.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TriadBuddyPlugin
{
    public class SolverGame
    {
        public enum Status
        {
            NoErrors,
            FailedToParseCards,
            FailedToParseRules,
            FailedToParseNpc,
        }

        private TriadGameScreenMemory screenMemory = new();
        public TriadGameScreenMemory? DebugScreenMemory => screenMemory;

        private ScannerTriad.GameState? cachedScreenState;
        public ScannerTriad.GameState? DebugScreenState => cachedScreenState;

        public TriadNpc? lastGameNpc;
        public TriadNpc? currentNpc;
        public TriadCard? moveCard => screenMemory.deckBlue?.GetCard(moveCardIdx);
        public int moveCardIdx;
        public int moveBoardIdx;
        public SolverResult moveWinChance;
        public bool hasMove;

        public Status status;
        public bool HasErrors => status != Status.NoErrors;

        public event Action<bool>? OnMoveChanged;

        public async void UpdateGame(UIStateTriadGame stateOb)
        {
            status = Status.NoErrors;

            ScannerTriad.GameState? screenOb = null;
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
            if (currentNpc != null &&
                screenOb != null && screenOb.turnState == ScannerTriad.ETurnState.Active &&
                stateOb != null && !stateOb.isPvP)
            {
                var updateFlags = screenMemory.OnNewScan(screenOb, currentNpc);
                if (updateFlags != TriadGameScreenMemory.EUpdateFlags.None)
                {
                    if (screenMemory.deckBlue != null && screenMemory.gameState != null && screenMemory.gameSolver != null)
                    {
#if DEBUG
                        // turn on verbose debugging when checking solver's behavior
                        screenMemory.gameSolver.agent.debugFlags = TriadGameAgent.DebugFlags.ShowMoveStart | TriadGameAgent.DebugFlags.ShowMoveDetails;
#endif // DEBUG

                        SolverUtils.solverDeckOptimize?.SetPauseForGameSolver(true);

                        var nextMoveInfo = await UpdateGameRunSolver();

                        hasMove = true;
                        moveCardIdx = nextMoveInfo.Item1;
                        moveBoardIdx = (moveCardIdx < 0) ? -1 : nextMoveInfo.Item2;
                        moveWinChance = nextMoveInfo.Item3;

                        var solverCardOb = screenMemory.deckBlue.GetCard(moveCardIdx);
                        if ((screenMemory.gameState.forcedCardIdx >= 0) && (moveCardIdx != screenMemory.gameState.forcedCardIdx))
                        {
                            // swap + chaos may cause selecting wrong instance of duplicated card?
                            // it really, really shouldn't unless solver's agent is broken

                            var forcedCardOb = screenMemory.deckBlue.GetCard(screenMemory.gameState.forcedCardIdx);

                            var solverCardDesc = solverCardOb != null ? solverCardOb.Name.GetCodeName() : "??";
                            var forcedCardDesc = forcedCardOb != null ? forcedCardOb.Name.GetCodeName() : "??";
                            Service.logger.Warning($"Solver selected card [{moveCardIdx}]:{solverCardDesc}, but game wants: [{screenMemory.gameState.forcedCardIdx}]:{forcedCardDesc} !");

                            moveCardIdx = screenMemory.gameState.forcedCardIdx;
                            solverCardOb = forcedCardOb;
                        }

                        Logger.WriteLine("  suggested move: [{0}] {1} {2} (expected: {3})",
                            moveBoardIdx, ETriadCardOwner.Blue,
                            solverCardOb != null ? solverCardOb.Name.GetCodeName() : "??",
                            moveWinChance.expectedResult);

                        SolverUtils.solverDeckOptimize?.SetPauseForGameSolver(false);
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
            screenMemory.gameSolver.FindNextMove(screenMemory.gameState, out int bestCardIdx, out int bestBoardPos, out var solverResult);
            return Task.FromResult(new Tuple<int, int, SolverResult>(bestCardIdx, bestBoardPos, solverResult));
        }

        public void UpdateKnownPlayerDeck(TriadDeck playerDeck)
        {
            screenMemory.UpdatePlayerDeck(playerDeck);
        }

        public (List<TriadCard>, List<TriadCard>) GetScreenRedDeckDebug()
        {
            var knownCards = new List<TriadCard>();
            var unknownCards = new List<TriadCard>();

            if (screenMemory != null && screenMemory.deckRed != null && screenMemory.deckRed.deck != null)
            {
                var deckInst = screenMemory.deckRed;
                if (deckInst.availableCardMask > 0)
                {
                    for (int Idx = 0; Idx < deckInst.cards.Length; Idx++)
                    {
                        bool bIsAvailable = (deckInst.availableCardMask & (1 << Idx)) != 0;
                        if (bIsAvailable)
                        {
                            TriadCard cardOb = deckInst.GetCard(Idx);
                            bool bIsKnownPool = deckInst.deck.knownCards.Contains(cardOb);

                            var listToUse = bIsKnownPool ? knownCards : unknownCards;
                            listToUse.Add(cardOb);
                        }
                    }
                }

                int visibleCardsMask = (deckInst.cards != null) ? ((1 << deckInst.cards.Length) - 1) : 0;
                bool hasHiddenCards = (deckInst.availableCardMask & ~visibleCardsMask) != 0;
                if (hasHiddenCards && deckInst.cards != null)
                {
                    for (int Idx = deckInst.cards.Length; Idx < 15; Idx++)
                    {
                        bool bIsAvailable = (deckInst.availableCardMask & (1 << Idx)) != 0;
                        if (bIsAvailable)
                        {
                            TriadCard cardOb = deckInst.GetCard(Idx);
                            bool bIsKnownPool = (deckInst.unknownPoolMask & (1 << Idx)) == 0;

                            var listToUse = bIsKnownPool ? knownCards : unknownCards;
                            listToUse.Add(cardOb);
                        }
                    }
                }
            }

            return (knownCards, unknownCards);
        }
    }
}