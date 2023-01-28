using Dalamud.Game.Gui;
using Dalamud.Logging;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Runtime.InteropServices;

namespace TriadBuddyPlugin
{
    public unsafe class UnsafeReaderProfileGS
    {
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate GSProfileData* GetGSProfileDataDelegate(IntPtr uiObject);

        [StructLayout(LayoutKind.Explicit, Size = 0x3A)]
        private unsafe struct GSProfileDeck
        {
            [FieldOffset(0x0)] public fixed byte NameBuffer[32];    // 15 chars + null, can it be unicode? (JA/KO/ZH)
            [FieldOffset(0x30)] public ushort Card0;
            [FieldOffset(0x32)] public ushort Card1;
            [FieldOffset(0x34)] public ushort Card2;
            [FieldOffset(0x36)] public ushort Card3;
            [FieldOffset(0x38)] public ushort Card4;
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x284)]           // it's more than that, but i'm not reading anything else so meh, it's good
        private unsafe struct GSProfileData
        {
            [FieldOffset(0x30)] public fixed byte NameBuffer[8];    // "GS.DAT"
            [FieldOffset(0x40)] public GSProfileDeck Deck0;
            [FieldOffset(0x7A)] public GSProfileDeck Deck1;
            [FieldOffset(0xB4)] public GSProfileDeck Deck2;
            [FieldOffset(0xEE)] public GSProfileDeck Deck3;
            [FieldOffset(0x128)] public GSProfileDeck Deck4;
            [FieldOffset(0x162)] public GSProfileDeck Deck5;
            [FieldOffset(0x19C)] public GSProfileDeck Deck6;
            [FieldOffset(0x1D6)] public GSProfileDeck Deck7;
            [FieldOffset(0x210)] public GSProfileDeck Deck8;
            [FieldOffset(0x24A)] public GSProfileDeck Deck9;

            // +0x2B4: 8 bytes about card being viewed already?
        }

        public class PlayerDeck
        {
            public string name;
            public int id;
            public ushort[] cardIds = new ushort[5];
        }

        private readonly GameGui gameGui;
        public bool HasErrors { get; private set; }

        public UnsafeReaderProfileGS(GameGui gameGui)
        {
            this.gameGui = gameGui;
        }

        public PlayerDeck[] GetPlayerDecks()
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

                var uiModulePtr = (gameGui != null) ? gameGui.GetUIModule() : IntPtr.Zero;
                if (uiModulePtr != IntPtr.Zero)
                {
                    // would be a nice place to use gameGui.address.GetVirtualFunction<> :(
                    var getGSProfileDataPtr = new IntPtr(((UIModule*)uiModulePtr)->vfunc[29]);
                    var getGSProfileData = Marshal.GetDelegateForFunctionPointer<GetGSProfileDataDelegate>(getGSProfileDataPtr);

                    var profileData = getGSProfileData(uiModulePtr);

                    PlayerDeck ConvertToPlayerDeck(GSProfileDeck deckMem, int deckId)
                    {
                        if (deckMem.Card0 != 0 && deckMem.Card1 != 0 && deckMem.Card2 != 0 && deckMem.Card3 != 0 && deckMem.Card4 != 0)
                        {
                            PlayerDeck deckOb = new() { id = deckId };

                            deckOb.name = MemoryHelper.ReadStringNullTerminated(new IntPtr(deckMem.NameBuffer));
                            deckOb.cardIds[0] = deckMem.Card0;
                            deckOb.cardIds[1] = deckMem.Card1;
                            deckOb.cardIds[2] = deckMem.Card2;
                            deckOb.cardIds[3] = deckMem.Card3;
                            deckOb.cardIds[4] = deckMem.Card4;

                            return deckOb;
                        }

                        return null;
                    };

                    // just 5 decks, no idea what other 5..9 are used for
                    return new PlayerDeck[]
                    {
                        ConvertToPlayerDeck(profileData->Deck0, 0),
                        ConvertToPlayerDeck(profileData->Deck1, 1),
                        ConvertToPlayerDeck(profileData->Deck2, 2),
                        ConvertToPlayerDeck(profileData->Deck3, 3),
                        ConvertToPlayerDeck(profileData->Deck4, 4)
                    };
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to read GS profile data, turning reader off");
                HasErrors = true;
            }

            return null;
        }
    }
}
