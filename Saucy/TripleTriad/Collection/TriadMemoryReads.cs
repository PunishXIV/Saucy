using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;

namespace Saucy.TripleTriad;

public static class TriadMemoryReads
{
    public static bool IsAvailable =>
        Svc.ClientState.IsLoggedIn && Svc.Objects.LocalPlayer != null;

    public static unsafe bool TryIsCardOwned(int cardId)
    {
        if (cardId <= 0 || cardId > ushort.MaxValue)
            return false;

        try
        {
            var uiState = UIState.Instance();
            if (uiState == null)
                return false;

            // Prefer the game helper over raw bit-array indexing (row id vs bit index can diverge).
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
