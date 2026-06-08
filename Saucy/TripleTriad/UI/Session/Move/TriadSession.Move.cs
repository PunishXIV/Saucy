#nullable disable
using System;
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

        if (currentNpc != null &&
            screenOb != null &&
            stateOb != null &&
            IsBlueTurnReadyForSolver(stateOb))
        {
            var updateFlags = DebugScreenMemory.OnNewScan(screenOb, currentNpc);
            if (updateFlags != TriadGameScreenMemory.EUpdateFlags.None)
            {
                if (DebugScreenMemory.deckBlue != null &&
                    DebugScreenMemory.gameState != null &&
                    DebugScreenMemory.gameSolver != null)
                {
                    RunSolverAndCommit();
                }
                else
                {
                    ClearMove();
                }
            }
        }
        else if (hasMove && (stateOb == null || !TriadTurnState.CanBlueAct(stateOb.move, stateOb.isPlayerTurn)))
        {
            ClearMove();
        }
    }

    public void ResetForNewMatch() => ClearMove();

    private void RunSolverAndCommit()
    {
        pauseOptimizerForSolver = true;
        UpdateDeckOptimizerPause();

        try
        {
            DebugScreenMemory.gameSolver.FindNextMove(
                DebugScreenMemory.gameState,
                out var bestCardIdx,
                out var bestBoardPos,
                out var _);

            var hadMove = hasMove;
            var newCardIdx = bestCardIdx;
            var newBoardIdx = newCardIdx < 0 ? -1 : bestBoardPos;

            // Forced-card override (mirrors upstream): under Swap+Chaos the solver can pick
            // a non-forced card; the game won't accept that, so force the forced index.
            var forcedCardIdx = DebugScreenMemory.gameState.forcedCardIdx;
            if (forcedCardIdx >= 0 && newCardIdx != forcedCardIdx)
            {
                newCardIdx = forcedCardIdx;
            }

            moveCardIdx = newCardIdx;
            moveBoardIdx = newBoardIdx;
            hasMove = moveCardIdx >= 0 && moveBoardIdx >= 0;
            if (hasMove && !hadMove)
            {
                moveReadyUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[Saucy] Live move solver failed");
            ClearMove();
        }
        finally
        {
            pauseOptimizerForSolver = false;
            UpdateDeckOptimizerPause();
        }
    }

    private static bool IsBlueTurnReadyForSolver(UIStateTriadGame stateOb) =>
        stateOb != null &&
        !stateOb.isPvP &&
        TriadTurnState.CanBlueAct(stateOb.move, stateOb.isPlayerTurn) &&
        !(TriadTurnState.IsBoardPickPhase(stateOb.move) && stateOb.turnBannerVisible);

    private void ClearMove()
    {
        hasMove = false;
        moveCardIdx = -1;
        moveBoardIdx = -1;
        moveReadyUtc = null;
    }

    internal void InvalidatePendingMoveCalc() => ClearMove();

    internal TriadNpc ResolveNpcForGame(UIStateTriadGame stateOb)
    {
        if (currentNpc != null)
        {
            lastGameNpc = currentNpc;
            return currentNpc;
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
}
