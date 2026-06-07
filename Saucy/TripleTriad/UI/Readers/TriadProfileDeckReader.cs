using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Text;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.GoldSaucerModule;

namespace Saucy.TripleTriad.UI;

public unsafe class TriadProfileDeckReader
{
    public bool HasErrors { get; private set; }

    public PlayerDeck?[]? GetPlayerDecks()
    {
        if (HasErrors)
        {
            return null;
        }

        try
        {
            var uiModule = (Svc.GameGui != null) ? (UIModule*)Svc.GameGui.GetUIModule().Address : null;
            var gsModule = (uiModule != null) ? uiModule->GetGoldSaucerModule() : null;

            if (gsModule != null)
            {
                static PlayerDeck? ConvertToPlayerDeck(TripleTriadDeck* deckPtr, int deckId)
                {
                    if (deckPtr == null)
                    {
                        return null;
                    }

                    var deckName = Encoding.UTF8.GetString(deckPtr->Name).Trim('\0');
                    if (string.IsNullOrWhiteSpace(deckName))
                    {
                        return null;
                    }

                    var deckOb = new PlayerDeck
                    {
                        id = deckId, name = deckName
                    };

                    for (var idx = 0; idx < 5; idx++)
                    {
                        deckOb.cardIds[idx] = deckPtr->Cards[idx];
                    }

                    return deckOb;
                }

                return
                [
                    ConvertToPlayerDeck(gsModule->GetDeck(0), 0),
                    ConvertToPlayerDeck(gsModule->GetDeck(1), 1),
                    ConvertToPlayerDeck(gsModule->GetDeck(2), 2),
                    ConvertToPlayerDeck(gsModule->GetDeck(3), 3),
                    ConvertToPlayerDeck(gsModule->GetDeck(4), 4)
                ];
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Failed to read GS profile data, turning reader off");
            HasErrors = true;
        }

        return null;
    }

    public bool TryWritePlayerDeck(int deckIdx, ushort[] cardIds, string? deckName = null)
    {
        if (HasErrors || deckIdx < 0 || deckIdx > 4 || cardIds == null || cardIds.Length != 5)
        {
            return false;
        }

        for (var idx = 0; idx < 5; idx++)
        {
            if (cardIds[idx] == 0)
            {
                return false;
            }
        }

        try
        {
            var uiModule = (Svc.GameGui != null) ? (UIModule*)Svc.GameGui.GetUIModule().Address : null;
            var gsModule = (uiModule != null) ? uiModule->GetGoldSaucerModule() : null;
            if (gsModule == null)
            {
                return false;
            }

            var deckPtr = gsModule->GetDeck(deckIdx);
            if (deckPtr == null)
            {
                return false;
            }

            for (var idx = 0; idx < 5; idx++)
            {
                deckPtr->Cards[idx] = cardIds[idx];
            }

            if (!string.IsNullOrWhiteSpace(deckName))
            {
                WriteDeckName(deckPtr, deckName.Trim());
            }

            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Failed to write GS profile deck {0}", deckIdx);
            HasErrors = true;
            return false;
        }
    }

    private static void WriteDeckName(TripleTriadDeck* deckPtr, string deckName)
    {
        Span<byte> nameBytes = stackalloc byte[48];
        nameBytes.Clear();
        var encoded = Encoding.UTF8.GetBytes(deckName);
        var copyLen = Math.Min(encoded.Length, 47);
        if (copyLen > 0)
        {
            encoded.AsSpan(0, copyLen).CopyTo(nameBytes);
        }

        for (var idx = 0; idx < 48; idx++)
        {
            deckPtr->Name[idx] = nameBytes[idx];
        }
    }

    public class PlayerDeck
    {
        public ushort[] cardIds = new ushort[5];
        public int id;
        public string name = string.Empty;
    }
}
