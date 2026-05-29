using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;

namespace TriadBuddyPlugin;

internal static class TriadMemoryReads
{
    public static bool IsAvailable =>
        Svc.ClientState.IsLoggedIn && Svc.Objects.LocalPlayer != null;

    public static unsafe bool TryIsCardOwned(int cardId)
    {
        if (cardId <= 0)
            return false;

        try
        {
            var uiState = UIState.Instance();
            if (uiState == null)
                return false;

            return uiState->UnlockedTripleTriadCardsBitArray.Get(cardId);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "TryIsCardOwned failed for card {CardId}", cardId);
            return false;
        }
    }

    public static unsafe bool TryIsNpcBeatenOnce(int triadSheetRowId)
    {
        // TripleTriad sheet row ids (e.g. 0x230002+); not TripleTriadResident indices.
        if (triadSheetRowId < 0x230002)
            return false;

        try
        {
            var uiState = UIState.Instance();
            if (uiState == null)
                return false;

            return uiState->IsTripleTriadNpcBeaten((uint)triadSheetRowId);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "TryIsNpcBeatenOnce failed for triad {TriadId}", triadSheetRowId);
            return false;
        }
    }
}
