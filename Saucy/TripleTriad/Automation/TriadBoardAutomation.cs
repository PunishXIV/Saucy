using ECommons.Automation;
using System;
namespace Saucy.TripleTriad;

internal static unsafe class TriadBoardAutomation
{
    private static bool boardActiveForSnapshot;

    public static bool Tick()
    {
        if (!TriadUiState.IsBoardVisible())
        {
            boardActiveForSnapshot = false;
            return false;
        }

        if (!TriadLocalClientStructs.TryGetBoard(out var triadAddon, false))
        {
            return false;
        }

        if (!boardActiveForSnapshot)
        {
            boardActiveForSnapshot = true;
            TriadRun.ResetForNewMatch();
            TriadCardFarmSession.ResetDropVerification();
            TriadRewardDropTracker.SnapshotAtMatchStart();
        }

        uiReaderGame.SyncCurrentFromAddon((nint)triadAddon);

        var state = uiReaderGame.currentState;
        var turnState = (byte)triadAddon->TurnState;
        TriadRun.UpdateGame(state);

        var canPlace = state != null &&
                       TriadTurnState.CanBlueAct(turnState, state.isPlayerTurn) &&
                       !(TriadTurnState.IsBoardPickPhase(turnState) && state.turnBannerVisible);

        if (canPlace &&
            TriadRun.hasMove &&
            TriadRun.IsMoveReadyForPlacement() &&
            TriadRun.moveCardIdx >= 0 &&
            TriadRun.moveBoardIdx >= 0)
        {
            PlaceCard(TriadRun.moveCardIdx, TriadRun.moveBoardIdx);
            return true;
        }

        return false;
    }

    private static bool PlaceCard(int which, int slot)
    {
        try
        {
            if (!TriadLocalClientStructs.TryGetBoard(out var addon, false))
            {
                return false;
            }

            Callback.Fire(&addon->AtkUnitBase, true, 14, (uint)slot + ((uint)which << 16));
            addon->AtkUnitBase.Update(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadBoardAutomation] PlaceCard failed");
            return false;
        }
    }
}
