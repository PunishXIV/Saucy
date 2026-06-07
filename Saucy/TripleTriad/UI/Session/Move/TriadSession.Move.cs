#nullable disable
using System;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.UI;

public partial class TriadSession
{
    public void UpdateGame(UIStateTriadGame stateOb)
    {
        status = Status.NoErrors;

        TriadBoardScanner.GameState screenOb = null;
        if (stateOb != null)
        {
            var parseCtx = new GameUIParser();
            screenOb = stateOb.ToTriadScreenState(parseCtx);
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
            currentNpc = null;
        }

        if (currentNpc != null)
        {
            lastGameNpc = currentNpc;
        }

        pauseOptimizerForActiveTriad = stateOb != null && TriadUiState.IsBoardVisible();
        if (pauseOptimizerForActiveTriad)
        {
            if (!activeTriadBoardWorkSuspended)
            {
                activeTriadBoardWorkSuspended = true;
                SuspendBackgroundDeckWorkForActiveMatch();
            }
        }
        else
        {
            if (activeTriadBoardWorkSuspended)
            {
                activeTriadBoardWorkSuspended = false;
                FlushDeferredOptimizerProfileWrite();
            }
        }

        UpdateDeckOptimizerPause();

        var npcForGame = ResolveNpcForGame(stateOb);
        if (npcForGame != null &&
            screenOb != null &&
            screenOb.turnState == TriadBoardScanner.ETurnState.Active &&
            stateOb != null &&
            !stateOb.isPvP)
        {
            var updateFlags = DebugScreenMemory.OnNewScan(screenOb, npcForGame);
            if (updateFlags != TriadGameScreenMemory.EUpdateFlags.None)
            {
                if (DebugScreenMemory.deckBlue != null &&
                    DebugScreenMemory.gameState != null &&
                    DebugScreenMemory.gameSolver != null)
                {
                    ApplySolverMove();
                }
                else
                {
                    ClearMove();
                }
            }
        }
        else if (hasMove)
        {
            ClearMove();
        }
    }

    public void ResetForNewMatch() => ClearMove();

    private void ApplySolverMove()
    {
        if (DebugScreenMemory.deckBlue == null ||
            DebugScreenMemory.gameState == null ||
            DebugScreenMemory.gameSolver == null)
        {
            ClearMove();
            return;
        }

        if (moveCalcInFlight)
        {
            return;
        }

        var solver = DebugScreenMemory.gameSolver;
        var state = DebugScreenMemory.gameState;
        var forcedCardIdx = state.forcedCardIdx;
        var calcGeneration = ++moveCalcGeneration;
        moveCalcInFlight = true;
        pauseOptimizerForSolver = true;
        UpdateDeckOptimizerPause();

        _ = Task.Run(() =>
        {
            var bestCardIdx = -1;
            var bestBoardPos = -1;
            try
            {
                solver.FindNextMove(state, out bestCardIdx, out bestBoardPos, out var _);
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "[Saucy] Live move solver failed");
            }

            Svc.Framework.Run(() => CommitSolverMove(calcGeneration, bestCardIdx, bestBoardPos, forcedCardIdx));
        });
    }

    private void CommitSolverMove(int calcGeneration, int bestCardIdx, int bestBoardPos, int forcedCardIdx)
    {
        moveCalcInFlight = false;
        pauseOptimizerForSolver = false;
        UpdateDeckOptimizerPause();

        if (calcGeneration != moveCalcGeneration ||
            DebugScreenMemory.gameState == null ||
            DebugScreenMemory.gameSolver == null)
        {
            return;
        }

        var hadMove = hasMove;
        moveCardIdx = bestCardIdx;
        moveBoardIdx = moveCardIdx < 0 ? -1 : bestBoardPos;

        if (forcedCardIdx >= 0 && moveCardIdx != forcedCardIdx)
        {
            moveCardIdx = forcedCardIdx;
        }

        hasMove = moveCardIdx >= 0 && moveBoardIdx >= 0;
        if (!hasMove)
        {
            TryApplyFallbackMove();
        }

        if (hasMove && !hadMove)
        {
            moveReadyUtc = DateTime.UtcNow;
        }
    }

    private void ClearMove()
    {
        moveCalcGeneration++;
        hasMove = false;
        moveCardIdx = -1;
        moveBoardIdx = -1;
        moveReadyUtc = null;
    }

    internal void InvalidatePendingMoveCalc()
    {
        moveCalcGeneration++;
        moveCalcInFlight = false;
        pauseOptimizerForSolver = false;
        hasMove = false;
        moveCardIdx = -1;
        moveBoardIdx = -1;
        moveReadyUtc = null;
    }

    private TriadNpc ResolveNpcForGame(UIStateTriadGame stateOb)
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
            var fromPrep = ctx.ParseNpc(uiReaderPrep.cachedState.npc, false) ??
                           ctx.ParseNpcNameStart(uiReaderPrep.cachedState.npc, false);
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
    }

    private void EnsureScreenMods(TriadBoardScanner.GameState screenOb)
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
}
