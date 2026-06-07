using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
namespace Saucy.TripleTriad;

public static class TriadMemoryReads
{
    public static bool IsAvailable
        => Svc.ClientState.IsLoggedIn && Svc.Objects.LocalPlayer != null;

    public static unsafe bool TryIsCardOwned(int cardId)
    {
        if (cardId is <= 0 or > ushort.MaxValue)
        {
            return false;
        }

        try
        {
            var uiState = UIState.Instance();
            if (uiState == null)
            {
                return false;
            }

            return uiState->IsTripleTriadCardUnlocked((ushort)cardId);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "TryIsCardOwned failed for card {CardId}", cardId);
            return false;
        }
    }

    public static unsafe bool TryIsNpcBeatenOnce(int triadSheetRowId)
    {
        if (triadSheetRowId < 0x230002)
        {
            return false;
        }

        try
        {
            var uiState = UIState.Instance();
            if (uiState == null)
            {
                return false;
            }

            return uiState->IsTripleTriadNpcBeaten((uint)triadSheetRowId);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "TryIsNpcBeatenOnce failed for triad {TriadId}", triadSheetRowId);
            return false;
        }
    }

    public static bool IsQuestCompleteOrUnneeded(uint questId) =>
        questId == 0 || QuestManager.IsQuestComplete(questId);
}
