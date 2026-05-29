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
        try
        {
            var uiState = UIState.Instance();
            if (uiState == null)
                return false;

            return uiState->IsTripleTriadCardUnlocked((ushort)cardId);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "IsTripleTriadCardUnlocked failed for card {CardId}", cardId);
            return false;
        }
    }
}
