using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Text;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.GoldSaucerModule;

namespace TriadBuddyPlugin
{
    public unsafe class UnsafeReaderProfileGS
    {
        public class PlayerDeck
        {
            public string name = string.Empty;
            public int id;
            public ushort[] cardIds = new ushort[5];
        }

        public bool HasErrors { get; private set; }

        public PlayerDeck?[]? GetPlayerDecks()
        {
            if (HasErrors)
            {
                // hard nope, reverse code again.
                return null;
            }

            try
            {
                // .text: 83 fa 09 77 1d 41 83 f8 04
                // SetCardInDeck(void* GSProfileData, uint deckIdx, uint cardIdx, ushort cardId)
                // 
                // GSProfileData = uiModule.vf29()
                //     e.g. used by GoldSaucerInfo addon in .text: 0f 94 c0 88 46 58 48 8b 74 24 38 48 83 c4 20
                //     SaveDeckToProfile(void* agentPtr)
                //
                //     5.58: addr = uiModulePtr + 0x90dd0, this function is just getter for member var holding pointer

                var uiModule = (Service.gameGui != null) ? (UIModule*)Service.gameGui.GetUIModule() : null;
                var gsModule = (uiModule != null) ? uiModule->GetGoldSaucerModule() : null;

                if (gsModule != null)
                {
                    PlayerDeck? ConvertToPlayerDeck(TripleTriadDeck* deckPtr, int deckId)
                    {
                        if (deckPtr != null &&
                            deckPtr->Cards[0] != 0 &&
                            deckPtr->Cards[1] != 0 &&
                            deckPtr->Cards[2] != 0 &&
                            deckPtr->Cards[3] != 0 &&
                            deckPtr->Cards[4] != 0)
                        {
                            var deckOb = new PlayerDeck()
                            {
                                id = deckId,
                                name = Encoding.UTF8.GetString(deckPtr->Name),
                            };

                            for (int idx = 0; idx < 5; idx++)
                            {
                                deckOb.cardIds[idx] = deckPtr->Cards[idx];
                            }

                            return deckOb;
                        }

                        return null;
                    };

                    // just 5 decks, no idea what other 5..9 are used for
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
                Service.logger.Error(ex, "Failed to read GS profile data, turning reader off");
                HasErrors = true;
            }

            return null;
        }
    }
}
